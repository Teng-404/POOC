using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using POOC.Data;
using POOC.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

[Authorize]
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
                .Any(m => !m.IsDeleted && m.FirstName == model.FirstName && m.LastName == model.LastName);

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
            AddAuditLog("Create", "Member", model.Id, $"เพิ่มสมาชิก {model.FirstName} {model.LastName}");
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

            // 2. Soft delete ข้อมูลที่เกี่ยวข้องทั้งหมด เพื่อเก็บประวัติการเงินไว้ตรวจสอบย้อนหลัง
            var userId = GetCurrentUserId();

            foreach (var loan in member.Loans)
            {
                loan.IsDeleted = true;
                loan.DeletedDate = DateTime.Now;
                loan.DeletedBy = userId;
                loan.Status = "Cancelled";
            }

            // 3. Soft delete ตัวสมาชิก
            member.IsDeleted = true;
            member.DeletedDate = DateTime.Now;
            member.DeletedBy = userId;

            AddAuditLog("SoftDelete", "Member", member.Id, $"ลบสมาชิกแบบ soft delete {member.FirstName} {member.LastName}");
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
            AddAuditLog("Update", "Member", member.Id, $"แก้ไขข้อมูลสมาชิก {member.FirstName} {member.LastName}");
            _context.SaveChanges();
            return Json(new { success = true });
        }
        catch (Exception ex) {
            return Json(new { success = false, message = "บันทึกไม่สำเร็จ: " + ex.Message });
        }
    }
    [HttpGet]
    public IActionResult GetSavingsBalance(int memberId)
    {
        var history = _context.Savings
            .Where(s => s.MemberId == memberId)
            .OrderByDescending(s => s.TransactionDate)
            .Take(1000)
            .ToList();

        // คำนวณยอดเงินรวม
        var balance = _context.Savings
            .Where(s => s.MemberId == memberId)
            .Sum(s => s.Amount);

        return Json(new { 
            balance = balance, 
            history = history 
        });
    }
    [HttpPost]
    public IActionResult SaveTransaction([FromBody] SavingsRequest req)
    {
        // ตรวจสอบเงินถอนว่าพอไหม
        var currentBalance = _context.Savings.Where(s => s.MemberId == req.MemberId).Sum(s => s.Amount);
        decimal amount = req.Type == "deposit" ? req.Amount : -req.Amount;

        if (req.Type == "withdraw" && currentBalance < req.Amount)
            return Json(new { success = false, message = "ยอดเงินคงเหลือไม่พอสำหรับการถอน" });

        var transaction = new Savings {
            MemberId = req.MemberId,
            Amount = amount,
            Balance = currentBalance + amount,
            Description = req.Type == "deposit" ? "ฝากเงินสด" : "ถอนเงินสด"
        };

        _context.Savings.Add(transaction);
        AddAuditLog(req.Type == "deposit" ? "Deposit" : "Withdraw", "Savings", req.MemberId, $"{transaction.Description} จำนวน {Math.Abs(amount):N2} บาท");
        _context.SaveChanges();

        return Json(new { success = true });
    }
    public class SavingsRequest {
        public int MemberId { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; } = string.Empty;
    }
    [HttpPost]
    public IActionResult CalculateAnnualInterest([FromBody] InterestRequest req)
    {
        int year = req.Year > 0 ? req.Year : DateTime.Now.Year;
        decimal rate = req.Rate > 0 ? req.Rate : GetDecimalSetting("SavingsInterestDefaultRate", 5m);

        var alreadyDone = _context.SavingsInterests.Any(x => x.Year == year);
        if (alreadyDone)
            return Json(new { success = false, message = $"คิดดอกเบี้ยปี {year} ไปแล้ว" });

        var members = _context.Members.IgnoreQueryFilters().Where(m => !m.IsDeleted).ToList();
        var results = new List<object>();

        foreach (var m in members)
        {
            var balance = _context.Savings
                .Where(s => s.MemberId == m.Id)
                .Sum(s => (decimal?)s.Amount) ?? 0;

            if (balance <= 0) continue;

            decimal interest = Math.Round(balance * rate / 100, 2);

            _context.SavingsInterests.Add(new SavingsInterest {
                MemberId = m.Id,
                Year = year,
                PrincipalSnapshot = balance,
                Rate = rate,
                InterestAmount = interest
            });

            var newBalance = balance + interest;
            _context.Savings.Add(new Savings {
                MemberId = m.Id,
                Amount = interest,
                Balance = newBalance,
                Description = $"ดอกเบี้ยเงินฝากประจำปี {year} ({rate}%)"
            });

            results.Add(new {
                name = $"{m.FirstName} {m.LastName}",
                principal = balance,
                interest
            });
        }

        AddAuditLog("CalculateAnnualInterest", "SavingsInterest", null, $"คิดดอกเบี้ยเงินฝากปี {year} อัตรา {rate}% จำนวน {results.Count} ราย");
        _context.SaveChanges();
        return Json(new { success = true, year, rate, results });
    }
    [HttpGet]
    public IActionResult GetSavingsSummary()
    {
        var members = _context.Members.IgnoreQueryFilters().Where(m => !m.IsDeleted).ToList();
        var summary = members.Select(m => {
            var balance = _context.Savings
                .Where(s => s.MemberId == m.Id)
                .Sum(s => (decimal?)s.Amount) ?? 0;

            var totalInterest = _context.SavingsInterests
                .Where(x => x.MemberId == m.Id)
                .Sum(x => (decimal?)x.InterestAmount) ?? 0;

            return new {
                memberId = m.Id,
                name = $"{m.FirstName} {m.LastName}",
                balance,
                totalInterest
            };
        })
        .Where(x => x.balance > 0 || x.totalInterest > 0)
        .OrderByDescending(x => x.balance)
        .ToList();

        return Json(new {
            members = summary,
            totalBalance = summary.Sum(x => x.balance),
            totalInterest = summary.Sum(x => x.totalInterest)
        });
    }
    public class InterestRequest {
        public int Year { get; set; }
        public decimal Rate { get; set; }
    }
    [HttpGet]
    public IActionResult GetExportData()
    {
        var members = _context.Members.IgnoreQueryFilters().Where(m => !m.IsDeleted).ToList();
        
        var result = members.Select(m => {
            var savings = _context.Savings
                .Where(s => s.MemberId == m.Id)
                .Sum(s => (decimal?)s.Amount) ?? 0;

            var loans = _context.Loans.IgnoreQueryFilters()
                .Include(l => l.LoanDetails)
                .Where(l => !l.IsDeleted && l.MemberId == m.Id)
                .ToList();

            var today = DateTime.Now.Date;
            int overdueCount = 0;
            var penaltyRate = (double)GetDecimalSetting("PenaltyRate", 1.5m) / 100;
            double totalPenalty = 0;

            foreach (var loan in loans)
            {
                var startDate = new DateTime(loan.CreatedDate.Year, loan.CreatedDate.Month, 1).AddMonths(1);
                foreach (var d in loan.LoanDetails.Where(d => !d.IsPaid))
                {
                    var dueDate = startDate.AddMonths(d.Installment - 1);
                    if (dueDate < today)
                    {
                        overdueCount++;
                        var monthsLate = ((today.Year - dueDate.Year) * 12) + today.Month - dueDate.Month;
                        if (monthsLate < 1) monthsLate = 1;
                        totalPenalty += d.Payment * penaltyRate * monthsLate;
                    }
                }
            }

            return new {
                ชื่อ = m.FirstName,
                นามสกุล = m.LastName,
                อาชีพ = m.Role,
                เบอร์โทร = m.Phone ?? "-",
                ยอดเงินฝากคงเหลือ = savings,
                จำนวนสัญญากู้ = loans.Count,
                งวดเกินกำหนด = overdueCount,
                ค่าปรับสะสม = Math.Round(totalPenalty, 2)
            };
        }).ToList();

        return Json(result);
    }
    private decimal GetDecimalSetting(string key, decimal defaultValue)
    {
        var value = _context.SystemSettings.FirstOrDefault(x => x.Key == key)?.Value;
        return decimal.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private void AddAuditLog(string action, string entityName, int? entityId, string detail)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = GetCurrentUserId(),
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Detail = detail
        });
    }
}