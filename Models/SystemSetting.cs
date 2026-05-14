using System.ComponentModel.DataAnnotations;

namespace POOC.Models
{
    public class SystemSetting
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Key { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Value { get; set; } = string.Empty;

        public string? Description { get; set; }
        public DateTime UpdatedDate { get; set; } = DateTime.Now;
    }
}
