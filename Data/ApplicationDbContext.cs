using Microsoft.EntityFrameworkCore;
using POOC.Models;
using System.Security.Claims; // ต้องใช้สำหรับ Claims
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Loan>()
                .HasMany(x => x.LoanDetails)
                .WithOne(x => x.Loan)
                .HasForeignKey(x => x.LoanId);

            var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            modelBuilder.Entity<Member>().HasQueryFilter(m => 
                _httpContextAccessor.HttpContext != null && 
                m.OwnerId == _httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
            modelBuilder.Entity<Loan>().HasQueryFilter(l => 
                _httpContextAccessor.HttpContext != null && 
                l.OwnerId == _httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
        }
    }
}