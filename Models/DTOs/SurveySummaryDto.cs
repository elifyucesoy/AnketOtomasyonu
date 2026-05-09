using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.DTOs
{
    /// <summary>
    /// Liste / dashboard için — soru ve yanıt koleksiyonları yerine SQL tarafında sayılır.
    /// </summary>
    public sealed class SurveySummaryDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public SurveyStatus Status { get; set; }
        public SurveyType SurveyType { get; set; }
        public string CreatedByUserId { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string TargetRoles { get; set; } = string.Empty;
        public bool IsAnonymous { get; set; }
        public string? TargetFaculties { get; set; }
        public string? TargetDepartments { get; set; }
        public string? CreatedByBirim { get; set; }
        public int? UnitId { get; set; }
        public string? UnitName { get; set; }
        public ApprovalStatus ApprovalStatus { get; set; }
        public string? ApprovalNote { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public int QuestionCount { get; set; }
        public int ResponseCount { get; set; }
    }
}
