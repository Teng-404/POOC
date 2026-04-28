using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }
    public IActionResult Member()
    {
        var vm = new MemberViewModel
        {
            Members = _context.Members.ToList()
        };

            return View("Member", vm); 
    }
}