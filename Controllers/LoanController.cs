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
        Console.WriteLine("DELETE ID = " + model.Id); // debug

        var loan = _context.Loans
            .Include(x => x.LoanDetails)
            .FirstOrDefault(x => x.Id == model.Id);

        if (loan != null)
        {
            _context.LoanDetails.RemoveRange(loan.LoanDetails);
            _context.Loans.Remove(loan);
            _context.SaveChanges();
        }

        return Json(new { success = true });
    }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult PayInstallment([FromBody] PayRequest model) // เปลี่ยนมาใช้ Class model
    {
        if (model == null || model.DetailId <= 0) 
            return Json(new { success = false, message = "ข้อมูลไม่ถูกต้อง" });

        var detail = _context.LoanDetails.Find(model.DetailId);
        if (detail == null) 
            return Json(new { success = false, message = "ไม่พบข้อมูลงวดนี้" });
        
        var previousUnpaid = _context.LoanDetails
        .Where(x => x.LoanId == detail.LoanId && x.Installment < detail.Installment && !x.IsPaid)
        .Any();

        if (previousUnpaid) 
        return Json(new { success = false, message = "กรุณาชำระงวดก่อนหน้าให้ครบถ้วนก่อน" });

        detail.IsPaid = true;
        detail.PaidDate = DateTime.Now;

        _context.SaveChanges();
        return Json(new { success = true });
    }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult CancelPayment([FromBody] PayRequest model)
    {
        var detail = _context.LoanDetails.Find(model.DetailId);
        if (detail == null) return Json(new { success = false, message = "ไม่พบข้อมูล" });

        // --- เช็คลำดับ: ห้ามยกเลิกงวดก่อนหน้า ถ้ามึงวดหลังจ่ายไปแล้ว ---
        var nextPaid = _context.LoanDetails
            .Where(x => x.LoanId == detail.LoanId && x.Installment > detail.Installment && x.IsPaid)
            .Any();

        if (nextPaid) 
            return Json(new { success = false, message = "ไม่สามารถยกเลิกได้ เนื่องจากมีการชำระงวดถัดไปแล้ว" });

        detail.IsPaid = false;
        detail.PaidDate = null;
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
        // 1. ดึงข้อมูลสัญญาและรายละเอียดงวดจาก DB
        var loan = _context.Loans
            .Include(x => x.Member)
            .Include(x => x.LoanDetails)
            .FirstOrDefault(x => x.Id == loanId);

        if (loan == null) return NotFound();

        // 2. สร้าง PDF ด้วย QuestPDF
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(12));

                // ส่วนหัว (Header)
                page.Header().Text("สัญญาเงินกู้ระบบ POOC").FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);

                // ส่วนเนื้อหา (Content)
                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Text($"ข้าพเจ้า {loan.Member?.FirstName ?? ""} {loan.Member?.LastName ?? ""} ได้ทำสัญญากู้ยืมเงิน...");
                    col.Item().Text($"จำนวนเงินต้น: {loan.Amount:N2} บาท  | ดอกเบี้ย: {loan.Rate}%");
                    
                    col.Item().PaddingTop(10).Text("ตารางการผ่อนชำระ").SemiBold();
                    
                    // สร้างตารางงวดชำระ (คล้ายกับตารางในหน้า Member.cshtml ของคุณ)
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(50); // งวดที่
                            columns.RelativeColumn();   // เงินต้น
                            columns.RelativeColumn();   // ดอกเบี้ย
                            columns.RelativeColumn();   // ยอดชำระ
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("งวด");
                            header.Cell().Text("เงินต้น");
                            header.Cell().Text("ดอกเบี้ย");
                            header.Cell().Text("รวมจ่าย");
                        });

                        foreach (var detail in loan.LoanDetails)
                        {
                            table.Cell().Text(detail.Installment.ToString());
                            table.Cell().Text(detail.Principal.ToString("N2"));
                            table.Cell().Text(detail.Interest.ToString("N2"));
                            table.Cell().Text(detail.Payment.ToString("N2"));
                        }
                    });
                });

                // ส่วนท้าย (Footer)
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("หน้า ");
                    x.CurrentPageNumber();
                });
            });
        });

        // 3. ส่งไฟล์ออกไปให้ User ดาวน์โหลด
        byte[] pdfBytes = document.GeneratePdf();
        return File(pdfBytes, "application/pdf", $"Contract_{loanId}.pdf");
    }
}