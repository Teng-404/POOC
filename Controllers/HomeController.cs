using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

[Authorize]
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