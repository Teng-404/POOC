using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

[Authorize]
public class LoanController : Controller
{
    private readonly ApplicationDbContext _context;

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

    [HttpGet]
    public IActionResult GetLoanSchedule(int memberId, double amount, double rate, int months)
    {
        var schedule = CalculateLoan(amount, rate, months);

        return Json(schedule);
    }
    [HttpPost]
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
            OwnerId = userId
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
        AddAuditLog("Create", "Loan", loan.Id, $"สร้างสัญญาเงินกู้ MemberId={model.MemberId} ยอด {model.Amount:N2} บาท");
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
        
        var previousUnpaid = _context.LoanDetails
        .Where(x => x.LoanId == detail.LoanId && x.Installment < detail.Installment && !x.IsPaid)
        .Any();

        if (previousUnpaid) 
        return Json(new { success = false, message = "กรุณาชำระงวดก่อนหน้าให้ครบถ้วนก่อน" });

        detail.IsPaid = true;
        detail.PaidDate = DateTime.Now;
        
        AddAuditLog("PayInstallment", "LoanDetail", detail.Id, $"ชำระงวดที่ {detail.Installment} LoanId={detail.LoanId}");
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
        detail.PaidDate = null;
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
            LoanDetails = x.LoanDetails.OrderBy(d => d.Installment).Select(d => new
            {
                d.Id,
                d.Installment,
                d.Payment,
                d.Principal,
                d.Interest,
                d.Balance,
                d.IsPaid,
                d.PaidDate
            })
        }));
    }
    public IActionResult DownloadContract(int loanId)
    {
        var loan = _context.Loans
            .Include(x => x.Member)
            .Include(x => x.LoanDetails)
            .FirstOrDefault(x => x.Id == loanId);

        if (loan == null) return NotFound();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                // ตั้งค่าพื้นฐาน: ใช้ฟอนต์ที่ดูทางการ (ถ้ามีในระบบ) และขอบกระดาษที่เหมาะสม
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("TH Sarabun New").FontSize(11).LineHeight(1.5f));

                // 1. Header: หัวเอกสาร
                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("หนังสือสัญญากู้ยืมเงิน").FontSize(18).SemiBold();
                    col.Item().AlignRight().Text($"ทำที่: ระบบบริหารจัดการเงินกู้ POOC").FontSize(10);
                    col.Item().AlignRight().Text($"วันที่ทำสัญญา: {loan.CreatedDate.ToString("dd MMMM yyyy", new System.Globalization.CultureInfo("th-TH"))}").FontSize(10);
                });

                // 2. Content: เนื้อความสัญญา
                page.Content().PaddingVertical(10).Column(col =>
                {
                    // ข้อมูลคู่สัญญา
                    col.Item().Text(t =>
                    {
                        t.Span("สัญญาฉบับนี้ทำขึ้นระหว่าง ");
                        t.Span($"{loan.Member?.FirstName} {loan.Member?.LastName}").SemiBold();
                        t.Span(" ซึ่งต่อไปนี้ในสัญญาเรียกว่า \"ผู้กู้\" ฝ่ายหนึ่ง กับ ");
                        t.Span("ระบบกองทุน POOC").SemiBold();
                        t.Span(" ซึ่งต่อไปนี้เรียกว่า \"ผู้ให้กู้\" อีกฝ่ายหนึ่ง ทั้งสองฝ่ายตกลงทำสัญญามีข้อความดังต่อไปนี้");
                    });

                    // รายละเอียดเงินกู้
                    col.Item().PaddingTop(5).PaddingLeft(20).Column(c =>
                    {
                        c.Item().Text($"ข้อ 1. ผู้กู้ได้กู้ยืมเงินจากผู้ให้กู้เป็นจำนวนเงิน {loan.Amount:N2} บาท");
                        c.Item().Text($"ข้อ 2. ผู้กู้ตกลงยินยอมเสียดอกเบี้ยให้แก่ผู้ให้กู้ในอัตราร้อยละ {loan.Rate}% ต่อปี");
                        c.Item().Text($"ข้อ 3. ผู้กู้ตกลงจะชำระคืนเงินต้นพร้อมดอกเบี้ยรวมทั้งสิ้น {loan.Months} งวด ตามตารางแนบท้ายสัญญานี้");
                    });

                    // 3. ตารางงวดชำระ
                    col.Item().PaddingTop(15).Text("ตารางรายละเอียดการชำระเงินแนบท้ายสัญญา").SemiBold();
                    col.Item().PaddingTop(5).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(40);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            var headerStyle = TextStyle.Default.SemiBold();
                            header.Cell().Element(HeaderStyle).Text("งวดที่").Style(headerStyle);
                            header.Cell().Element(HeaderStyle).Text("เงินต้น").Style(headerStyle);
                            header.Cell().Element(HeaderStyle).Text("ดอกเบี้ย").Style(headerStyle);
                            header.Cell().Element(HeaderStyle).Text("ยอดจ่าย").Style(headerStyle);
                            header.Cell().Element(HeaderStyle).Text("คงเหลือ").Style(headerStyle);

                            static IContainer HeaderStyle(IContainer container) => 
                                container.PaddingVertical(5).BorderBottom(1).AlignCenter();
                        });

                        foreach (var item in loan.LoanDetails.OrderBy(x => x.Installment))
                        {
                            table.Cell().Element(ContentStyle).Text(item.Installment.ToString());
                            table.Cell().Element(ContentStyle).Text(item.Principal.ToString("N2"));
                            table.Cell().Element(ContentStyle).Text(item.Interest.ToString("N2"));
                            table.Cell().Element(ContentStyle).Text(item.Payment.ToString("N2"));
                            table.Cell().Element(ContentStyle).Text(item.Balance.ToString("N2"));

                            static IContainer ContentStyle(IContainer container) => 
                                container.PaddingVertical(2).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).AlignCenter();
                        }
                    });

                    // 4. ส่วนลงชื่อ 
                    col.Item().PaddingTop(40).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignCenter().Text("ลงชื่อ......................................................");
                            c.Item().PaddingTop(2).AlignCenter().Text($"( {loan.Member?.FirstName} {loan.Member?.LastName} )");
                            c.Item().AlignCenter().Text("ผู้กู้");
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignCenter().Text("ลงชื่อ......................................................");
                            c.Item().PaddingTop(2).AlignCenter().Text("( ...................................................... )");
                            c.Item().AlignCenter().Text("ผู้ให้กู้ / พยาน");
                        });
                    });
                });
            });
        });

        return File(document.GeneratePdf(), "application/pdf", $"Contract_{loanId}.pdf");
    }
    [HttpGet]
    public IActionResult GetOverdueSummary()
    {
        var today = DateTime.Now.Date;
        
        var loans = _context.Loans
            .Include(x => x.Member)
            .Include(x => x.LoanDetails)
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
                LoanDetails = loan.LoanDetails.OrderBy(d => d.Installment).Select(d => {
                    var dueDate = startDate.AddMonths(d.Installment - 1);
                    bool isOverdue = !d.IsPaid && dueDate < today;
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
                        d.IsPaid,
                        d.PaidDate,
                        DueDate = dueDate.ToString("yyyy-MM-dd"),
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
}