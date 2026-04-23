using Microsoft.AspNetCore.Mvc;
using POOC.Data;
using POOC.Models;
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private static List<string> _history = new();
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

            return View("Member", vm); // ไป Views/Home/Member.cshtml
    }
    public IActionResult Index()
    {
        ViewBag.History = _history;
        return View();
    }

    [HttpPost]
    public IActionResult Index(double? width, double? length, double? radius, double? coneRadius, double? coneHeight)
    {
        if (width.HasValue && length.HasValue)
        {
            var result = width.Value * length.Value;
            ViewBag.SquareResult = result;
            _history.Add($"Square: {result}");
        }

        if (radius.HasValue)
        {
            var result = Math.PI * Math.Pow(radius.Value, 2);
            ViewBag.CircleResult = result;
            _history.Add($"Circle: {result}");
        }

        if (coneRadius.HasValue && coneHeight.HasValue)
        {
            double r = coneRadius.Value;
            double h = coneHeight.Value;

            var result = Math.PI * r * (r + Math.Sqrt(h * h + r * r));
            ViewBag.ConeResult = result;
            _history.Add($"Cone: {result}");
        }

        ViewBag.History = _history;
        return View();
    }

    [HttpPost]
    public IActionResult ClearHistory()
    {
        _history.Clear();
        return RedirectToAction("Index");
    }
}