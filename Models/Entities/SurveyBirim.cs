using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AnketOtomasyonu.Models.Entities
{
    public class SurveyBirim
    {
        [Key]
        public int Id { get; set; }

        public int SurveyId { get; set; }
        public Survey Survey { get; set; } = null!;

        [Required]
        [MaxLength(300)]
        public string Birim { get; set; } = string.Empty;
    }
}
