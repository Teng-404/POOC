using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using System.Security.Claims;

[Authorize(Policy = "Admin")]
public class AuditLogController : Controller
{
    private readonly ApplicationDbContext _context;
    public AuditLogController(ApplicationDbContext context) => _context = context;

    // GET: /AuditLog
    public IActionResult Index()
    {
        return View();
    }

    // GET: /AuditLog/List?page=1&pageSize=50&search=...&action=...&entity=...&fromDate=...&toDate=...
    [HttpGet]
    [IgnoreAntiforgeryToken]
    public IActionResult List(
        int page = 1,
        int pageSize = 50,
        string? search = null,
        string? action = null,
        string? entity = null,
        string? fromDate = null,
        string? toDate = null)
    {
        var query = _context.AuditLogs.AsQueryable();

        // ── Filter ──
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(x =>
                x.UserId.ToLower().Contains(s) ||
                x.Action.ToLower().Contains(s) ||
                x.EntityName.ToLower().Contains(s) ||
                (x.Detail != null && x.Detail.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action == action);

        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(x => x.EntityName == entity);

        if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fd))
            query = query.Where(x => x.CreatedDate >= fd);

        if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var td))
            query = query.Where(x => x.CreatedDate < td.AddDays(1));

        var total = query.Count();

        // Join กับ Users เพื่อเอา FullName
        var userMap = _context.Users
            .ToDictionary(u => u.Id.ToString(), u => u.FullName ?? u.Username);

        var items = query
            .OrderByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList()
            .Select(x => new
            {
                x.Id,
                x.UserId,
                UserName = userMap.TryGetValue(x.UserId, out var name) ? name : x.UserId,
                x.Action,
                x.EntityName,
                x.EntityId,
                x.Detail,
                CreatedDate = x.CreatedDate.ToString("dd/MM/yyyy HH:mm:ss")
            })
            .ToList();

        return Json(new
        {
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            items
        });
    }

    // GET: /AuditLog/FilterOptions — ดึง distinct Action + Entity เพื่อใส่ใน dropdown filter
    [HttpGet]
    [IgnoreAntiforgeryToken]
    public IActionResult FilterOptions()
    {
        var actions  = _context.AuditLogs.Select(x => x.Action).Distinct().OrderBy(x => x).ToList();
        var entities = _context.AuditLogs.Select(x => x.EntityName).Distinct().OrderBy(x => x).ToList();
        return Json(new { actions, entities });
    }
}
