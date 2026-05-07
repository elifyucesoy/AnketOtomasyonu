using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Services.Interfaces
{
    public interface ISurveyService
    {
        Task<Survey?> GetSurveyWithQuestionsAsync(int surveyId);

        /// <summary>Düzenleme / önizleme için — sorular + hedef birimler; yanıt kayıtları yüklenmez (hafif).</summary>
        Task<Survey?> GetSurveyForEditAsync(int surveyId);

        /// <summary>Tüm anketler + tüm yanıtlar (ağır). Yalnızca gerçekten tam graf gerektiğinde kullanın.</summary>
        Task<IEnumerable<Survey>> GetAllSurveysAsync();

        /// <summary>Liste / ana sayfa / paneller için — yanıtları yüklemez, sayıları SQL ile hesaplar.</summary>
        Task<IReadOnlyList<SurveySummaryDto>> GetAllSurveySummariesAsync();

        Task<IEnumerable<Survey>> GetActiveSurveysAsync();
        Task<IEnumerable<Survey>> GetActiveAnonymousSurveysAsync();

        /// <summary>Anonim aktif anket listesi (PublicSurveys) — hafif.</summary>
        Task<IReadOnlyList<SurveySummaryDto>> GetActiveAnonymousSurveySummariesAsync();

        Task<IEnumerable<Survey>> GetSurveysByCreatorAsync(string creatorUserId);

        /// <summary>Anket kartlarında hedef birim etiketleri için SurveyBirim satırları (toplu).</summary>
        Task<Dictionary<int, List<string>>> GetTargetUnitNamesBySurveyIdsAsync(IReadOnlyList<int> surveyIds);

        /// <summary>Admin anket listesi için — hafif.</summary>
        Task<IReadOnlyList<SurveySummaryDto>> GetSurveySummariesByCreatorAsync(string creatorUserId);
        Task<IEnumerable<Survey>> GetSurveysByBirimAsync(string birim);
        Task<IEnumerable<Survey>> GetSurveysByBirimsAsync(List<string> birims);
        Task<Survey> CreateSurveyAsync(SurveyCreateDto dto, string creatorUserId, string creatorName, string? creatorBirim = null, bool isSuperAdmin = false);
        Task PublishSurveyAsync(int surveyId);
        Task CloseSurveyAsync(int surveyId);
        Task DeleteSurveyAsync(int surveyId);
        /// <summary>
        /// Anketi günceller. resetToApproval=true ise durum Taslak'a çekilir ve
        /// onay durumu Pending'e sıfırlanır (Admin düzenlemelerinde kullanılır).
        /// </summary>
        Task UpdateSurveyAsync(int surveyId, SurveyCreateDto dto, bool resetToApproval = false);
    }
}