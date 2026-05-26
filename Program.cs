using Microsoft.EntityFrameworkCore;
using POOC.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using POOC.Models;
using QuestPDF.Infrastructure;
using POOC.Services;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=loan_data.db";

var dbOptions = (DbContextOptionsBuilder options) =>
{
    options.UseSqlite(connectionString);
    if (builder.Environment.IsDevelopment())
    {
        options.LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information)
               .EnableSensitiveDataLogging();
    }
};
builder.Services.AddDbContextPool<ApplicationDbContext>(dbOptions, poolSize: 2);

// ── Authentication ──
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        // Dev = HTTP ได้ปกติ, Production = HTTPS เท่านั้น
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy =>
        policy.RequireClaim("IsAdmin", "true"));
});

builder.Services.AddHttpContextAccessor();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

QuestPDF.Settings.License = LicenseType.Community;

// Rate limiting
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<POOC.Services.LoginRateLimiter>();

// Auto backup
// builder.Services.AddHostedService<POOC.Services.SqliteBackupService>();

var invariantCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = invariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = invariantCulture;

var app = builder.Build();

// ── Middleware ──
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

// ── Database seed ──
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate();

    DatabaseInitializer.EnsureColumn(context, "Users", "IsAdmin", "INTEGER NOT NULL DEFAULT 0");

    if (!context.SystemSettings.Any(x => x.Key == "PenaltyRate"))
        context.SystemSettings.Add(new SystemSetting { Key = "PenaltyRate", Value = "1.5", Description = "อัตราค่าปรับต่อเดือน (%)" });

    if (!context.SystemSettings.Any(x => x.Key == "SavingsInterestDefaultRate"))
        context.SystemSettings.Add(new SystemSetting { Key = "SavingsInterestDefaultRate", Value = "5", Description = "อัตราดอกเบี้ยเงินฝากเริ่มต้น (%)" });

    if (!context.Users.Any(u => u.Username == "admin"))
    {
        context.Users.Add(new User
        {
            Username = "admin",
            Password = PasswordHashService.HashPassword("123"),
            FullName = "Admin",
            IsAdmin = true
        });
    }
    else
    {
        var adminUser = context.Users.FirstOrDefault(u => u.Username == "admin");
        if (adminUser != null && !adminUser.IsAdmin)
            adminUser.IsAdmin = true;
    }

    foreach (var user in context.Users.Where(u => !u.Password.StartsWith("PBKDF2$")))
        user.Password = PasswordHashService.HashPassword(user.Password);

    context.SaveChanges();
}

app.Run();