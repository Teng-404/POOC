using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

[Authorize]
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    public HomeController(ApplicationDbContext context) => _context = context;

    public IActionResult Dashboard()
    {
        var loans = _context.Loans
            .Include(x => x.LoanDetails)
            .ToList();

        ViewBag.TotalLoan = loans.Sum(x => x.Amount);
        
        // ดึงรายละเอียดงวดจากสัญญาที่ผ่าน query filter ของผู้ใช้ปัจจุบันเท่านั้น
        var details = loans.SelectMany(x => x.LoanDetails).ToList();
        
        ViewBag.Collected = details.Where(x => x.IsPaid).Sum(x => x.Payment);
        ViewBag.Pending = details.Where(x => !x.IsPaid).Sum(x => x.Payment);
        ViewBag.TotalInterest = details.Sum(x => x.Interest);

        return View();
    }

    public IActionResult Member()
    {
        var vm = new MemberViewModel { Members = _context.Members.ToList() };
        return View("Member", vm); 
    }

    // ── [ใหม่] API: trend เงินกู้ + เงินฝากรายเดือน ย้อนหลัง N เดือน ──
    [HttpGet]
    public IActionResult GetLoanTrend(int months = 12)
    {
        if (months < 3) months = 3;
        if (months > 24) months = 24;

        var today = DateTime.Now;
        // สร้าง list ของ (year, month) ย้อนหลัง
        var periods = Enumerable.Range(0, months)
            .Select(i => today.AddMonths(-months + 1 + i))
            .Select(d => new { d.Year, d.Month })
            .ToList();

        // ── เงินกู้ที่ปล่อยแต่ละเดือน (Active/Closed เท่านั้น ไม่นับ Cancelled/Rejected) ──
        var loansByMonth = _context.Loans
            .AsNoTracking()
            .Where(l => !l.IsDeleted
                && l.Status != "Cancelled"
                && l.Status != "Rejected"
                && l.CreatedDate >= new DateTime(today.Year, today.Month, 1).AddMonths(-months + 1))
            .GroupBy(l => new { l.CreatedDate.Year, l.CreatedDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(l => l.Amount) })
            .ToList();

        // ── เงินที่เก็บคืนได้แต่ละเดือน (จาก PaidDate) ──
        var collectedByMonth = _context.LoanDetails
            .AsNoTracking()
            .Where(d => d.IsPaid
                && d.PaidDate != null
                && d.PaidDate >= new DateTime(today.Year, today.Month, 1).AddMonths(-months + 1))
            .GroupBy(d => new { d.PaidDate!.Value.Year, d.PaidDate!.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Principal = g.Sum(d => d.Principal), Interest = g.Sum(d => d.Interest) })
            .ToList();

        // ── เงินฝากสุทธิ (ยอดสะสมแต่ละเดือน จาก Savings.TransactionDate) ──
        var savingsByMonth = _context.Savings
            .AsNoTracking()
            .Where(s => s.TransactionDate >= new DateTime(today.Year, today.Month, 1).AddMonths(-months + 1))
            .GroupBy(s => new { s.TransactionDate.Year, s.TransactionDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Net = g.Sum(s => s.Amount) })
            .ToList();

        // ── สร้าง result ครบทุกเดือน (เติม 0 ถ้าไม่มีข้อมูล) ──
        var labels = periods.Select(p =>
            new DateTime(p.Year, p.Month, 1).ToString("MMM yy", new System.Globalization.CultureInfo("th-TH"))
        ).ToList();

        var loanDisbursed = periods.Select(p =>
            loansByMonth.FirstOrDefault(x => x.Year == p.Year && x.Month == p.Month)?.Total ?? 0
        ).ToList();

        var principalCollected = periods.Select(p =>
            collectedByMonth.FirstOrDefault(x => x.Year == p.Year && x.Month == p.Month)?.Principal ?? 0
        ).ToList();

        var interestCollected = periods.Select(p =>
            collectedByMonth.FirstOrDefault(x => x.Year == p.Year && x.Month == p.Month)?.Interest ?? 0
        ).ToList();

        var savingsNet = periods.Select(p =>
            (double)(savingsByMonth.FirstOrDefault(x => x.Year == p.Year && x.Month == p.Month)?.Net ?? 0)
        ).ToList();

        return Json(new
        {
            labels,
            loanDisbursed,
            principalCollected,
            interestCollected,
            savingsNet
        });
    }
}
