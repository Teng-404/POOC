using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using POOC.Data;
using POOC.Models;
using POOC.Services;

public class AuthController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly LoginRateLimiter _rateLimiter;

    public AuthController(ApplicationDbContext context, LoginRateLimiter rateLimiter)
    {
        _context = context;
        _rateLimiter = rateLimiter;
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View("Index");
    }

    [HttpPost]
    [IgnoreAntiforgeryToken] // หน้า Login ไม่มี form token เดิม
    public async Task<IActionResult> Login(string username, string password)
    {
        // [STEP 4] Rate limiting — บล็อก IP ที่ผิดเกิน 5 ครั้งใน 10 นาที
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_rateLimiter.IsBlocked(ip))
        {
            ViewBag.Error = "ลองเข้าสู่ระบบหลายครั้งเกินไป กรุณารอ 10 นาทีแล้วลองใหม่";
            return View("Index");
        }

        var user = _context.Users.FirstOrDefault(u => u.Username == username);

        if (user != null && PasswordHashService.VerifyPassword(password, user.Password))
        {
            // [STEP 4] login สำเร็จ — reset นับครั้งของ IP นี้
            _rateLimiter.Reset(ip);

            if (!PasswordHashService.IsHashedPassword(user.Password))
            {
                user.Password = PasswordHashService.HashPassword(password);
                AddAuditLog(user.Id.ToString(), "UpgradePasswordHash", "User", user.Id, "อัปเกรดรหัสผ่านจากรูปแบบเดิมเป็น hash");
                _context.SaveChanges();
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                // [แก้ไข #2] ฝัง IsAdmin Claim — ใช้คู่กับ Policy "Admin"
                new Claim("IsAdmin", user.IsAdmin ? "true" : "false")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            AddAuditLog(user.Id.ToString(), "Login", "User", user.Id, "เข้าสู่ระบบสำเร็จ");
            _context.SaveChanges();

            return RedirectToAction("Member", "Home");
        }

        // [STEP 4] login ผิด — นับครั้ง
        _rateLimiter.RecordFailure(ip);
        ViewBag.Error = "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง";
        return View("Index");
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View();
    }

    [Authorize]
    [HttpPost]
    public IActionResult ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            if (isAjax) return Json(new { success = false, message = "รหัสผ่านใหม่ต้องมีอย่างน้อย 8 ตัวอักษร" });
            ViewBag.Error = "รหัสผ่านใหม่ต้องมีอย่างน้อย 8 ตัวอักษร";
            return View();
        }

        if (newPassword != confirmPassword)
        {
            if (isAjax) return Json(new { success = false, message = "ยืนยันรหัสผ่านใหม่ไม่ตรงกัน" });
            ViewBag.Error = "ยืนยันรหัสผ่านใหม่ไม่ตรงกัน";
            return View();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = _context.Users.FirstOrDefault(u => u.Id.ToString() == userId);

        if (user == null || !PasswordHashService.VerifyPassword(currentPassword, user.Password))
        {
            if (isAjax) return Json(new { success = false, message = "รหัสผ่านปัจจุบันไม่ถูกต้อง" });
            ViewBag.Error = "รหัสผ่านปัจจุบันไม่ถูกต้อง";
            return View();
        }

        user.Password = PasswordHashService.HashPassword(newPassword);
        AddAuditLog(user.Id.ToString(), "ChangePassword", "User", user.Id, "เปลี่ยนรหัสผ่านสำเร็จ");
        _context.SaveChanges();

        if (isAjax) return Json(new { success = true, message = "เปลี่ยนรหัสผ่านสำเร็จ" });
        ViewBag.Success = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว";
        return View();
    }

    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        AddAuditLog(userId, "Logout", "User", int.TryParse(userId, out var id) ? id : null, "ออกจากระบบ");
        _context.SaveChanges();

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    private void AddAuditLog(string userId, string action, string entityName, int? entityId, string detail)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Detail = detail
        });
    }
}
