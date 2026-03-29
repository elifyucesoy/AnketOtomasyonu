using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.ViewModels
{
    public class SuperAdminDashboardViewModel
    {
        public int TotalSurveys { get; set; }
        public int ActiveSurveys { get; set; }
        public int DraftSurveys { get; set; }
        public int PendingApprovalCount { get; set; }
        public int TotalResponses { get; set; }
        public int TotalAdminCount { get; set; }

        /// <summary>Seçili fakülte filtresi (null = tümü)</summary>
        public string? SelectedBirim { get; set; }

        /// <summary>Tüm mevcut birim/fakülte adları (filtre dropdown için)</summary>
        public List<string> AllBirimler { get; set; } = new();

        public List<SurveyListItemViewModel> Surveys { get; set; } = new();
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
    }

    public class SuperAdminResultsViewModel
    {
        public string? SelectedBirim { get; set; }
        public List<string> AllBirimler { get; set; } = new();
        public List<SurveyListItemViewModel> Surveys { get; set; } = new();
    }
}
