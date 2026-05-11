using AnketOtomasyonu.Models.DTOs;

namespace AnketOtomasyonu.Services.Interfaces
{
    public interface ISurveyResponseService
    {
        
        Task<bool> HasRespondedByIpAsync(int surveyId, string ipAddress); // ← YENİ
      
        Task<SurveyResultDto> GetSurveyResultsAsync(int surveyId, string? fakulte = null, string? bolum = null, string? birim = null);
        Task<RespondentFilterOptionsDto> GetRespondentFilterOptionsAsync(int surveyId);
        Task<bool> HasUserRespondedAsync(int surveyId, string userId);

        /// <summary>
        /// Verilen UserId kümesinden hangilerinin bu ankete daha önce yanıt verdiğini
        /// tek bir DB sorgusuyla döndürür (ders listesi gibi N adet kayıt için N+1 önler).
        /// </summary>
        Task<HashSet<string>> GetRespondedUserIdsAsync(int surveyId, IEnumerable<string> userIds);
        /// <param name="userFullName">İmzada uyumluluk için; kayıtta kullanılmaz (null yazılır).</param>
        Task<(bool success, string message)> SubmitResponseAsync(
            SurveySubmitDto dto, string userId, string? ipAddress,
            string? userFullName = null, string? fakulteAdi = null, string? bolumAdi = null,
            int? respondentUnitId = null, string? birimAdi = null);
        
    }
}