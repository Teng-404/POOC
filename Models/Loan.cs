using System.Text.Json.Serialization;
namespace POOC.Models

{
    public class Loan
    {
        public int Id { get; set; }

        public int MemberId { get; set; }

        public double Amount { get; set; }

        public double Rate { get; set; }

        public int Months { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public List<LoanDetail> LoanDetails { get; set; } = new();
    }
    public class LoanDetail
    {
        public int Id { get; set; }

        public int LoanId { get; set; } 
        public int Installment { get; set; }

        public double Payment { get; set; }
        public double Principal { get; set; }
        public double Interest { get; set; }
        public double Balance { get; set; }
        
        [JsonIgnore]
        public Loan? Loan { get; set; }
    }
    public class LoanRequest
    {
        public int MemberId { get; set; }
        public double Amount { get; set; }
        public double Rate { get; set; }
        public int Months { get; set; }
    }
    public class LoanSchedule
    {
        public int Installment { get; set; }
        public double Payment { get; set; }
        public double Principal { get; set; }
        public double Interest { get; set; }
        public double Balance { get; set; }
    }
    public class DeleteRequest
    {
        public int Id { get; set; }
    }
}