using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
using POOC.Services;
using System.Security.Claims;

[Authorize]
public class UserController : Controller
{
    private readonly ApplicationDbContext _context;
    public UserController(ApplicationDbContext context) => _context = context;

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool CurrentUserIsAdmin => User.FindFirstValue("IsAdmin") == "true";

    // GET: หน้าจัดการผู้ใช้ — เฉพาะ Admin
    [Authorize(Policy = "Admin")]  // [แก้ไข #2]
    public IActionResult Index()
    {
        return View();
    }

    // GET: รายชื่อ user ทั้งหมด (JSON) — เฉพาะ Admin
    [HttpGet]
    [Authorize(Policy = "Admin")]  // [แก้ไข #2]
    [IgnoreAntiforgeryToken]       // GET ไม่ต้องการ token
    public IActionResult List()
    {
        var users = _context.Users
            .OrderBy(u => u.Id)
            .Select(u => new {
                u.Id,
                u.Username,
                u.FullName,
                u.CreatedDate,
                u.IsAdmin  // [แก้ไข #1] ใช้ field จริงแทนการเช็ค username
            })
            .ToList();
        return Json(users);
    }

    // POST: เพิ่ม user ใหม่ — เฉพาะ Admin
    [HttpPost]
    [Authorize(Policy = "Admin")]  // [แก้ไข #2]
    [IgnoreAntiforgeryToken]       // [แก้ไข #4] AJAX JSON endpoint ไม่ส่ง form token — ป้องกันโดย Policy แทน
    public IActionResult Add([FromBody] AddUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return Json(new { success = false, message = "กรุณากรอก Username และรหัสผ่าน" });

        if (req.Password.Length < 8)
            return Json(new { success = false, message = "รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร" });

        if (_context.Users.Any(u => u.Username == req.Username))
            return Json(new { success = false, message = "Username นี้มีอยู่แล้ว" });

        var user = new User
        {
            Username = req.Username.Trim(),
            FullName = req.FullName?.Trim() ?? req.Username,
            Password = PasswordHashService.HashPassword(req.Password),
            IsAdmin = req.IsAdmin  // [แก้ไข #1] รองรับการกำหนด role ตอนสร้าง
        };

        _context.Users.Add(user);
        var roleLabel = user.IsAdmin ? "Admin" : "Staff";
        AddAuditLog("CreateUser", "User", null, $"เพิ่มผู้ใช้ '{user.Username}' ({user.FullName}) สิทธิ์: {roleLabel}");
        _context.SaveChanges();

        return Json(new { success = true, id = user.Id });
    }

    // POST: แก้ไข FullName — เฉพาะ Admin
    [HttpPost]
    [Authorize(Policy = "Admin")]  // [แก้ไข #2]
    [IgnoreAntiforgeryToken]
    public IActionResult UpdateName([FromBody] UpdateNameRequest req)
    {
        var user = _context.Users.Find(req.Id);
        if (user == null) return Json(new { success = false, message = "ไม่พบผู้ใช้" });

        var oldName = user.FullName;
        user.FullName = req.FullName?.Trim() ?? user.FullName;

        AddAuditLog("UpdateUser", "User", user.Id, $"แก้ไขชื่อผู้ใช้ '{user.Username}': {oldName} → {user.FullName}");
        _context.SaveChanges();

        return Json(new { success = true });
    }

    // POST: Reset รหัสผ่าน (Admin reset ให้คนอื่น) — เฉพาะ Admin
    // [แก้ไข #3] รับ targetUserId จริงแทนที่จะ reset ตัวเอง
    [HttpPost]
    [Authorize(Policy = "Admin")]  // [แก้ไข #2]
    [IgnoreAntiforgeryToken]
    public IActionResult ResetPassword([FromBody] ResetPasswordRequest req)
    {
        var user = _context.Users.Find(req.Id);
        if (user == null) return Json(new { success = false, message = "ไม่พบผู้ใช้" });

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
            return Json(new { success = false, message = "รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร" });

        user.Password = PasswordHashService.HashPassword(req.NewPassword);
        AddAuditLog("ResetPassword", "User", user.Id, $"Reset รหัสผ่านให้ผู้ใช้ '{user.Username}'");
        _context.SaveChanges();

        return Json(new { success = true });
    }

    // POST: ลบ user — เฉพาะ Admin
    [HttpPost]
    [Authorize(Policy = "Admin")]  // [แก้ไข #2]
    [IgnoreAntiforgeryToken]
    public IActionResult Delete([FromBody] DeleteUserRequest req)
    {
        var user = _context.Users.Find(req.Id);
        if (user == null) return Json(new { success = false, message = "ไม่พบผู้ใช้" });

        // [แก้ไข #1] ใช้ IsAdmin field แทนการเช็ค username == "admin"
        if (user.IsAdmin && user.Username == "admin")
            return Json(new { success = false, message = "ไม่สามารถลบ admin หลักได้" });

        if (user.Id.ToString() == CurrentUserId)
            return Json(new { success = false, message = "ไม่สามารถลบบัญชีของตัวเองได้" });

        var username = user.Username;
        _context.Users.Remove(user);
        AddAuditLog("DeleteUser", "User", req.Id, $"ลบผู้ใช้ '{username}'");
        _context.SaveChanges();

        return Json(new { success = true });
    }

    // POST: เปลี่ยน Role (Admin toggle IsAdmin ให้คนอื่น) — [ใหม่]
    [HttpPost]
    [Authorize(Policy = "Admin")]
    [IgnoreAntiforgeryToken]
    public IActionResult SetRole([FromBody] SetRoleRequest req)
    {
        var user = _context.Users.Find(req.Id);
        if (user == null) return Json(new { success = false, message = "ไม่พบผู้ใช้" });

        if (user.Username == "admin")
            return Json(new { success = false, message = "ไม่สามารถเปลี่ยนสิทธิ์ admin หลักได้" });

        if (user.Id.ToString() == CurrentUserId)
            return Json(new { success = false, message = "ไม่สามารถเปลี่ยนสิทธิ์ตัวเองได้" });

        user.IsAdmin = req.IsAdmin;
        var roleLabel = req.IsAdmin ? "Admin" : "Staff";
        AddAuditLog("SetRole", "User", user.Id, $"เปลี่ยนสิทธิ์ '{user.Username}' → {roleLabel}");
        _context.SaveChanges();

        return Json(new { success = true });
    }

    private void AddAuditLog(string action, string entityName, int? entityId, string detail)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Detail = detail
        });
    }

    // ── Request Models ──
    public class AddUserRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string? FullName { get; set; }
        public bool IsAdmin { get; set; } = false;  // [ใหม่]
    }
    public class UpdateNameRequest   { public int Id { get; set; } public string? FullName { get; set; } }
    public class ResetPasswordRequest { public int Id { get; set; } public string NewPassword { get; set; } = ""; }
    public class DeleteUserRequest   { public int Id { get; set; } }
    public class SetRoleRequest      { public int Id { get; set; } public bool IsAdmin { get; set; } }  // [ใหม่]
}
