using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.ViewModels
{
    /// <summary>Anket listesinde her bir satır için kullanılır</summary>
    public class SurveyListItemViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusBadgeClass { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
        public int ResponseCount { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public string CreatedByBirim { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsAnonymous { get; set; }
        public string TargetRoles { get; set; } = string.Empty;

        // Onay sistemi
        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;
        public string? ApprovalNote { get; set; }

        public string ApprovalBadgeClass => ApprovalStatus switch
        {
            ApprovalStatus.Approved => "bg-success",
            ApprovalStatus.Rejected => "bg-danger",
            _ => "bg-warning text-dark"
        };

        public string ApprovalLabel => ApprovalStatus switch
        {
            ApprovalStatus.Approved => "Onaylandı",
            ApprovalStatus.Rejected => "Reddedildi",
            _ => "Onay Bekliyor"
        };
    }
}