using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using POOC.Helpers;
using System.Globalization;

[Authorize]
public class LoanController : Controller
{
    private readonly ApplicationDbContext _context;
    private static readonly CultureInfo ThaiCulture = new("th-TH");

    private List<LoanSchedule> CalculateLoan(double amount, double rate, int months)
    {
        var list = new List<LoanSchedule>();
        double balance = amount; // ประกาศไว้ด้านบนสุดเพื่อให้ใช้ได้ทั้งสองเงื่อนไข

        // --- กรณีไม่มีดอกเบี้ย (0%) ---
        if (rate == 0)
        {
            double payment = Math.Round(amount / months, 2); // ปัดเศษต่อเดือน
            for (int i = 1; i <= months; i++)
            {
                if (i == months) {
                    payment = balance; // งวดสุดท้ายจ่ายเท่ากับยอดคงเหลือที่เหลืออยู่จริง
                }
        
                balance -= payment;
                list.Add(new LoanSchedule {
                    Installment = i,
                    Payment = payment,
                    Principal = payment,
                    Interest = 0,
                    Balance = Math.Max(0, balance)
                });
            }
            return list;
        }
        // --- กรณีมีดอกเบี้ย ---
        double monthlyRate = rate / 100 / 12;
        double paymentFormula = amount * monthlyRate / (1 - Math.Pow(1 + monthlyRate, -months));
    
        for (int i = 1; i <= months; i++)
        {
            double interest = balance * monthlyRate;
            double principal = paymentFormula - interest;
            balance -= principal;
        
            list.Add(new LoanSchedule
            {
                Installment = i,
                Payment = paymentFormula,
                Principal = principal,
                Interest = interest,
                Balance = balance < 0.01 ? 0 : balance
            });
        }
        return list;
    }

    public LoanController(ApplicationDbContext context)
    {
        _context = context;
    }
    private string GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private static double GetEffectivePaidAmount(LoanDetail detail) =>
        detail.IsPaid && detail.PaidAmount <= 0 ? detail.Payment : detail.PaidAmount;

    private static double GetLoanPaidAmount(Loan loan) => loan.LoanDetails.Sum(GetEffectivePaidAmount);

    private static double GetLoanTotalDue(Loan loan) => loan.LoanDetails.Sum(x => x.Payment);

    private static double GetRemainingPayment(LoanDetail detail) => Math.Max(0, detail.Payment - GetEffectivePaidAmount(detail));

    private static string GetDisplayStatus(Loan loan)
    {
        if (loan.Status == "Cancelled" || loan.Status == "Rejected" || loan.Status == "Pending" || loan.Status == "Closed")
            return loan.Status;

        var totalDue = GetLoanTotalDue(loan);
        var paidAmount = GetLoanPaidAmount(loan);

        if (totalDue > 0 && paidAmount >= totalDue - 0.01)
            return "Closed";

        if (paidAmount > 0)
            return "PartialPaid";

        return "Active";
    }

    private static string GetStatusText(string status) => status switch
    {
        "Pending" => "รออนุมัติ",
        "Active" => "อนุมัติแล้ว",
        "PartialPaid" => "ชำระบางส่วน",
        "Overdue"     => "ผิดนัดชำระ",
        "Closed" => "ปิดบัญชี",
        "Cancelled" => "ยกเลิก",
        "Rejected" => "ไม่อนุมัติ",
        _ => status
    };

    private void RefreshLoanStatus(Loan loan)
    {
        if (loan.Status is "Cancelled" or "Rejected" or "Pending")
            return;

        var totalDue  = GetLoanTotalDue(loan);
        var paidAmount = GetLoanPaidAmount(loan);

        if (paidAmount >= totalDue - 0.01)
        {
            loan.Status = "Closed";
            loan.ClosedDate = DateTime.Now;
        }
        else
        {
            // เช็ค overdue จาก LoanDetail ที่ยังไม่จ่ายและเลย DueDate
            var today = DateTime.Now.Date;
            var startDate = new DateTime(loan.CreatedDate.Year, loan.CreatedDate.Month, 1).AddMonths(1);
            bool hasOverdue = loan.LoanDetails
                .Where(d => !d.IsPaid)
                .Any(d => startDate.AddMonths(d.Installment - 1) < today);

            loan.Status = hasOverdue ? "Overdue" : (paidAmount > 0 ? "PartialPaid" : "Active");
            loan.ClosedDate = null;
        }
    }

