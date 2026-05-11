using System.ComponentModel.DataAnnotations;
using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.DTOs
{
    public class SurveyCreateDto
    {
        [Required(ErrorMessage = "Anket başlığı zorunludur")]
        [StringLength(200, ErrorMessage = "Başlık en fazla 200 karakter olabilir")]
        public string Title { get; set; } = string.Empty;

        /// <summary>Anket tipi: Normal veya CourseEvaluation (Ders Değerlendirme)</summary>
        public SurveyType SurveyType { get; set; } = SurveyType.Normal;

        [StringLength(1000)]
        public string Description { get; set; } = string.Empty;

        /// <summary>Hangi roller bu anketi doldurabilir</summary>
        public List<string> TargetRoles { get; set; } = new();

        public bool IsAnonymous { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> TargetFaculties { get; set; } = new();
        public List<string> TargetDepartments { get; set; } = new();
        public string? CreatedByBirim { get; set; }

        /// <summary>UnitList'ten seçilen birimin API Id'si (raporlama/filtreleme için)</summary>
        public int? UnitId { get; set; }

        /// <summary>UnitList'ten seçilen birimin adı (metin, raporlama kolaylığı için)</summary>
        public string? UnitName { get; set; }

        public List<QuestionCreateDto> Questions { get; set; } = new();
    }
}
