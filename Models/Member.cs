using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace POOC.Models
{
    [Index(nameof(FirstName), nameof(LastName), IsUnique = true)]
    public class Member
    {
        public int Id { get; set; }

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;
    }
    public class MemberViewModel
    {
        public List<Member> Members { get; set; } = new();
        public List<Loan> Loans { get; set; } = new();
        public List<LoanSchedule> Schedule { get; set; } = new();
    }
}