using System.ComponentModel.DataAnnotations;
using AnketOtomasyonu.Models.Obis;

namespace AnketOtomasyonu.Models.ViewModels
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Öğrenci giriş formu (OBIS kimlik doğrulama)
    // ─────────────────────────────────────────────────────────────────────────────

    public class CourseEvalLoginViewModel
    {
        public int    SurveyId    { get; set; }
        public string SurveyTitle { get; set; } = string.Empty;

        [Required(ErrorMessage = "Öğrenci numarası zorunludur.")]
        [Display(Name = "Öğrenci Numarası")]
        public string? OgrNo  { get; set; }

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [DataType(DataType.Password)]
        [Display(Name = "Şifre (OBS/OBIS şifresi)")]
        public string? Parola { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Ders listesi sayfası
    // ─────────────────────────────────────────────────────────────────────────────

    public class CourseEvalCoursesViewModel
    {
        public int    SurveyId            { get; set; }
        public string SurveyTitle         { get; set; } = string.Empty;
        public string OgrNo               { get; set; } = string.Empty;
        public string StudentDisplayName  { get; set; } = string.Empty;

        public List<CourseEvalCourseItemViewModel> Courses { get; set; } = new();
    }

    public class CourseEvalCourseItemViewModel
    {
        /// <summary>Benzersiz ders anahtarı — CourseEvalSessionHelper.BuildResponseUserId ile uyumlu.</summary>
        public string Key            { get; set; } = string.Empty;
        /// <summary>Kullanıcıya gösterilecek okunabilir metin (DersNo — DersAdi — Yil)</summary>
        public string DisplayLine    { get; set; } = string.Empty;
        /// <summary>Bu öğrenci bu ders için anketi zaten doldurdu mu?</summary>
        public bool   AlreadyResponded { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Session içinde tutulan durum nesnesi
    // ─────────────────────────────────────────────────────────────────────────────

    public class CourseEvalSessionState
    {
        public int    SurveyId          { get; set; }
        public string OgrNo             { get; set; } = string.Empty;
        /// <summary>Şifre session'da tutulur — dersler sayfasında tekrar OBIS doğrulaması için.</summary>
        public string Parola            { get; set; } = string.Empty;
        public ObisStudentProfile Profile  { get; set; } = new();
        public List<ObisCourseRow>  Courses  { get; set; } = new();
        /// <summary>Öğrencinin seçtiği ders anahtarı (ObisCourseRow.Key)</summary>
        public string? SelectedCourseKey { get; set; }

        /// <summary>
        /// OBIS SOAP servisinden son başarılı ders çekiminin zamanı (UTC).
        /// Sayfa geçişlerinde bu değer üzerinden TTL kontrolü yapılır;
        /// böylece her sayfa açılışında uzak SOAP'a gidilmez (yavaşlama önlenir).
        /// </summary>
        public DateTime? LastObisFetchUtc { get; set; }
    }
}
