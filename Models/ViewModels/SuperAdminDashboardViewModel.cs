using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.ViewModels
{
    public class SuperAdminDashboardViewModel
    {
        public int TotalSurveys { get; set; }
        public int ActiveSurveys { get; set; }
        public int DraftSurveys { get; set; }
        public int PendingApprovalCount { get; set; }
        public int RejectedCount { get; set; }
        public int TotalResponses { get; set; }
        public int TotalAdminCount { get; set; }

        /// <summary>Seçili fakülte filtresi (null = tümü)</summary>
        public string? SelectedBirim { get; set; }

        /// <summary>Anket durum filtresi: "active" | "draft" | "inactive" | "closed" | null</summary>
        public string? SurveyStatusFilter { get; set; }

        /// <summary>View uyumluluğu için alias (Dashboard.cshtml Model.StatusFilter kullanır)</summary>
        public string? StatusFilter
        {
            get => SurveyStatusFilter;
            set => SurveyStatusFilter = value;
        }

        /// <summary>Onay durum filtresi: "approved" | "pending" | "rejected" | null</summary>
        public string? ApprovalFilter { get; set; }

        /// <summary>Tarih aralığı filtresi</summary>
        public string? StartDateStr { get; set; }
        public string? EndDateStr { get; set; }

        /// <summary>Tüm mevcut birim/fakülte adları (filtre dropdown için)</summary>
        public List<string> AllBirimler { get; set; } = new();

        public List<SurveyListItemViewModel> Surveys { get; set; } = new();

        // Sayfalama
        public int CurrentPage { get; set; } = 1;
        public int TotalCount { get; set; }
        public int PageSize { get; set; } = 25;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class AdminPermissionViewModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PersonelBirim { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AdminManagementViewModel
    {
        public List<AdminPermissionViewModel> Admins { get; set; } = new();
        public List<string> AllBirimler { get; set; } = new();
        public string? FilterBirim { get; set; }
        public string? SearchName { get; set; }

        // Sayfalama
        public int CurrentPage { get; set; } = 1;
        public int TotalCount { get; set; }
        public int PageSize { get; set; } = 25;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class SuperAdminResultsViewModel
    {
        public string? SelectedBirim { get; set; }
        public List<string> AllBirimler { get; set; } = new();
        public List<SurveyListItemViewModel> Surveys { get; set; } = new();

        // Tarih aralığı filtresi
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? StartDateStr { get; set; }
        public string? EndDateStr { get; set; }

        /// <summary><c>newest</c> = oluşturma tarihi azalan; <c>oldest</c> = artan.</summary>
        public string DateSort { get; set; } = "newest";
    }
}
