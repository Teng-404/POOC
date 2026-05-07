using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using POOC.Data;
using POOC.Models;
using System.Security.Claims;

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

    // DELETE
    [HttpPost]
    public IActionResult Delete(int id)
    {
        try
        {
            // 1. ค้นหาสมาชิกพร้อมสัญญาและรายละเอียดงวดทั้งหมด
            var member = _context.Members
                .Include(m => m.Loans)
                    .ThenInclude(l => l.LoanDetails)
                .FirstOrDefault(m => m.Id == id);

            if (member == null) return Json(new { success = false, message = "ไม่พบข้อมูลสมาชิก" });

            // 2. ลบข้อมูลที่เกี่ยวข้องทั้งหมด (ถ้ามี)
            foreach (var loan in member.Loans)
            {
                _context.LoanDetails.RemoveRange(loan.LoanDetails); // ลบงวด
            }
            _context.Loans.RemoveRange(member.Loans); // ลบสัญญา
            
            // 3. ลบตัวสมาชิก
            _context.Members.Remove(member);
            _context.SaveChanges();

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "ลบไม่สำเร็จ: " + ex.Message });
        }
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
    [HttpGet]
    public IActionResult GetMemberDetail(int id)
    {
        var member = _context.Members.FirstOrDefault(m => m.Id == id);
        if (member == null) return Json(new { success = false, message = "ไม่พบข้อมูลสมาชิก" });

        // ดึงชื่อ Admin ผู้สร้าง (ถ้ามี)
        var creator = _context.Users.FirstOrDefault(u => u.Id.ToString() == member.OwnerId);

        return Json(new { success = true, data = new {
            id = member.Id,
            firstName = member.FirstName,
            lastName = member.LastName,
            role = member.Role,
            citizenId = member.CitizenId,
            phone = member.Phone,
            address = member.Address,
            ownerName = creator != null ? creator.FullName : "ไม่พบข้อมูลผู้ดูแล"
        }});
    }
    [HttpPost]
    public IActionResult UpdateMember([FromBody] Member model)
    {
        if (model == null) return Json(new { success = false, message = "ไม่ได้รับข้อมูล" });

        var member = _context.Members.Find(model.Id);
        if (member == null) return Json(new { success = false, message = "ไม่พบสมาชิก" });

        member.FirstName = model.FirstName;
        member.LastName = model.LastName;
        member.Role = model.Role;
        member.CitizenId = model.CitizenId;
        member.Phone = model.Phone;
        member.Address = model.Address;

        try {
            _context.SaveChanges();
            return Json(new { success = true });
        }
        catch (Exception ex) {
            return Json(new { success = false, message = "บันทึกไม่สำเร็จ: " + ex.Message });
        }
    }
}