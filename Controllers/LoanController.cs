using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
using Microsoft.EntityFrameworkCore;

public class LoanController : Controller
{
    private readonly ApplicationDbContext _context;

    private List<LoanSchedule> CalculateLoan(double amount, double rate, int months)
    {
        var list = new List<LoanSchedule>();
        double monthlyRate = rate / 100 / 12;
        double payment = amount * monthlyRate / (1 - Math.Pow(1 + monthlyRate, -months));
        double balance = amount;
        for (int i = 1; i <= months; i++)
        {
            double interest = balance * monthlyRate;
            double principal = payment - interest;
            balance -= principal;
            list.Add(new LoanSchedule
            {
                Installment = i,
                Payment = payment,
                Principal = principal,
                Interest = interest,
                Balance = balance < 0 ? 0 : balance
            });
        }
        return list;
    }

    public LoanController(ApplicationDbContext context)
    {
        _context = context;
    }
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
        var loan = new Loan
        {
            MemberId = model.MemberId,
            Amount = model.Amount,
            Rate = model.Rate,
            Months = model.Months
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
            LoanDetails = x.LoanDetails.Select(d => new
            {
                d.Installment,
                d.Payment,
                d.Principal,
                d.Interest,
                d.Balance
            })
        }));
    }
}