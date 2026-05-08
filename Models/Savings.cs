using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace POOC.Models
{
    public class Savings
    {
        public int Id { get; set; }
        public int MemberId { get; set; } 
        public decimal Amount { get; set; } 
        public decimal Balance { get; set; } 
        public DateTime TransactionDate { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
    }
}