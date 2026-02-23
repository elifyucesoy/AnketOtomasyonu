using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AnketOtomasyonu.Services.Implementations
{
    public class SurveyResponseService : ISurveyResponseService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SurveyResponseService> _logger;

        public SurveyResponseService(
            ApplicationDbContext context,
            ILogger<SurveyResponseService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> HasUserRespondedAsync(int surveyId, string userId)
        {
            return await _context.SurveyResponses
                .AnyAsync(r => r.SurveyId == surveyId && r.UserId == userId);
        }

        public async Task<bool> HasRespondedByIpAsync(int surveyId, string ipAddress)
        {
            return await _context.SurveyResponses
                .AnyAsync(r => r.SurveyId == surveyId && r.IpAddress == ipAddress);
        }

        public async Task<(bool success, string message)> SubmitResponseAsync(
            SurveySubmitDto dto, string userId, string? ipAddress)
        {
            var survey = await _context.Surveys
                .Include(s => s.Questions)
                .FirstOrDefaultAsync(s => s.Id == dto.SurveyId);

            if (survey == null)
                return (false, "Anket bulunamadı.");

            if (survey.Status != SurveyStatus.Active)
                return (false, "Bu anket aktif değil.");

            if (survey.IsAnonymous)
            {
                // Anonim ankette IP ile tekrar kontrolü
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    var alreadyByIp = await _context.SurveyResponses
                        .AnyAsync(r => r.SurveyId == dto.SurveyId
                                    && r.IpAddress == ipAddress);
                    if (alreadyByIp)
                        return (false, "Bu anketi zaten doldurdunuz.");
                }
            }
            else
            {
                // Normal ankette UserId ile tekrar kontrolü
                var already = await _context.SurveyResponses
                    .AnyAsync(r => r.SurveyId == dto.SurveyId && r.UserId == userId);
                if (already)
                    return (false, "Bu anketi zaten doldurdunuz.");
            }

            // Sadece zorunlu soruları kontrol et
            var requiredIds = survey.Questions
                .Where(q => q.IsRequired)
                .Select(q => q.Id).ToList();

            var answeredIds = dto.Answers
                .Where(a => a.SelectedOptionId.HasValue
                    || !string.IsNullOrWhiteSpace(a.OpenEndedAnswer))
                .Select(a => a.QuestionId).ToList();

            if (requiredIds.Except(answeredIds).Any())
                return (false, "Lütfen zorunlu (*) soruları cevaplayınız.");

            var response = new SurveyResponse
            {
                SurveyId = dto.SurveyId,
                UserId = survey.IsAnonymous ? Guid.NewGuid().ToString() : userId,
                SubmittedAt = DateTime.UtcNow,
                IpAddress = ipAddress,
                Answers = dto.Answers.Select(a => new SurveyAnswer
                {
                    QuestionId = a.QuestionId,
                    SelectedOptionId = a.SelectedOptionId,
                    OpenEndedAnswer = a.OpenEndedAnswer
                }).ToList()
            };

            _context.SurveyResponses.Add(response);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Cevap kaydedildi SurveyId={S} Anonim={A}",
                dto.SurveyId, survey.IsAnonymous);

            return (true, "Anket başarıyla gönderildi. Teşekkür ederiz!");
        }

        public async Task<SurveyResultDto> GetSurveyResultsAsync(int surveyId)
        {
            var survey = await _context.Surveys
                .Include(s => s.Questions).ThenInclude(q => q.Options)
                .Include(s => s.Responses).ThenInclude(r => r.Answers)
                .FirstOrDefaultAsync(s => s.Id == surveyId);

            if (survey == null) return new SurveyResultDto();

            return new SurveyResultDto
            {
                SurveyId = survey.Id,
                Title = survey.Title,
                TotalResponses = survey.Responses.Count,
                Questions = survey.Questions.Select(q =>
                {
                    var answers = survey.Responses
                        .SelectMany(r => r.Answers)
                        .Where(a => a.QuestionId == q.Id).ToList();

                    return new QuestionResultDto
                    {
                        QuestionId = q.Id,
                        QuestionText = q.Text,
                        QuestionType = q.Type,
                        AnswerCount = answers.Count,
                        OptionResults = q.Options.Select(o => new OptionResultDto
                        {
                            OptionId = o.Id,
                            OptionText = o.Text,
                            Count = answers.Count(a => a.SelectedOptionId == o.Id),
                            Percentage = answers.Count == 0 ? 0 :
                                Math.Round(
                                    (double)answers.Count(a => a.SelectedOptionId == o.Id)
                                    / answers.Count * 100, 1)
                        }).ToList(),
                        OpenEndedAnswers = q.Type == QuestionType.OpenEnded
                            ? answers
                                .Where(a => !string.IsNullOrWhiteSpace(a.OpenEndedAnswer))
                                .Select(a => a.OpenEndedAnswer!).ToList()
                            : new List<string>()
                    };
                }).ToList()
            };
        }
    }
}