using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalSurveys { get; set; }
        public int TotalResponses { get; set; }
        public int ActiveSurveys { get; set; }
        public int DraftSurveys { get; set; }

        /// <summary>Son oluşturulan anketler (Dashboard'da liste için)</summary>
        public List<SurveyListItemViewModel> RecentSurveys { get; set; } = new();
    }
}