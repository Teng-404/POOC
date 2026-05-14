using Microsoft.EntityFrameworkCore;
using POOC.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using POOC.Models;
using QuestPDF.Infrastructure;
using POOC.Services;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// ใช้ SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=loan_data.db"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
    });

builder.Services.AddHttpContextAccessor();

builder.Services.AddControllersWithViews();

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    context.Database.EnsureCreated();

    context.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS SavingsInterests (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MemberId INTEGER NOT NULL,
            Year INTEGER NOT NULL,
            PrincipalSnapshot TEXT NOT NULL,
            Rate TEXT NOT NULL,
            InterestAmount TEXT NOT NULL,
            CreatedDate TEXT NOT NULL
        )
    ");

    context.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS AuditLogs (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId TEXT NOT NULL,
            Action TEXT NOT NULL,
            EntityName TEXT NOT NULL,
            EntityId INTEGER NULL,
            Detail TEXT NULL,
            CreatedDate TEXT NOT NULL
        )
    ");

    context.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS SystemSettings (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ""Key"" TEXT NOT NULL UNIQUE,
            Value TEXT NOT NULL,
            Description TEXT NULL,
            UpdatedDate TEXT NOT NULL
        )
    ");

    DatabaseInitializer.EnsureColumn(context, "Members", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
    DatabaseInitializer.EnsureColumn(context, "Members", "DeletedDate", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Members", "DeletedBy", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Loans", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
    DatabaseInitializer.EnsureColumn(context, "Loans", "DeletedDate", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Loans", "DeletedBy", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Loans", "Status", "TEXT NOT NULL DEFAULT 'Active'");
    DatabaseInitializer.EnsureColumn(context, "Loans", "ClosedDate", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Loans", "GuarantorName", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Loans", "GuarantorPhone", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Loans", "GuarantorAddress", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Loans", "ApprovedDate", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "Loans", "ApprovedBy", "TEXT NULL");
    DatabaseInitializer.EnsureColumn(context, "LoanDetails", "PaidAmount", "REAL NOT NULL DEFAULT 0");
    context.Database.ExecuteSqlRaw("UPDATE LoanDetails SET PaidAmount = Payment WHERE IsPaid = 1 AND PaidAmount = 0");

    if (!context.SystemSettings.Any(x => x.Key == "PenaltyRate"))
    {
        context.SystemSettings.Add(new SystemSetting { Key = "PenaltyRate", Value = "1.5", Description = "อัตราค่าปรับต่อเดือน (%)" });
    }

    if (!context.SystemSettings.Any(x => x.Key == "SavingsInterestDefaultRate"))
    {
        context.SystemSettings.Add(new SystemSetting { Key = "SavingsInterestDefaultRate", Value = "5", Description = "อัตราดอกเบี้ยเงินฝากเริ่มต้น (%)" });
    }

    if (!context.Users.Any(u => u.Username == "admin"))
    {
        context.Users.Add(new User { Username = "admin", Password = PasswordHashService.HashPassword("123"), FullName = "Admin" });
    }

    foreach (var user in context.Users.Where(u => !u.Password.StartsWith("PBKDF2$")))
    {
        user.Password = PasswordHashService.HashPassword(user.Password);
    }

    context.SaveChanges();
}

app.Run();