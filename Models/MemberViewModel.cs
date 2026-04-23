using System.Collections.Generic;

namespace POOC.Models
{
    public class MemberViewModel
    {
        public List<Member> Members { get; set; } = new();
        public List<Loan> Loans { get; set; } = new();
        public List<LoanSchedule> Schedule { get; set; } = new();
    }
}