    [HttpGet]
    public IActionResult GetLoanSchedule(int memberId, double amount, double rate, int months)
    {
        var schedule = CalculateLoan(amount, rate, months);

        return Json(schedule);
    }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult Create([FromBody] LoanRequest model)
    {
        if (model == null)
        {
            return Json(new { success = false, message = "ไม่มีข้อมูล" });
        }
        if (model.Amount <= 0 || model.Months <= 0)
        {
            return Json(new { success = false, message = "กรุณากรอกข้อมูลให้ถูกต้อง" });
        }

        if (model.Rate < 0)
        {
            return Json(new { success = false, message = "ดอกเบี้ยต้องไม่ติดลบ" });
        }
        var userId = GetCurrentUserId();
        var loan = new Loan
        {
            MemberId = model.MemberId,
            Amount = model.Amount,
            Rate = model.Rate,
            Months = model.Months,
            OwnerId = userId,
            Status = "Pending",
            GuarantorName = model.GuarantorName,
            GuarantorPhone = model.GuarantorPhone,
            GuarantorAddress = model.GuarantorAddress
        };
        Console.WriteLine("SAVE => MemberId: " + model.MemberId);
        _context.Loans.Add(loan);
        _context.SaveChanges();

        var schedule = CalculateLoan(model.Amount, model.Rate, model.Months);

        var details = schedule.Select(item => new LoanDetail
        {
            LoanId = loan.Id,
            Installment = item.Installment,
            Payment = item.Payment,
            Principal = item.Principal,
            Interest = item.Interest,
            Balance = item.Balance
        }).ToList();

        _context.LoanDetails.AddRange(details);
        AddAuditLog("Create", "Loan", loan.Id, $"สร้างคำขอเงินกู้ MemberId={model.MemberId} ยอด {model.Amount:N2} บาท รออนุมัติ");
        _context.SaveChanges();

        return Json(new { success = true });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult Approve([FromBody] LoanActionRequest model)
    {
        if (model == null || model.LoanId <= 0)
            return Json(new { success = false, message = "ข้อมูลไม่ถูกต้อง" });

        var loan = _context.Loans.FirstOrDefault(x => x.Id == model.LoanId);
        if (loan == null)
            return Json(new { success = false, message = "ไม่พบสัญญาเงินกู้" });

        if (loan.Status == "Cancelled" || loan.Status == "Closed")
            return Json(new { success = false, message = "สถานะสัญญานี้ไม่สามารถอนุมัติได้" });

        loan.Status = "Active";
        loan.ApprovedDate = DateTime.Now;
        loan.ApprovedBy = GetCurrentUserId();
        AddAuditLog("Approve", "Loan", loan.Id, $"อนุมัติเงินกู้ LoanId={loan.Id}");
        _context.SaveChanges();

        return Json(new { success = true });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult CloseEarly([FromBody] LoanActionRequest model)
    {
        if (model == null || model.LoanId <= 0)
            return Json(new { success = false, message = "ข้อมูลไม่ถูกต้อง" });

        var loan = _context.Loans
            .Include(x => x.LoanDetails)
            .FirstOrDefault(x => x.Id == model.LoanId);

        if (loan == null)
            return Json(new { success = false, message = "ไม่พบสัญญาเงินกู้" });

        if (loan.Status == "Pending")
            return Json(new { success = false, message = "กรุณาอนุมัติเงินกู้ก่อนปิดบัญชี" });

        if (loan.Status == "Closed")
            return Json(new { success = false, message = "สัญญานี้ปิดบัญชีแล้ว" });

        foreach (var detail in loan.LoanDetails.Where(x => !x.IsPaid))
        {
            detail.PaidAmount = detail.Payment;
            detail.IsPaid = true;
            detail.PaidDate = DateTime.Now;
        }

        loan.Status = "Closed";
        loan.ClosedDate = DateTime.Now;
        AddAuditLog("CloseEarly", "Loan", loan.Id, $"ปิดบัญชีเงินกู้ก่อนกำหนด LoanId={loan.Id}");
        _context.SaveChanges();

        return Json(new { success = true });
    }
    public IActionResult ByMember(int id)
    {
        var member = _context.Members.FirstOrDefault(x => x.Id == id);

        if (member == null)
        return NotFound();

        var loans = _context.Loans
            .Where(x => x.MemberId == id)
            .Include(x => x.LoanDetails)
            .ToList();

        var vm = new MemberViewModel
        {
            Members = new List<Member> { member },
            Loans = loans
        };

        ViewBag.MemberName = member?.FirstName + " " + member?.LastName;

        return View("~/Views/Home/Member.cshtml", vm);
    }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult Delete([FromBody] DeleteRequest model)
    {   
        if (model == null) return Json(new { success = false, message = "ข้อมูลไม่ถูกต้อง" });

        try 
        {
            var loan = _context.Loans
                .Include(x => x.LoanDetails)
                .FirstOrDefault(x => x.Id == model.Id);

            if (loan != null)
            {
                // Soft delete เฉพาะสัญญา และเก็บงวดชำระไว้เป็นประวัติการเงินสำหรับตรวจสอบย้อนหลัง
                loan.IsDeleted = true;
                loan.DeletedDate = DateTime.Now;
                loan.DeletedBy = GetCurrentUserId();
                loan.Status = "Cancelled";
                AddAuditLog("SoftDelete", "Loan", loan.Id, $"ลบสัญญาเงินกู้แบบ soft delete LoanId={loan.Id}");
                _context.SaveChanges();
                return Json(new { success = true });
            }
            
            return Json(new { success = false, message = "ไม่พบข้อมูลสัญญา" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ตรวจสอบว่ามี Class นี้อยู่ด้านล่างสุดของไฟล์ (นอกปีกกาของ Controller) หรือยัง
    public class DeleteRequest
    {
        public int Id { get; set; }
    }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult PayInstallment([FromBody] PayRequest model) // เปลี่ยนมาใช้ Class model
    {
        if (model == null || model.DetailId <= 0) 
            return Json(new { success = false, message = "ข้อมูลไม่ถูกต้อง" });

        var detail = _context.LoanDetails
            .Include(x => x.Loan)
            .FirstOrDefault(x => x.Id == model.DetailId);
        if (detail == null) 
            return Json(new { success = false, message = "ไม่พบข้อมูลงวดนี้" });
        
        if (detail.Loan == null || detail.Loan.Status != "Active")
            return Json(new { success = false, message = "สัญญาต้องได้รับอนุมัติก่อนจึงจะชำระเงินได้" });
        
        var previousUnpaid = _context.LoanDetails
        .Where(x => x.LoanId == detail.LoanId && x.Installment < detail.Installment && !x.IsPaid)
        .Any();

        if (previousUnpaid) 
        return Json(new { success = false, message = "กรุณาชำระงวดก่อนหน้าให้ครบถ้วนก่อน" });

        var payAmount = model.Amount ?? GetRemainingPayment(detail);
        if (payAmount <= 0)
            return Json(new { success = false, message = "จำนวนเงินชำระต้องมากกว่า 0" });

        var remainingPayment = GetRemainingPayment(detail);
        if (payAmount > remainingPayment + 0.01)
            return Json(new { success = false, message = $"ยอดชำระเกินยอดคงเหลืองวดนี้ ({remainingPayment:N2} บาท)" });

        detail.PaidAmount = Math.Round(detail.PaidAmount + payAmount, 2);
        detail.IsPaid = GetRemainingPayment(detail) <= 0.01;
        detail.PaidDate = DateTime.Now;
        detail.PenaltyPaid = Math.Round(detail.PenaltyPaid + model.PenaltyAmount, 2);
        
        AddAuditLog("PayInstallment", "LoanDetail", detail.Id, $"ชำระงวดที่ {detail.Installment} LoanId={detail.LoanId} " + $"จำนวน {payAmount:N2} บาท ค่าปรับ {model.PenaltyAmount:N2} บาท");
        RefreshLoanStatus(detail.Loan);
        _context.SaveChanges();
        return Json(new { success = true });
    }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult CancelPayment([FromBody] PayRequest model)
    {
        var detail = _context.LoanDetails
            .Include(x => x.Loan)
            .FirstOrDefault(x => x.Id == model.DetailId);
        if (detail == null) return Json(new { success = false, message = "ไม่พบข้อมูล" });

        // --- เช็คลำดับ: ห้ามยกเลิกงวดก่อนหน้า ถ้ามึงวดหลังจ่ายไปแล้ว ---
        var nextPaid = _context.LoanDetails
            .Where(x => x.LoanId == detail.LoanId && x.Installment > detail.Installment && x.IsPaid)
            .Any();

        if (nextPaid) 
            return Json(new { success = false, message = "ไม่สามารถยกเลิกได้ เนื่องจากมีการชำระงวดถัดไปแล้ว" });

        detail.IsPaid = false;
        detail.PaidAmount = 0;
        detail.PaidDate = null;
        if (detail.Loan != null)
        {
            detail.Loan.Status = detail.Loan.ApprovedDate.HasValue ? "Active" : detail.Loan.Status;
            detail.Loan.ClosedDate = null;
            RefreshLoanStatus(detail.Loan);
        }
        AddAuditLog("CancelPayment", "LoanDetail", detail.Id, $"ยกเลิกชำระงวดที่ {detail.Installment} LoanId={detail.LoanId}");
        _context.SaveChanges();
        return Json(new { success = true });
    }
    [HttpGet]
    public IActionResult GetLoanHistory(int memberId)
    {
        var loans = _context.Loans
            .Where(x => x.MemberId == memberId)
            .Include(x => x.LoanDetails)
            .OrderByDescending(x => x.Id)
            .ToList();

        return Json(loans.Select(x => new
        {
            x.Id,
            x.MemberId,
            x.Amount,
            x.Rate,
            x.Months,
            Status = GetDisplayStatus(x),
            StatusText = GetStatusText(GetDisplayStatus(x)),
            x.GuarantorName,
            x.GuarantorPhone,
            x.GuarantorAddress,
            PaidAmount = GetLoanPaidAmount(x),
            TotalDue = GetLoanTotalDue(x),
            LoanDetails = x.LoanDetails.OrderBy(d => d.Installment).Select(d => new
            {
                d.Id,
                d.Installment,
                d.Payment,
                d.Principal,
                d.Interest,
                d.Balance,
                PaidAmount = GetEffectivePaidAmount(d),
                RemainingPayment = GetRemainingPayment(d),
                d.IsPaid,
                d.PaidDate
            })
        }));
    }
    [HttpGet]
    public IActionResult DownloadContract(int loanId)
    {
        var loan = _context.Loans
            .Include(x => x.Member)
            .Include(x => x.LoanDetails)
            .FirstOrDefault(x => x.Id == loanId);

        if (loan == null) return NotFound();

        // ── ข้อมูลเอกสาร ──────────────────────────────────────────────────────
        var schedule      = loan.LoanDetails.OrderBy(d => d.Installment).ToList();
        var memberName    = $"{loan.Member?.FirstName} {loan.Member?.LastName}".Trim();
        var docNo         = $"LN-{loan.Id:D5}";
        var contractDate  = (loan.ApprovedDate ?? loan.CreatedDate)
                                .ToString("dd MMMM yyyy", ThaiCulture);
        var printDate     = DateTime.Now.ToString("dd MMMM yyyy", ThaiCulture);
        var memberId      = loan.Member != null ? $"M-{loan.Member.Id:D5}" : "-";
        var guarantor     = string.IsNullOrWhiteSpace(loan.GuarantorName)  ? "-" : loan.GuarantorName;
        var guarPhone     = string.IsNullOrWhiteSpace(loan.GuarantorPhone) ? "-" : loan.GuarantorPhone;
        var guarAddr      = string.IsNullOrWhiteSpace(loan.GuarantorAddress) ? "-" : loan.GuarantorAddress;
        var memberPhone   = string.IsNullOrWhiteSpace(loan.Member?.Phone)   ? "-" : loan.Member!.Phone;
        var memberAddress = string.IsNullOrWhiteSpace(loan.Member?.Address) ? "-" : loan.Member!.Address;
        var memberRole    = string.IsNullOrWhiteSpace(loan.Member?.Role)    ? "-" : loan.Member!.Role;
        var startDue      = new DateTime(loan.CreatedDate.Year, loan.CreatedDate.Month, 1).AddMonths(1);

        double totalPrincipal = schedule.Sum(d => d.Principal);
        double totalInterest  = schedule.Sum(d => d.Interest);
        double totalPayment   = schedule.Sum(d => d.Payment);
        double firstPayment   = schedule.FirstOrDefault()?.Payment ?? 0;

        // ── Design Tokens (ขาว-ดำ สไตล์เอกสารราชการ) ────────────────────────
        const float FS_TITLE  = 15f;   // ชื่อเอกสาร
        const float FS_BODY   = 10f;   // เนื้อหาทั่วไป
        const float FS_SMALL  =  9f;   // label / caption
        const float FS_TABLE  =  8.5f; // ตาราง
        const float FS_FOOTER =  7.5f; // footer

        const string BLACK  = "#000000";
        const string INK    = "#1A1A1A"; // เนื้อหาหลัก
        const string GRAY   = "#555555"; // label รอง
        const string RULE   = "#AAAAAA"; // เส้นแบ่ง
        const string ROWALT = "#F4F4F4"; // แถวสลับตาราง — เทาอ่อนมาก

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginLeft(2.5f,  Unit.Centimetre);
                page.MarginRight(2.0f, Unit.Centimetre);
                page.MarginTop(2.0f,   Unit.Centimetre);
                page.MarginBottom(2.0f, Unit.Centimetre);
                page.DefaultTextStyle(x =>
                    x.FontFamily(PdfDocumentBase.DefaultFont)
                     .FontSize(FS_BODY)
                     .FontColor(INK)
                     .LineHeight(1.55f));

                // ── Footer ───────────────────────────────────────────────────
                page.Footer()
                    .BorderTop(0.5f).BorderColor(RULE)
                    .PaddingTop(5)
                    .Row(row =>
                    {
                        row.RelativeItem()
                           .Text($"POOC — ระบบบริหารจัดการกองทุน  |  สัญญาเลขที่ {docNo}")
                           .FontSize(FS_FOOTER).FontColor(GRAY);
                        row.AutoItem()
                           .Text(t =>
                           {
                               t.Span("หน้า ").FontSize(FS_FOOTER).FontColor(GRAY);
                               t.CurrentPageNumber().FontSize(FS_FOOTER).FontColor(GRAY);
                               t.Span(" / ").FontSize(FS_FOOTER).FontColor(GRAY);
                               t.TotalPages().FontSize(FS_FOOTER).FontColor(GRAY);
                           });
                    });

                // ── Content ──────────────────────────────────────────────────
                page.Content().Column(col =>
                {
                    // ══ ❶ หัวเอกสาร ══════════════════════════════════════════
                    col.Item().AlignCenter()
                       .Text("ระบบบริหารจัดการกองทุน POOC")
                       .FontSize(FS_SMALL).FontColor(GRAY);

                    col.Item().PaddingTop(2).AlignCenter()
                       .Text("หนังสือสัญญากู้ยืมเงิน")
                       .FontSize(FS_TITLE).Bold().FontColor(BLACK);

                    col.Item().PaddingTop(3)
                       .BorderBottom(1.5f).BorderColor(BLACK);
                    col.Item().PaddingTop(1)
                       .BorderBottom(0.4f).BorderColor(BLACK);

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        row.AutoItem().PaddingRight(4)
                           .Text("เลขที่สัญญา").FontSize(FS_SMALL).FontColor(GRAY);
                        row.AutoItem()
                           .BorderBottom(0.5f).BorderColor(RULE)
                           .PaddingBottom(1).PaddingRight(20)
                           .Text(docNo).FontSize(FS_SMALL).Bold();

                        row.AutoItem().PaddingRight(4)
                           .Text("วันที่ทำสัญญา").FontSize(FS_SMALL).FontColor(GRAY);
                        row.AutoItem()
                           .BorderBottom(0.5f).BorderColor(RULE)
                           .PaddingBottom(1)
                           .Text(contractDate).FontSize(FS_SMALL).Bold();

                        row.RelativeItem();

                        row.AutoItem().PaddingRight(4)
                           .Text("พิมพ์วันที่").FontSize(FS_SMALL).FontColor(GRAY);
                        row.AutoItem()
                           .Text(printDate).FontSize(FS_SMALL).FontColor(GRAY);
                    });

                    col.Item().PaddingTop(10);

                    // ══ ❷ ข้อความนำ ══════════════════════════════════════════
                    col.Item().Text(t =>
                    {
                        t.Span("        "); // indent ย่อหน้า
                        t.Span("หนังสือสัญญาฉบับนี้ทำขึ้น ณ ที่ทำการ POOC ระหว่าง กองทุน POOC " +
                               "ซึ่งต่อไปเรียกว่า ");
                        t.Span("\"ผู้ให้กู้\"").Bold();
                        t.Span(" ฝ่ายหนึ่ง กับ ");
                        t.Span(memberName).Bold();
                        t.Span($" รหัสสมาชิก {memberId} ซึ่งต่อไปเรียกว่า ");
                        t.Span("\"ผู้กู้\"").Bold();
                        t.Span(" อีกฝ่ายหนึ่ง " +
                               "โดยคู่สัญญาทั้งสองฝ่ายตกลงทำสัญญากันมีข้อความดังต่อไปนี้");
                    });

                    col.Item().PaddingTop(10);

                    // ══ ❸ ข้อมูลคู่สัญญา ════════════════════════════════════
                    // หัวข้อ: ผู้กู้
                    col.Item()
                       .BorderBottom(0.5f).BorderColor(BLACK)
                       .PaddingBottom(2)
                       .Text("ข้อมูลผู้กู้").FontSize(FS_BODY).Bold();

                    col.Item().PaddingTop(5).Row(row =>
                    {
                        ContractFieldCell(row.RelativeItem(3), "ชื่อ-นามสกุล", memberName, FS_BODY, GRAY, RULE);
                        ContractFieldCell(row.RelativeItem(2), "รหัสสมาชิก",   memberId,   FS_BODY, GRAY, RULE);
                    });
                    col.Item().PaddingTop(6).Row(row =>
                    {
                        ContractFieldCell(row.RelativeItem(3), "ตำแหน่ง/แผนก", memberRole,  FS_BODY, GRAY, RULE);
                        ContractFieldCell(row.RelativeItem(2), "เบอร์โทรศัพท์", memberPhone, FS_BODY, GRAY, RULE);
                    });
                    col.Item().PaddingTop(6);
                    ContractFieldCell(col.Item(), "ที่อยู่", memberAddress, FS_BODY, GRAY, RULE);

                    col.Item().PaddingTop(10);

                    // หัวข้อ: ผู้ค้ำประกัน
                    col.Item()
                       .BorderBottom(0.5f).BorderColor(BLACK)
                       .PaddingBottom(2)
                       .Text("ข้อมูลผู้ค้ำประกัน").FontSize(FS_BODY).Bold();

                    col.Item().PaddingTop(5).Row(row =>
                    {
                        ContractFieldCell(row.RelativeItem(3), "ชื่อ-นามสกุล",   guarantor, FS_BODY, GRAY, RULE);
                        ContractFieldCell(row.RelativeItem(2), "เบอร์โทรศัพท์", guarPhone,  FS_BODY, GRAY, RULE);
                    });
                    col.Item().PaddingTop(6);
                    ContractFieldCell(col.Item(), "ที่อยู่", guarAddr, FS_BODY, GRAY, RULE);

                    col.Item().PaddingTop(12)
                       .BorderBottom(0.5f).BorderColor(RULE);
                    col.Item().PaddingTop(10);

                    // ══ ❹ ข้อกำหนดสัญญา ═════════════════════════════════════
                    col.Item()
                       .BorderBottom(0.5f).BorderColor(BLACK)
                       .PaddingBottom(2)
                       .Text("ข้อกำหนดและเงื่อนไข").FontSize(FS_BODY).Bold();

                    col.Item().PaddingTop(6);

                    ContractClause(col, "ข้อ ๑.",
                        $"ผู้กู้ได้กู้ยืมเงินจากผู้ให้กู้ เป็นจำนวนเงิน {loan.Amount:N2} บาท " +
                        "โดยผู้ให้กู้ได้ส่งมอบเงินให้แก่ผู้กู้ครบถ้วนแล้วในวันทำสัญญานี้",
                        FS_BODY);

                    ContractClause(col, "ข้อ ๒.",
                        $"ผู้กู้ตกลงชำระคืนเงินต้นพร้อมดอกเบี้ยในอัตราร้อยละ {loan.Rate:N2} ต่อปี " +
                        $"แบ่งชำระรายเดือน จำนวน {loan.Months} งวด งวดละ {firstPayment:N2} บาท " +
                        $"เริ่มงวดแรกวันที่ {startDue.ToString("d MMMM yyyy", ThaiCulture)} " +
                        "และชำระทุกวันที่ 1 ของเดือนถัดไปจนครบตามตารางแนบท้ายสัญญา",
                        FS_BODY);

                    ContractClause(col, "ข้อ ๓.",
                        "หากผู้กู้ผิดนัดชำระเกินกว่า 30 วัน ผู้กู้ยินยอมให้คิดค่าปรับในอัตราร้อยละ 1.50 " +
                        "ต่อเดือนของยอดค้างชำระ นับแต่วันครบกำหนดจนถึงวันชำระจริง",
                        FS_BODY);

                    ContractClause(col, "ข้อ ๔.",
                        "ผู้ค้ำประกันยินยอมรับผิดร่วมกับผู้กู้อย่างลูกหนี้ร่วม " +
                        "ผู้ให้กู้มีสิทธิ์เรียกให้ผู้ค้ำประกันชำระหนี้ก่อนหรือพร้อมกับผู้กู้ก็ได้",
                        FS_BODY);

                    ContractClause(col, "ข้อ ๕.",
                        "ผู้ให้กู้มีสิทธิ์หักเงินเดือน สวัสดิการ หรือสิทธิประโยชน์ใดๆ ของผู้กู้ " +
                        "เพื่อชำระหนี้ตามสัญญาฉบับนี้ โดยผู้กู้ให้ความยินยอมไว้ล่วงหน้าด้วยการลงลายมือชื่อในสัญญาฉบับนี้",
                        FS_BODY);

                    ContractClause(col, "ข้อ ๖.",
                        "สัญญาฉบับนี้ทำขึ้นสองฉบับ มีข้อความตรงกัน คู่สัญญาแต่ละฝ่ายยึดถือฝ่ายละหนึ่งฉบับ " +
                        "หากเกิดข้อพิพาทให้อยู่ภายใต้เขตอำนาจของศาลที่มีอำนาจและบังคับด้วยกฎหมายไทย",
                        FS_BODY);

                    col.Item().PaddingTop(6);
                    col.Item().Text(t =>
                    {
                        t.Span("        ");
                        t.Span("คู่สัญญาทั้งสองฝ่ายได้อ่านและเข้าใจข้อความในสัญญาโดยตลอดแล้ว " +
                               "จึงลงลายมือชื่อไว้เป็นสำคัญต่อหน้าพยาน");
                    });

                    col.Item().PaddingTop(14);

                    // ══ ❺ ตารางการผ่อนชำระ ══════════════════════════════════
                    col.Item()
                       .BorderBottom(0.5f).BorderColor(BLACK)
                       .PaddingBottom(2)
                       .Row(row =>
                       {
                           row.RelativeItem()
                              .Text("ตารางการผ่อนชำระ").FontSize(FS_BODY).Bold();
                           row.AutoItem()
                              .Text($"วงเงิน {loan.Amount:N2} บาท  |  " +
                                    $"ดอกเบี้ย {loan.Rate:N2}% ต่อปี  |  " +
                                    $"{loan.Months} งวด")
                              .FontSize(FS_SMALL).FontColor(GRAY);
                       });

                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(28);    // งวดที่
                            c.RelativeColumn(1.6f);  // วันครบกำหนด
                            c.RelativeColumn(1.2f);  // เงินต้น
                            c.RelativeColumn(1.2f);  // ดอกเบี้ย
                            c.RelativeColumn(1.2f);  // ยอดผ่อน
                            c.RelativeColumn(1.4f);  // ยอดคงเหลือ
                        });

                        // แถว header
                        table.Header(h =>
                        {
                            IContainer Hdr(IContainer c) =>
                                c.BorderTop(1f).BorderColor(BLACK)
                                 .BorderBottom(0.5f).BorderColor(BLACK)
                                 .PaddingVertical(4).PaddingHorizontal(3);

                            h.Cell().Element(Hdr).AlignCenter()
                             .Text("งวดที่").FontSize(FS_TABLE).Bold();
                            h.Cell().Element(Hdr)
                             .Text("วันครบกำหนด").FontSize(FS_TABLE).Bold();
                            h.Cell().Element(Hdr).AlignRight()
                             .Text("เงินต้น (บาท)").FontSize(FS_TABLE).Bold();
                            h.Cell().Element(Hdr).AlignRight()
                             .Text("ดอกเบี้ย (บาท)").FontSize(FS_TABLE).Bold();
                            h.Cell().Element(Hdr).AlignRight()
                             .Text("ยอดผ่อน (บาท)").FontSize(FS_TABLE).Bold();
                            h.Cell().Element(Hdr).AlignRight()
                             .Text("ยอดคงเหลือ (บาท)").FontSize(FS_TABLE).Bold();
                        });

                        // แถวข้อมูล — สลับสีขาว / เทาอ่อน ไม่มีเส้นแนวตั้ง
                        bool alt = false;
                        foreach (var d in schedule)
                        {
                            var dueDate = startDue.AddMonths(d.Installment - 1)
                                                  .ToString("d MMM yyyy", ThaiCulture);
                            string bg = alt ? ROWALT : Colors.White;
                            alt = !alt;

                            IContainer Cell(IContainer c) =>
                                c.Background(bg)
                                 .PaddingVertical(3).PaddingHorizontal(3);

                            table.Cell().Element(Cell).AlignCenter()
                                 .Text(d.Installment.ToString()).FontSize(FS_TABLE);
                            table.Cell().Element(Cell)
                                 .Text(dueDate).FontSize(FS_TABLE);
                            table.Cell().Element(Cell).AlignRight()
                                 .Text(d.Principal.ToString("N2")).FontSize(FS_TABLE);
                            table.Cell().Element(Cell).AlignRight()
                                 .Text(d.Interest.ToString("N2")).FontSize(FS_TABLE);
                            table.Cell().Element(Cell).AlignRight()
                                 .Text(d.Payment.ToString("N2")).FontSize(FS_TABLE).Bold();
                            table.Cell().Element(Cell).AlignRight()
                                 .Text(d.Balance.ToString("N2")).FontSize(FS_TABLE);
                        }

                        // แถวรวม
                        IContainer TotalCell(IContainer c) =>
                            c.BorderTop(0.5f).BorderColor(BLACK)
                             .BorderBottom(1f).BorderColor(BLACK)
                             .PaddingVertical(4).PaddingHorizontal(3);

                        table.Cell().Element(TotalCell).AlignCenter()
                             .Text("รวม").FontSize(FS_TABLE).Bold();
                        table.Cell().Element(TotalCell);
                        table.Cell().Element(TotalCell).AlignRight()
                             .Text(totalPrincipal.ToString("N2")).FontSize(FS_TABLE).Bold();
                        table.Cell().Element(TotalCell).AlignRight()
                             .Text(totalInterest.ToString("N2")).FontSize(FS_TABLE).Bold();
                        table.Cell().Element(TotalCell).AlignRight()
                             .Text(totalPayment.ToString("N2")).FontSize(FS_TABLE).Bold();
                        table.Cell().Element(TotalCell).AlignRight()
                             .Text("-").FontSize(FS_TABLE).FontColor(GRAY);
                    });

                    col.Item().PaddingTop(16);

                    // ══ ❻ ลายเซ็น ═══════════════════════════════════════════
                    col.Item().Row(row =>
                    {
                        ContractSigCol(row.RelativeItem(), memberName,  "ผู้กู้",           null,               FS_SMALL, GRAY);
                        ContractSigCol(row.RelativeItem(), guarantor,   "ผู้ค้ำประกัน",      null,               FS_SMALL, GRAY);
                        ContractSigCol(row.RelativeItem(), null,        "ผู้ให้กู้",         "ผู้มีอำนาจลงนาม", FS_SMALL, GRAY);
                    });

                    col.Item().PaddingTop(12)
                       .BorderTop(0.5f).BorderColor(RULE);

                    col.Item().PaddingTop(6)
                       .Text("พยาน").FontSize(FS_SMALL).FontColor(GRAY);

                    col.Item().PaddingTop(4).Row(row =>
                    {
                        ContractSigCol(row.RelativeItem(), null, "พยานที่ 1", null, FS_SMALL, GRAY);
                        row.ConstantItem(24);
                        ContractSigCol(row.RelativeItem(), null, "พยานที่ 2", null, FS_SMALL, GRAY);
                        row.RelativeItem();
                    });

                    col.Item().PaddingTop(12)
                       .BorderTop(0.5f).BorderColor(RULE);

                    col.Item().PaddingTop(5).AlignCenter()
                       .Text("สัญญาฉบับนี้ทำขึ้นเป็นสองฉบับ มีข้อความถูกต้องตรงกัน " +
                             "คู่สัญญาแต่ละฝ่ายได้รับไปยึดถือไว้ฝ่ายละหนึ่งฉบับ")
                       .FontSize(FS_SMALL).FontColor(GRAY);
                });
            });
        });

        return File(document.GeneratePdf(), "application/pdf", $"Contract_{loanId}.pdf");
    }

    // ── helpers สำหรับ DownloadContract ──────────────────────────────────────

    /// <summary>ช่องข้อมูล: label เล็ก + value ตัวหนา + เส้นใต้</summary>
    private static void ContractFieldCell(
        IContainer container, string label, string value,
        float fs, string labelColor, string ruleColor)
    {
        container.PaddingRight(16).Column(c =>
        {
            c.Item()
             .Text(label).FontSize(fs - 1f).FontColor(labelColor);
            c.Item()
             .BorderBottom(0.5f).BorderColor(ruleColor)
             .PaddingBottom(2)
             .Text(string.IsNullOrWhiteSpace(value) ? "-" : value)
             .FontSize(fs).Bold();
        });
    }

    /// <summary>
    /// ข้อกำหนด: "ข้อ X." inline กับเนื้อหาในย่อหน้าเดียวกัน
    /// ป้องกัน gap จาก ConstantItem และป้องกัน Justify ยืดบรรทัดสุดท้าย
    /// </summary>
    private static void ContractClause(
        ColumnDescriptor col, string num, string text, float fs)
    {
        col.Item().PaddingTop(1).PaddingBottom(4).Text(t =>
        {
            t.Span(num + " ").FontSize(fs).Bold();
            t.Span(text).FontSize(fs);
        });
    }

    /// <summary>คอลัมน์ลายเซ็น: เส้น + ชื่อ + บทบาท + วันที่</summary>
    private static void ContractSigCol(
        IContainer container, string? name,
        string role1, string? role2, float fs, string labelColor)
    {
        container.PaddingHorizontal(6).Column(c =>
        {
            c.Item().PaddingTop(28).PaddingHorizontal(4)
             .BorderBottom(0.5f).BorderColor("#888888");
            c.Item().PaddingTop(4).AlignCenter()
             .Text(string.IsNullOrWhiteSpace(name)
                 ? "(................................................)"
                 : $"({name})")
             .FontSize(fs);
            c.Item().AlignCenter()
             .Text(role1).FontSize(fs - 0.5f).FontColor(labelColor);
            if (!string.IsNullOrWhiteSpace(role2))
                c.Item().AlignCenter()
                 .Text(role2).FontSize(fs - 0.5f).FontColor(labelColor);
            c.Item().PaddingTop(5).AlignCenter()
             .Text("วันที่ ......../......../.........")
             .FontSize(fs - 1f).FontColor(labelColor);
        });
    }

    [HttpGet]
    public IActionResult GetOverdueSummary()
    {
        var today = DateTime.Now.Date;
        
        var loans = _context.Loans
            .Include(x => x.Member)
            .Include(x => x.LoanDetails)
            .Where(x => x.Status != "Pending" && x.Status != "Cancelled" && x.Status != "Closed")
            .ToList();

        int overdueCount = 0;
        var penaltyRate = (double)GetDecimalSetting("PenaltyRate", 1.5m) / 100;
        double totalPenalty = 0;

        foreach (var loan in loans)
        {
            var startDate = new DateTime(loan.CreatedDate.Year, loan.CreatedDate.Month, 1).AddMonths(1);
            
            foreach (var detail in loan.LoanDetails.Where(d => !d.IsPaid))
            {
                var dueDate = startDate.AddMonths(detail.Installment - 1);
                if (dueDate < today)
                {
                    overdueCount++;
                    var monthsLate = ((today.Year - dueDate.Year) * 12) + today.Month - dueDate.Month;
                    if (monthsLate < 1) monthsLate = 1;
                    totalPenalty += detail.Payment * penaltyRate * monthsLate;
                }
            }
        }

        return Json(new { overdueCount, totalPenalty });
    }
    [HttpGet]
    public IActionResult GetLoanHistoryWithOverdue(int memberId)
    {
        var today = DateTime.Now.Date;
        
        var loans = _context.Loans
            .Where(x => x.MemberId == memberId)
            .Include(x => x.LoanDetails)
            .OrderByDescending(x => x.Id)
            .ToList();

        return Json(loans.Select(loan => {
            var startDate = new DateTime(loan.CreatedDate.Year, loan.CreatedDate.Month, 1).AddMonths(1);
            
            return new {
                loan.Id,
                loan.MemberId,
                loan.Amount,
                loan.Rate,
                loan.Months,
                loan.CreatedDate,
                Status = GetDisplayStatus(loan),
                StatusText = GetStatusText(GetDisplayStatus(loan)),
                loan.GuarantorName,
                loan.GuarantorPhone,
                loan.GuarantorAddress,
                loan.ApprovedDate,
                loan.ClosedDate,
                PaidAmount = GetLoanPaidAmount(loan),
                TotalDue = GetLoanTotalDue(loan),
                LoanDetails = loan.LoanDetails.OrderBy(d => d.Installment).Select(d => {
                    var dueDate = startDate.AddMonths(d.Installment - 1);
                    bool isOverdue = loan.Status != "Pending" && loan.Status != "Closed" && !d.IsPaid && dueDate < today;
                    int monthsLate = 0;
                    double penalty = 0;
                    
                    if (isOverdue)
                    {
                        monthsLate = ((today.Year - dueDate.Year) * 12) + today.Month - dueDate.Month;
                        if (monthsLate < 1) monthsLate = 1;
                        var penaltyRate = (double)GetDecimalSetting("PenaltyRate", 1.5m) / 100;
                        penalty = Math.Round(d.Payment * penaltyRate * monthsLate, 2);
                    }
                    
                    return new {
                        d.Id,
                        d.Installment,
                        d.Payment,
                        d.Principal,
                        d.Interest,
                        d.Balance,
                        PaidAmount = GetEffectivePaidAmount(d),
                        RemainingPayment = GetRemainingPayment(d),
                        d.IsPaid,
                        d.PaidDate,
                        DueDate = dueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                        IsOverdue = isOverdue,
                        MonthsLate = monthsLate,
                        Penalty = penalty
                    };
                })
            };
        }));
    }
    private decimal GetDecimalSetting(string key, decimal defaultValue)
    {
        var value = _context.SystemSettings.FirstOrDefault(x => x.Key == key)?.Value;
        return decimal.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private void AddAuditLog(string action, string entityName, int? entityId, string detail)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = GetCurrentUserId(),
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Detail = detail
        });
    }
    [HttpGet]
    public IActionResult SearchLoans(string? keyword, string? status)
    {
        var query = _context.Loans
            .Include(x => x.Member)
            .Include(x => x.LoanDetails)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                x.Member!.FirstName.Contains(keyword) ||
                x.Member!.LastName.Contains(keyword) ||
                x.GuarantorName!.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "All")
            query = query.Where(x => x.Status == status);

        var loans = query.OrderByDescending(x => x.Id).Take(100).ToList();

        return Json(loans.Select(x => new {
            x.Id,
            MemberName = $"{x.Member?.FirstName} {x.Member?.LastName}",
            x.Amount,
            x.Rate,
            x.Months,
            x.CreatedDate,
            Status = GetDisplayStatus(x),
            StatusText = GetStatusText(GetDisplayStatus(x)),
            PaidAmount = GetLoanPaidAmount(x),
            Remaining = GetLoanTotalDue(x) - GetLoanPaidAmount(x)
        }));
    }
}