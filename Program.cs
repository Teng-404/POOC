using Microsoft.EntityFrameworkCore;
using POOC.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using POOC.Models;
using QuestPDF.Infrastructure;
using POOC.Services;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// ── ลด Memory: ปิด Server header และ features ที่ไม่จำเป็น ──
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});

// ── Database ──
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=loan_data.db";

var dbOptions = (DbContextOptionsBuilder options) =>
{
    options.UseSqlite(connectionString);
    // ปิด logging ใน Production เพื่อลด memory
    if (builder.Environment.IsDevelopment())
    {
        options.LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Warning)
               .EnableSensitiveDataLogging();
    }
};

// ลด pool size เพื่อประหยัด RAM
builder.Services.AddDbContextPool<ApplicationDbContext>(dbOptions, poolSize: 1);

// ── Authentication ──
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax; // เปลี่ยนจาก Strict เป็น Lax
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy =>
        policy.RequireClaim("IsAdmin", "true"));
});

builder.Services.AddHttpContextAccessor();

// ── ลด Memory: ปิด features ที่ไม่ใช้ ──
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = 
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// ── QuestPDF ──
QuestPDF.Settings.License = LicenseType.Community;
QuestPDF.Settings.EnableDebugging = false;

// ── Rate limiting (ใช้ Singleton ประหยัดกว่า Scoped) ──
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<POOC.Services.LoginRateLimiter>();

// ปิด BackgroundService เพื่อประหยัด RAM
// builder.Services.AddHostedService<POOC.Services.SqliteBackupService>();

// ── ใช้ InvariantCulture เพื่อลด overhead ──
var invariantCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = invariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = invariantCulture;

// ── ลด Logging level ใน Production ──
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
}
else
{
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = 
        Microsoft.Extensions.Logging.LogLevel.Error);
}

var app = builder.Build();

// ── Middleware ──
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static files 7 วัน เพื่อลด request
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=604800");
    }
});

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

// ── Force GC หลัง startup เพื่อคืน RAM ──
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

app.Run();
