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
        public DateTime CreatedAt { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsAnonymous { get; set; }
        public string TargetRoles { get; set; } = string.Empty;
    }
}