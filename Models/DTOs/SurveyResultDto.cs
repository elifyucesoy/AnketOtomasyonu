using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Models.DTOs
{
    public class SurveyResultDto
    {
        public int SurveyId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TotalResponses { get; set; }
        public List<RespondentInfoDto> Respondents { get; set; } = new();
        public List<QuestionResultDto> Questions { get; set; } = new();
        public List<DepartmentResultDto> DepartmentResults { get; set; } = new();
        public List<FakulteResultDto> FakulteResults { get; set; } = new();
        public List<BirimResultDto> BirimResults { get; set; } = new();
    }

    /// <summary>Anket sonuç filtreleri (dropdown): tek SELECT, ağır Include yok.</summary>
    public class RespondentFilterOptionsDto
    {
        public List<string> Bolumler { get; set; } = new();
        public List<string> Birimler { get; set; } = new();
    }

    public class BirimResultDto
    {
        public string? BirimName { get; set; }
        public int ResponseCount { get; set; }
        public double AverageSatisfaction { get; set; }
    }

    public class FakulteResultDto
    {
        public string? FakulteName { get; set; }
        public int ResponseCount { get; set; }
        public double AverageSatisfaction { get; set; }
    }

    public class DepartmentResultDto
    {
        public string? DepartmentName { get; set; }
        public int ResponseCount { get; set; }
        public double AverageSatisfaction { get; set; } // Likert tipi sorular için ağırlıklı ortalama
    }

    public class RespondentInfoDto
    {
        public string? UserFullName { get; set; }
        public string? FakulteAdi { get; set; }
        /// <summary>UnitList/CachedUnits ile eşleşen birim adı.</summary>
        public string? BirimAdi { get; set; }
        public string? BolumAdi { get; set; }
        /// <summary>GetProfile / UnitList — kayıtlı birim API Id (katılımcı).</summary>
        public int? RespondentUnitId { get; set; }
        /// <summary>UnitList parentId (üst birim referansı).</summary>
        public int? ParentUnitId { get; set; }
        public DateTime SubmittedAt { get; set; }
    }

    public class QuestionResultDto
    {
        public int QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public QuestionType QuestionType { get; set; }
        public int AnswerCount { get; set; }
        public double AverageSatisfaction { get; set; } // Likert ort.
        public List<OptionResultDto> OptionResults { get; set; } = new();
        public List<string> OpenEndedAnswers { get; set; } = new();
    }

    public class OptionResultDto
    {
        public int OptionId { get; set; }
        public string OptionText { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }
}