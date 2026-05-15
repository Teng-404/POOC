using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using POOC.Models;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace POOC.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor httpContextAccessor)
            : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public DbSet<Member> Members { get; set; }
        public DbSet<Loan> Loans { get; set; }
        public DbSet<LoanDetail> LoanDetails { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Savings> Savings { get; set; }
        public DbSet<SavingsInterest> SavingsInterests { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<SystemSetting> SystemSettings { get; set; }

        // รองรับทุก format ที่ SQLite อาจเก็บไว้ (มี/ไม่มี fractional seconds)
        private static readonly string[] DateTimeFormats = {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.F",
            "yyyy-MM-dd HH:mm:ss.FF",
            "yyyy-MM-dd HH:mm:ss.FFF",
            "yyyy-MM-dd HH:mm:ss.FFFF",
            "yyyy-MM-dd HH:mm:ss.FFFFF",
            "yyyy-MM-dd HH:mm:ss.FFFFFF",
            "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
            "yyyy-MM-dd"
        };

        private static DateTime ParseDt(string v)
        {
            if (DateTime.TryParseExact(v, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var r))
                return r;
            return DateTime.Parse(v, CultureInfo.InvariantCulture);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // บังคับ DateTime ทุกตัวใน SQLite ใช้ InvariantCulture
            // ป้องกัน EF Core parse ปีเป็น พ.ศ. เมื่อ locale เครื่องเป็น th-TH
            var dateTimeConverter = new ValueConverter<DateTime, string>(
                v => v.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                v => ParseDt(v)
            );

            var nullableDateTimeConverter = new ValueConverter<DateTime?, string?>(
                v => v == null ? null : v.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                v => v == null ? null : ParseDt(v)
            );

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                        property.SetValueConverter(dateTimeConverter);
                    else if (property.ClrType == typeof(DateTime?))
                        property.SetValueConverter(nullableDateTimeConverter);
                }
            }

            // Relations
            modelBuilder.Entity<Loan>()
                .HasMany(x => x.LoanDetails)
                .WithOne(x => x.Loan)
                .HasForeignKey(x => x.LoanId);

            modelBuilder.Entity<SystemSetting>()
                .HasIndex(x => x.Key)
                .IsUnique();

            // Query Filters (multi-user isolation)
            modelBuilder.Entity<Member>().HasQueryFilter(m =>
                !m.IsDeleted &&
                _httpContextAccessor.HttpContext != null &&
                m.OwnerId == _httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));

            modelBuilder.Entity<Loan>().HasQueryFilter(l =>
                !l.IsDeleted &&
                _httpContextAccessor.HttpContext != null &&
                l.OwnerId == _httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));

            modelBuilder.Entity<LoanDetail>().HasQueryFilter(d =>
                _httpContextAccessor.HttpContext != null &&
                d.Loan != null &&
                !d.Loan.IsDeleted &&
                d.Loan.OwnerId == _httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
        }
    }
}
