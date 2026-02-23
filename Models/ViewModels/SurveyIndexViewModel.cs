namespace AnketOtomasyonu.Models.ViewModels
{
    /// <summary>Ana sayfa - Anket kartları listesi</summary>
    public class SurveyIndexViewModel
    {
        public List<SurveyListItemViewModel> Surveys { get; set; } = new();
        public string? UserRole { get; set; }
        public string? UserFullName { get; set; }
        public bool IsLoggedIn { get; set; }
    }
}