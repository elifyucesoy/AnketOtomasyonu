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

        [StringLength(1000)]
        [Display(Name = "Açıklama")]
        public string Description { get; set; } = string.Empty;

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

        public List<QuestionCreateViewModel> Questions { get; set; } = new();

        /// <summary>Mevcut roller (checkbox listesi için) — API'deki UserType değerlerine karşılık gelir</summary>
        public List<string> AvailableRoles { get; set; } = new() { "Employee", "Student" };
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