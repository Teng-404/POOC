using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;

public class MemberController : Controller
{
    private readonly ApplicationDbContext _context;

    public MemberController(ApplicationDbContext context)
    {
        _context = context;
    }

    // LIST PAGE
    public IActionResult Index()
    {
        var vm = new MemberViewModel
        {
            Members = _context.Members.ToList()
        };

        return View("~/Views/Home/Member.cshtml", vm);
    }

    // ADD
    [HttpPost]
    public IActionResult Add(Member model)
    {
        var exists = _context.Members
        .Any(x => x.FirstName == model.FirstName && x.LastName == model.LastName);

        if (exists)
        {
            TempData["error"] = "มีสมาชิกนี้อยู่แล้ว";
            return RedirectToAction("Index");
        }
        _context.Members.Add(model);
        _context.SaveChanges();

        return RedirectToAction("Index");
    }
    // UPDATE
    [HttpPost]
    public IActionResult Update(Member model)
    {
        var data = _context.Members.FirstOrDefault(x => x.Id == model.Id);

        if (data != null)
        {
            data.FirstName = model.FirstName;
            data.LastName = model.LastName;
            data.Role = model.Role;

            _context.SaveChanges();
        }

        return RedirectToAction("Index");
    }

    // DELETE
    [HttpPost]
    public IActionResult Delete(int id)
    {
        var data = _context.Members.Find(id);

        if (data != null)
        {
            _context.Members.Remove(data);
            _context.SaveChanges();
        }

        return RedirectToAction("Index");
    }
}