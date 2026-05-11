using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Services.Interfaces
{
    public interface ISurveyService
    {
        Task<Survey?> GetSurveyWithQuestionsAsync(int surveyId);

        /// <summary>
        /// ÇOK HAFİF anket başlık bilgisi: yalnızca durum / onay / tip / başlık / hedef gibi alanlar.
        /// Soru, seçenek, yanıt ve hedef birim listeleri YÜKLENMEZ; sadece yetki ve yönlendirme kararı için kullanılır.
        /// (CourseEvaluation, SurveyResponse erişim kontrolleri gibi sık çağrılan yerlerde kullanın.)
        /// </summary>
        Task<Survey?> GetSurveyMetadataAsync(int surveyId);

        /// <summary>Düzenleme / önizleme için — sorular + hedef birimler; yanıt kayıtları yüklenmez (hafif).</summary>
        Task<Survey?> GetSurveyForEditAsync(int surveyId);

        /// <summary>
        /// Ders değerlendirme akışı için: <see cref="Survey.TargetUnits"/> ve
        /// <see cref="Survey.Responses"/> include edilmez; yalnızca Questions + Options.
        /// </summary>
        Task<Survey?> GetSurveyWithQuestionsOnlyAsync(int surveyId);

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

        /// <summary>
        /// Birim Admin paneli: yalnızca yetkili birim adlarıyla örtüşen anketler
        /// (<c>CreatedByBirim</c>, <c>UnitName</c>, <c>SurveyBirim</c>).
        /// </summary>
        Task<IReadOnlyList<SurveySummaryDto>> GetSurveySummariesForAdminUnitScopeAsync(
            IReadOnlyList<string> authorizedUnitNames);

        /// <summary>Admin paneli / önizleme / sonuçlar: anket bu birim kapsamında mı?</summary>
        Task<bool> IsSurveyInAdminUnitScopeAsync(int surveyId, IReadOnlyList<string> authorizedUnitNames);

        /// <summary>Anketi bu kullanıcı oluşturdu mu (düzenle / sil / yayın — yalnızca sahip).</summary>
        Task<bool> IsSurveyCreatedByUserAsync(int surveyId, string? userId);
        Task<IEnumerable<Survey>> GetSurveysByBirimAsync(string birim);
        Task<IEnumerable<Survey>> GetSurveysByBirimsAsync(List<string> birims);
        /// <param name="expandAllTokenScope">
        /// «Tüm birimler» jetonu (<c>__ALL__</c>) genişletilirken kullanılacak birim adları.
        /// <c>null</c>: CachedUnits içindeki tüm aktif birimler. Dolu liste: yalnızca bu adlar (yetkili birimler).
        /// </param>
        Task<Survey> CreateSurveyAsync(
            SurveyCreateDto dto,
            string creatorUserId,
            string creatorName,
            string? creatorBirim = null,
            bool isSuperAdmin = false,
            IReadOnlyList<string>? expandAllTokenScope = null);
        Task PublishSurveyAsync(int surveyId);
        Task CloseSurveyAsync(int surveyId);
        Task DeleteSurveyAsync(int surveyId);
        /// <summary>
        /// Anketi günceller. resetToApproval=true ise durum Taslak'a çekilir ve
        /// onay durumu Pending'e sıfırlanır (Admin düzenlemelerinde kullanılır).
        /// </summary>
        Task UpdateSurveyAsync(
            int surveyId,
            SurveyCreateDto dto,
            bool resetToApproval = false,
            IReadOnlyList<string>? expandAllTokenScope = null);
    }
}