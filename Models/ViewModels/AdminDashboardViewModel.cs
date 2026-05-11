using AnketOtomasyonu.Models.Entities;
using System.ComponentModel.DataAnnotations;

namespace AnketOtomasyonu.Models.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalSurveys { get; set; }
        public int TotalResponses { get; set; }
        public int ActiveSurveys { get; set; }
        public int DraftSurveys { get; set; }
        public int PassiveSurveys { get; set; }
        public int PendingApprovalCount { get; set; }
        public int RejectedCount { get; set; }

        // Çoklu birim yetkisi ve filtreleme için
        public List<string> AuthorizedUnits { get; set; } = new();
        public string? SelectedBirim { get; set; }

        /// <summary>Anket durum filtresi: "active" | "draft" | "passive" | "pending" | "rejected" | null</summary>
        public string? SurveyStatusFilter { get; set; }

        /// <summary>View uyumluluğu için alias</summary>
        public string? StatusFilter
        {
            get => SurveyStatusFilter;
            set => SurveyStatusFilter = value;
        }

        /// <summary>Onay durum filtresi: "approved" | "pending" | "rejected" | null</summary>
        public string? ApprovalFilter { get; set; }
        public string? StartDateStr { get; set; }
        public string? EndDateStr { get; set; }

        /// <summary>Sıralama: "newest" (varsayılan) | "oldest"</summary>
        public string DateSort { get; set; } = "newest";

        // Sayfalama
        public int CurrentPage { get; set; } = 1;
        public int TotalCount { get; set; }
        public int PageSize { get; set; } = 25;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        /// <summary>Dashboard liste anketleri (sayfalı)</summary>
        public List<SurveyListItemViewModel> RecentSurveys { get; set; } = new();
    }
}