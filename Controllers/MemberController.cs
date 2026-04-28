using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POOC.Data;
using POOC.Models;
using System.Security.Claims; // เพิ่มตัวนี้เพื่อใช้ Claims

public class MemberController : Controller
{
    private readonly ApplicationDbContext _context;

    public MemberController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

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
        try 
        {
            // 1. เช็คว่าชื่อ+นามสกุล ซ้ำไหม (แบบไม่สน Filter)
            var isDuplicate = _context.Members.IgnoreQueryFilters()
                .Any(m => m.FirstName == model.FirstName && m.LastName == model.LastName);

            if (isDuplicate)
            {
                TempData["error"] = "ชื่อและนามสกุลนี้มีอยู่แล้วในระบบ";
                return RedirectToAction("Index");
            }

         // 2. เคลียร์ ID ให้เป็น 0 เสมอเพื่อให้ DB รันเลขใหม่ให้เอง
            model.Id = 0; 
        
            // 3. ใส่ OwnerId (ถ้าไม่ได้ใช้ Filter แล้ว จะปล่อยว่างหรือใส่ ID admin ก็ได้)
            model.OwnerId = GetCurrentUserId();

            _context.Members.Add(model);
            _context.SaveChanges();

            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            // ถ้าพัง มันจะบอกสาเหตุที่หน้าจอเลยครับ
            return Content("เกิดข้อผิดพลาด: " + ex.InnerException?.Message ?? ex.Message);
        }
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

    [HttpGet]
    public IActionResult Search(string keyword)
    {
        var members = _context.Members
            .Where(x => string.IsNullOrEmpty(keyword)
                || x.FirstName.Contains(keyword)
                || x.LastName.Contains(keyword)
                || x.Role.Contains(keyword))
            .Select(x => new {
                x.Id,
                x.FirstName,
                x.LastName,
                x.Role
            })
            .ToList();

        return Json(members);
    }
}