using Microsoft.EntityFrameworkCore;
using POOC.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using POOC.Models;

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

    // --- ส่วนของ User ---
    if (!context.Users.Any(u => u.Username == "admin"))
    {
        context.Users.Add(new User { Username = "admin", Password = "123", FullName = "Admin" });
    }

    context.SaveChanges();
}

app.Run();