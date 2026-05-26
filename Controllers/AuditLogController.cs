using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POOC.Data;
using System.Security.Claims;

[Authorize(Policy = "Admin")]
public class AuditLogController : Controller
{
    private readonly ApplicationDbContext _context;
    public AuditLogController(ApplicationDbContext context) => _context = context;
    public IActionResult Index() => View();

    // GET: /AuditLog/List?page=1&pageSize=50&search=...&actionFilter=...&entity=...&fromDate=...&toDate=...
    [HttpGet]
    [IgnoreAntiforgeryToken]
    public IActionResult List(
        int page = 1,
        int pageSize = 50,
        string? search = null,
        string? actionFilter = null,
        string? entity = null,
        string? fromDate = null,
        string? toDate = null)
    {
        try
        {
            var query = _context.AuditLogs.IgnoreQueryFilters().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(x =>
                    x.UserId.ToLower().Contains(s) ||
                    x.Action.ToLower().Contains(s) ||
                    x.EntityName.ToLower().Contains(s) ||
                    (x.Detail != null && x.Detail.ToLower().Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(actionFilter))
                query = query.Where(x => x.Action == actionFilter);

            if (!string.IsNullOrWhiteSpace(entity))
                query = query.Where(x => x.EntityName == entity);

            if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fd))
                query = query.Where(x => x.CreatedDate >= fd);

            if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var td))
                query = query.Where(x => x.CreatedDate < td.AddDays(1));

            var total = query.Count();

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
        catch (Exception ex)
        {
            return Json(new { error = ex.Message, inner = ex.InnerException?.Message });
        }
    }

    // GET: /AuditLog/FilterOptions
    [HttpGet]
    [IgnoreAntiforgeryToken]
    public IActionResult FilterOptions()
    {
        var actions  = _context.AuditLogs.IgnoreQueryFilters().Select(x => x.Action).Distinct().OrderBy(x => x).ToList();
        var entities = _context.AuditLogs.IgnoreQueryFilters().Select(x => x.EntityName).Distinct().OrderBy(x => x).ToList();
        return Json(new { actions, entities });
    }

    [HttpGet]
    [IgnoreAntiforgeryToken]
    public IActionResult Debug()
    {
        var count        = _context.AuditLogs.Count();
        var countIgnored = _context.AuditLogs.IgnoreQueryFilters().Count();
        var latest = _context.AuditLogs.IgnoreQueryFilters()
            .OrderByDescending(x => x.CreatedDate)
            .Take(5)
            .Select(x => new { x.Id, x.Action, x.UserId, x.CreatedDate })
            .ToList();
        return Json(new { count, countIgnored, latest });
    }
}
