using Microsoft.EntityFrameworkCore;
using POOC.Models;

namespace POOC.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Member> Members { get; set; }
        public DbSet<Loan> Loans { get; set; }
        public DbSet<LoanDetail> LoanDetails { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Loan>()
            .HasMany(x => x.LoanDetails)
            .WithOne(x => x.Loan)
            .HasForeignKey(x => x.LoanId);
        }
    }
}