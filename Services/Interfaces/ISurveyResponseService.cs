using AnketOtomasyonu.Models.DTOs;

namespace AnketOtomasyonu.Services.Interfaces
{
    public interface ISurveyResponseService
    {
        
        Task<bool> HasRespondedByIpAsync(int surveyId, string ipAddress); // ← YENİ
      
        Task<SurveyResultDto> GetSurveyResultsAsync(int surveyId);
        Task<bool> HasUserRespondedAsync(int surveyId, string userId);
        Task<(bool success, string message)> SubmitResponseAsync(
            SurveySubmitDto dto, string userId, string? ipAddress);
        
    }
}