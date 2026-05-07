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

        /// <summary>
        /// Anketi kimin dolduracağını sınırlar; değerler: Employee, Student, Akademik, Idari.
        /// Selçuk izin eşlemesi: <b>Akademik</b> hedefi ANKET_API_AKADEMIK (akademik kadro);
        /// <b>Personel</b> (Employee) ve <b>İdari</b> hedefleri ANKET_IDARI kapsamındaki personel ile uyumludur.
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
        /// Checkbox listesi; <c>Employee</c> arayüzde "Personel" olarak gösterilir.
        /// </summary>
        public List<string> AvailableRoles { get; set; } = new() { "Employee", "Student", "Akademik", "Idari" };
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