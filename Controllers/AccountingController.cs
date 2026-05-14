using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POOC.Data;
using POOC.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text;

[Authorize]
public class AccountingController : Controller
{
    private readonly ApplicationDbContext _context;
    private static readonly CultureInfo ThaiCulture = new("th-TH");

    public AccountingController(ApplicationDbContext context)
    {
        _context = context;
    }

    public IActionResult Index(DateTime? fromDate, DateTime? toDate)
    {
        var (from, to) = NormalizeDateRange(fromDate, toDate);
        var report = BuildReport(from, to);
        return View(report);
    }

    [HttpGet]
    public IActionResult ExportCsv(DateTime? fromDate, DateTime? toDate)
    {
        var (from, to) = NormalizeDateRange(fromDate, toDate);
        var report = BuildReport(from, to);
        var csv = new StringBuilder();
        csv.AppendLine("วันที่,เลขที่เอกสาร,ประเภท,สมาชิก,รายละเอียด,เดบิต,เครดิต");

        foreach (var row in report.LedgerRows)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                EscapeCsv(row.Date.ToString("yyyy-MM-dd")),
                EscapeCsv(row.DocumentNo),
                EscapeCsv(row.Type),
                EscapeCsv(row.MemberName),
                EscapeCsv(row.Description),
                row.Debit.ToString("0.00", CultureInfo.InvariantCulture),
                row.Credit.ToString("0.00", CultureInfo.InvariantCulture)
            }));
        }

        var fileName = $"Accounting_{from:yyyyMMdd}_{to:yyyyMMdd}.csv";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", fileName);
    }

    [HttpGet]
    public IActionResult ExportPdf(DateTime? fromDate, DateTime? toDate)
    {
        var (from, to) = NormalizeDateRange(fromDate, toDate);
        var report = BuildReport(from, to);
        var document = CreateAccountingReportDocument(report);

        return File(document.GeneratePdf(), "application/pdf", $"Accounting_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");
    }

    private AccountingIndexViewModel BuildReport(DateTime from, DateTime to)
    {
        var visibleMembers = _context.Members.AsNoTracking().ToList();
        var visibleMemberIds = visibleMembers.Select(m => m.Id).ToHashSet();
        var memberNames = visibleMembers.ToDictionary(m => m.Id, m => $"{m.FirstName} {m.LastName}".Trim());

        var savings = _context.Savings.AsNoTracking()
            .Where(s => visibleMemberIds.Contains(s.MemberId) && s.TransactionDate.Date >= from && s.TransactionDate.Date <= to)
            .ToList();

        var loans = _context.Loans.AsNoTracking()
            .Include(l => l.Member)
            .Include(l => l.LoanDetails)
            .Where(l => l.CreatedDate.Date >= from && l.CreatedDate.Date <= to)
            .ToList();

        var loanDetails = _context.LoanDetails.AsNoTracking()
            .Include(d => d.Loan)
                .ThenInclude(l => l!.Member)
            .Where(d => d.IsPaid && d.PaidDate.HasValue && d.PaidDate.Value.Date >= from && d.PaidDate.Value.Date <= to)
            .ToList();

        var ledgerRows = new List<AccountingLedgerRow>();
        var documents = new List<AccountingDocumentRow>();

        foreach (var item in savings)
        {
            var isInterest = item.Description.Contains("ดอกเบี้ยเงินฝาก", StringComparison.OrdinalIgnoreCase);
            var isWithdrawal = item.Amount < 0;
            var amount = Math.Abs(item.Amount);
            var memberName = GetMemberName(memberNames, item.MemberId);
            var documentNo = $"SV-{item.Id:D5}";
            var type = isInterest ? "ดอกเบี้ยเงินฝาก" : isWithdrawal ? "ถอนเงินฝาก" : "ฝากเงิน";

            ledgerRows.Add(new AccountingLedgerRow
            {
                Date = item.TransactionDate,
                DocumentNo = documentNo,
                Type = type,
                MemberName = memberName,
                Description = item.Description,
                Debit = isWithdrawal || isInterest ? amount : 0,
                Credit = !isWithdrawal && !isInterest ? amount : 0
            });

            documents.Add(new AccountingDocumentRow
            {
                Date = item.TransactionDate,
                DocumentNo = documentNo,
                Category = type,
                MemberName = memberName,
                Description = item.Description,
                Amount = amount,
                DownloadUrl = Url.Action(nameof(Receipt), "Accounting", new { type = "savings", id = item.Id }) ?? string.Empty
            });
        }

        foreach (var loan in loans)
        {
            var memberName = loan.Member != null ? $"{loan.Member.FirstName} {loan.Member.LastName}".Trim() : GetMemberName(memberNames, loan.MemberId);
            var documentNo = $"LN-{loan.Id:D5}";
            ledgerRows.Add(new AccountingLedgerRow
            {
                Date = loan.CreatedDate,
                DocumentNo = documentNo,
                Type = "จ่ายเงินกู้",
                MemberName = memberName,
                Description = $"ปล่อยกู้ {loan.Months} งวด อัตรา {loan.Rate:N2}% ต่อปี",
                Debit = (decimal)loan.Amount,
                Credit = 0
            });

            documents.Add(new AccountingDocumentRow
            {
                Date = loan.CreatedDate,
                DocumentNo = documentNo,
                Category = "สัญญาเงินกู้",
                MemberName = memberName,
                Description = $"สัญญาเงินกู้ยอด {loan.Amount:N2} บาท",
                Amount = (decimal)loan.Amount,
                DownloadUrl = Url.Action("DownloadContract", "Loan", new { loanId = loan.Id }) ?? string.Empty
            });
        }

        foreach (var detail in loanDetails)
        {
            if (detail.Loan == null)
            {
                continue;
            }

            var memberName = detail.Loan.Member != null
                ? $"{detail.Loan.Member.FirstName} {detail.Loan.Member.LastName}".Trim()
                : GetMemberName(memberNames, detail.Loan.MemberId);
            var documentNo = $"RC-{detail.Id:D5}";
            ledgerRows.Add(new AccountingLedgerRow
            {
                Date = detail.PaidDate!.Value,
                DocumentNo = documentNo,
                Type = "รับชำระเงินกู้",
                MemberName = memberName,
                Description = $"รับชำระงวดที่ {detail.Installment} ของสัญญา LN-{detail.LoanId:D5}",
                Debit = 0,
                Credit = (decimal)detail.Payment
            });

            documents.Add(new AccountingDocumentRow
            {
                Date = detail.PaidDate.Value,
                DocumentNo = documentNo,
                Category = "ใบเสร็จรับชำระ",
                MemberName = memberName,
                Description = $"เงินต้น {detail.Principal:N2} / ดอกเบี้ย {detail.Interest:N2}",
                Amount = (decimal)detail.Payment,
                DownloadUrl = Url.Action(nameof(Receipt), "Accounting", new { type = "loan", id = detail.Id }) ?? string.Empty
            });
        }

        var summary = new AccountingSummary
        {
            SavingsDeposits = savings.Where(s => s.Amount > 0 && !s.Description.Contains("ดอกเบี้ยเงินฝาก", StringComparison.OrdinalIgnoreCase)).Sum(s => s.Amount),
            SavingsWithdrawals = Math.Abs(savings.Where(s => s.Amount < 0).Sum(s => s.Amount)),
            SavingsInterestPaid = savings.Where(s => s.Amount > 0 && s.Description.Contains("ดอกเบี้ยเงินฝาก", StringComparison.OrdinalIgnoreCase)).Sum(s => s.Amount),
            LoanDisbursed = loans.Sum(l => (decimal)l.Amount),
            LoanPrincipalCollected = loanDetails.Sum(d => (decimal)d.Principal),
            LoanInterestCollected = loanDetails.Sum(d => (decimal)d.Interest),
            OutstandingLoanPrincipal = _context.Loans.AsNoTracking()
                .Include(l => l.LoanDetails)
                .ToList()
                .Sum(l => (decimal)Math.Max(0, l.Amount - l.LoanDetails.Where(d => d.IsPaid).Sum(d => d.Principal)))
        };

        return new AccountingIndexViewModel
        {
            FromDate = from,
            ToDate = to,
            Summary = summary,
            LedgerRows = ledgerRows.OrderByDescending(r => r.Date).ThenByDescending(r => r.DocumentNo).ToList(),
            Documents = documents.OrderByDescending(d => d.Date).ThenByDescending(d => d.DocumentNo).ToList()
        };
    }

    public IActionResult Receipt(string type, int id)
    {
        if (string.Equals(type, "savings", StringComparison.OrdinalIgnoreCase))
        {
            var saving = _context.Savings.AsNoTracking().FirstOrDefault(s => s.Id == id);
            if (saving == null) return NotFound();

            var member = _context.Members.AsNoTracking().FirstOrDefault(m => m.Id == saving.MemberId);
            if (member == null) return NotFound();

            var document = CreateReceiptDocument(
                $"SV-{saving.Id:D5}",
                saving.TransactionDate,
                $"{member.FirstName} {member.LastName}",
                saving.Description,
                Math.Abs(saving.Amount),
                saving.Amount < 0 ? "ใบสำคัญจ่าย" : "ใบรับเงินฝาก");

            return File(document.GeneratePdf(), "application/pdf", $"Receipt_SV_{saving.Id:D5}.pdf");
        }

        if (string.Equals(type, "loan", StringComparison.OrdinalIgnoreCase))
        {
            var detail = _context.LoanDetails.AsNoTracking()
                .Include(d => d.Loan)
                    .ThenInclude(l => l!.Member)
                .FirstOrDefault(d => d.Id == id);
            if (detail?.Loan == null) return NotFound();

            var memberName = detail.Loan.Member != null ? $"{detail.Loan.Member.FirstName} {detail.Loan.Member.LastName}" : "-";
            var document = CreateReceiptDocument(
                $"RC-{detail.Id:D5}",
                detail.PaidDate ?? DateTime.Now,
                memberName,
                $"รับชำระเงินกู้งวดที่ {detail.Installment} เงินต้น {detail.Principal:N2} บาท ดอกเบี้ย {detail.Interest:N2} บาท",
                (decimal)detail.Payment,
                "ใบเสร็จรับชำระเงินกู้");

            return File(document.GeneratePdf(), "application/pdf", $"Receipt_RC_{detail.Id:D5}.pdf");
        }

        return BadRequest();
    }

    private static IDocument CreateAccountingReportDocument(AccountingIndexViewModel report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("TH Sarabun New").FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("รายงานบัญชีและเอกสาร").FontSize(18).SemiBold();
                    col.Item().AlignCenter().Text($"ช่วงวันที่ {report.FromDate.ToString("dd MMM yyyy", ThaiCulture)} - {report.ToDate.ToString("dd MMM yyyy", ThaiCulture)}");
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        AddSummaryBox(row, "เงินรับ", report.Summary.CashInflow);
                        AddSummaryBox(row, "เงินจ่าย", report.Summary.CashOutflow);
                        AddSummaryBox(row, "สุทธิ", report.Summary.NetCashMovement);
                        AddSummaryBox(row, "ลูกหนี้คงเหลือ", report.Summary.OutstandingLoanPrincipal);
                    });

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(65);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(90);
                            columns.RelativeColumn();
                            columns.RelativeColumn(1.6f);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(70);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderStyle).Text("วันที่").SemiBold();
                            header.Cell().Element(HeaderStyle).Text("เลขที่").SemiBold();
                            header.Cell().Element(HeaderStyle).Text("ประเภท").SemiBold();
                            header.Cell().Element(HeaderStyle).Text("สมาชิก").SemiBold();
                            header.Cell().Element(HeaderStyle).Text("รายละเอียด").SemiBold();
                            header.Cell().Element(HeaderStyle).Text("เดบิต").SemiBold();
                            header.Cell().Element(HeaderStyle).Text("เครดิต").SemiBold();

                            static IContainer HeaderStyle(IContainer container) =>
                                container.Background(Colors.Grey.Lighten3).Padding(4);
                        });

                        foreach (var row in report.LedgerRows.Take(80))
                        {
                            table.Cell().Element(ContentStyle).Text(row.Date.ToString("dd/MM/yyyy"));
                            table.Cell().Element(ContentStyle).Text(row.DocumentNo);
                            table.Cell().Element(ContentStyle).Text(row.Type);
                            table.Cell().Element(ContentStyle).Text(row.MemberName);
                            table.Cell().Element(ContentStyle).Text(row.Description);
                            table.Cell().Element(ContentStyle).Text(row.Debit.ToString("N2"));
                            table.Cell().Element(ContentStyle).Text(row.Credit.ToString("N2"));
                        }

                        static IContainer ContentStyle(IContainer container) =>
                            container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
                    });
                });
            });
        });
    }

    private static IDocument CreateReceiptDocument(string documentNo, DateTime date, string memberName, string description, decimal amount, string title)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("TH Sarabun New").FontSize(13));

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text(title).FontSize(20).SemiBold();
                    col.Item().AlignRight().Text($"เลขที่เอกสาร: {documentNo}");
                    col.Item().AlignRight().Text($"วันที่: {date.ToString("dd MMMM yyyy", ThaiCulture)}");
                    col.Item().PaddingTop(15).Text($"ได้รับจาก / จ่ายให้: {memberName}");
                    col.Item().Text($"รายละเอียด: {description}");
                    col.Item().PaddingTop(10).Border(1).Padding(8).Text($"จำนวนเงิน {amount:N2} บาท").FontSize(16).SemiBold();
                    col.Item().PaddingTop(35).Row(row =>
                    {
                        row.RelativeItem().AlignCenter().Text("ลงชื่อ................................ ผู้รับเงิน/ผู้จ่ายเงิน");
                        row.RelativeItem().AlignCenter().Text("ลงชื่อ................................ ผู้ตรวจสอบ");
                    });
                });
            });
        });
    }

    private static void AddSummaryBox(RowDescriptor row, string label, decimal value)
    {
        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(col =>
        {
            col.Item().Text(label).FontSize(10).FontColor(Colors.Grey.Darken1);
            col.Item().Text(value.ToString("N2")).FontSize(14).SemiBold();
        });
    }

    private static void AddHeaderCell(TableDescriptor header, string text)
    {
        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(text).SemiBold();
    }

    private static void AddBodyCell(TableDescriptor table, string text)
    {
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(text);
    }

    private static (DateTime From, DateTime To) NormalizeDateRange(DateTime? fromDate, DateTime? toDate)
    {
        var today = DateTime.Now.Date;
        var from = (fromDate ?? new DateTime(today.Year, today.Month, 1)).Date;
        var to = (toDate ?? today).Date;

        if (from > to)
        {
            (from, to) = (to, from);
        }

        return (from, to);
    }

    private static string GetMemberName(Dictionary<int, string> memberNames, int memberId)
    {
        return memberNames.TryGetValue(memberId, out var name) ? name : $"Member #{memberId}";
    }

    private static string EscapeCsv(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}