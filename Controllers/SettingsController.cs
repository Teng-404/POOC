using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
using System.Security.Claims;

[Authorize]
public class SettingsController : Controller
{
    private readonly ApplicationDbContext _context;

    public SettingsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult GetFinancialSettings()
    {
        var settings = _context.SystemSettings
            .Where(x => x.Key == "PenaltyRate" || x.Key == "SavingsInterestDefaultRate")
            .Select(x => new { x.Key, x.Value, x.Description })
            .ToList();

        return Json(settings);
    }

    [HttpPost]
    public IActionResult UpdateFinancialSetting([FromBody] UpdateSettingRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Key))
        {
            return Json(new { success = false, message = "ข้อมูลไม่ถูกต้อง" });
        }

        var allowedKeys = new[] { "PenaltyRate", "SavingsInterestDefaultRate" };
        if (!allowedKeys.Contains(request.Key))
        {
            return Json(new { success = false, message = "ไม่อนุญาตให้แก้ไขค่านี้" });
        }

        if (!decimal.TryParse(request.Value, out var numericValue) || numericValue < 0)
        {
            return Json(new { success = false, message = "ค่าที่ตั้งต้องเป็นตัวเลขและไม่ติดลบ" });
        }

        var setting = _context.SystemSettings.FirstOrDefault(x => x.Key == request.Key);
        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = request.Key,
                Value = numericValue.ToString("0.####"),
                Description = request.Description
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = numericValue.ToString("0.####");
            setting.Description = request.Description ?? setting.Description;
            setting.UpdatedDate = DateTime.Now;
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            Action = "UpdateSetting",
            EntityName = "SystemSetting",
            EntityId = setting.Id,
            Detail = $"แก้ไข {request.Key} เป็น {setting.Value}"
        });
        _context.SaveChanges();

        return Json(new { success = true, setting.Key, setting.Value, setting.Description });
    }

    public class UpdateSettingRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
