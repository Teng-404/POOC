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
        // ตรวจสอบว่ามีข้อมูลไหม ถ้าไม่มีให้เป็น 0 แทน Null
        ViewBag.TotalLoan = _context.Loans.Sum(x => (double?)x.Amount) ?? 0;
        
        // ดึงรายละเอียดงวดมาคำนวณ
        var details = _context.LoanDetails.ToList();
        
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
}