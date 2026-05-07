namespace AnketOtomasyonu.Models.ViewModels
{
    /// <summary>Ana sayfa - Anket kartları listesi</summary>
    public class SurveyIndexViewModel
    {
        public List<SurveyListItemViewModel> Surveys { get; set; } = new();
        public string? UserRole { get; set; }
        public string? UserFullName { get; set; }
        public bool IsLoggedIn { get; set; }

        /// <summary>ANKET_API_ADMIN veya ANKET_API_SUPER_ADMIN (claim veya rol) — önizleme / taslak düzenleme.</summary>
        public bool CanUseStaffSurveyTools { get; set; }

        /// <summary>Önizleme linki SuperAdmin mi Admin mi controller'a gitsin.</summary>
        public bool PreferSuperAdminSurveyLinks { get; set; }
    }
}