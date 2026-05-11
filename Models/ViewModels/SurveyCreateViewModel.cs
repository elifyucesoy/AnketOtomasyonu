using System.ComponentModel.DataAnnotations;
using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.ViewModels
{
    public class SurveyCreateViewModel
    {
        [Required(ErrorMessage = "Anket başlığı zorunludur")]
        [StringLength(200, ErrorMessage = "Başlık en fazla 200 karakter olabilir")]
        [Display(Name = "Anket Başlığı")]
        public string Title { get; set; } = string.Empty;

        /// <summary>Anket tipi: Normal veya CourseEvaluation (Ders Değerlendirme — OBIS akışı)</summary>
        [Display(Name = "Anket Tipi")]
        public SurveyType SurveyType { get; set; } = SurveyType.Normal;

        [StringLength(1000)]
        [Display(Name = "Açıklama")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Checkbox etiketleri; kullanıcıya uzaktan atanmış ANKET kodlarının (5 taneden alt küme) her biri ilgili etiketle eşlenir, çoklu izinde VEYA mantığı.
        /// </summary>
        [Display(Name = "Hedef Roller")]
        public List<string> TargetRoles { get; set; } = new();

        [Display(Name = "Anonim Anket")]
        public bool IsAnonymous { get; set; }

        [Display(Name = "Başlangıç Tarihi")]
        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [Display(Name = "Bitiş Tarihi")]
        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }

        [Display(Name = "Hedef Fakülteler/Birimler")]
        public List<string> TargetFaculties { get; set; } = new();

        [Display(Name = "Hedef Bölümler")]
        public List<string> TargetDepartments { get; set; } = new();
        
        [Display(Name = "Anketin Bağlı Olduğu Birim")]
        public string? SelectedBirim { get; set; }

        public List<string> AuthorizedUnits { get; set; } = new();

        public List<QuestionCreateViewModel> Questions { get; set; } = new();

        /// <summary>
        /// Checkbox listesi; personel hedefi uzak sistemde <c>ANKET_IDARI</c> ile eşlenir.
        /// Veritabanında kalan <c>Idari</c> / <c>Employee</c> metinleri kodda yine <c>ANKET_IDARI</c> ile tanınır.
        /// </summary>
        public List<string> AvailableRoles { get; set; } = new()
        {
            "Personel", "Student", "Akademik", "Admin", "SuperAdmin"
        };
    }

    public class QuestionCreateViewModel
    {
        [Required(ErrorMessage = "Soru metni zorunludur")]
        [Display(Name = "Soru")]
        public string Text { get; set; } = string.Empty;

        [Display(Name = "Soru Tipi")]
        public QuestionType Type { get; set; } = QuestionType.Likert;

        [Display(Name = "Zorunlu")]
        public bool IsRequired { get; set; } = true;

        public int OrderIndex { get; set; }

        public List<OptionCreateViewModel> Options { get; set; } = new();
    }

    public class OptionCreateViewModel
    {
        public string Text { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public int? Value { get; set; }
    }
}