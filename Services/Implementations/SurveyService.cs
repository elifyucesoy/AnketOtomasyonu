using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AnketOtomasyonu.Services.Implementations
{
    public class SurveyService : ISurveyService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SurveyService> _logger;

        public SurveyService(ApplicationDbContext context, ILogger<SurveyService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Survey?> GetSurveyWithQuestionsAsync(int surveyId)
        {
            return await _context.Surveys
                .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                    .ThenInclude(q => q.Options.OrderBy(o => o.OrderIndex))
                .Include(s => s.Responses)
                .FirstOrDefaultAsync(s => s.Id == surveyId);
        }

        public async Task<IEnumerable<Survey>> GetAllSurveysAsync()
        {
            return await _context.Surveys
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetActiveSurveysAsync()
        {
            var now = DateTime.UtcNow;
            return await _context.Surveys
                .Where(s => s.Status == SurveyStatus.Active
                    && (s.StartDate == null || s.StartDate <= now)
                    && (s.EndDate == null || s.EndDate >= now))
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetActiveAnonymousSurveysAsync()
        {
            var now = DateTime.UtcNow;
            return await _context.Surveys
                .Where(s => s.Status == SurveyStatus.Active
                    && s.IsAnonymous
                    && (s.StartDate == null || s.StartDate <= now)
                    && (s.EndDate == null || s.EndDate >= now))
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetSurveysByCreatorAsync(string creatorUserId)
        {
            return await _context.Surveys
                .Where(s => s.CreatedByUserId == creatorUserId)
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<Survey> CreateSurveyAsync(
            SurveyCreateDto dto, string creatorUserId, string creatorName)
        {
            var survey = new Survey
            {
                Title = dto.Title,
                Description = dto.Description,
                IsAnonymous = dto.IsAnonymous,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                TargetRoles = string.Join(",", dto.TargetRoles),
                CreatedByUserId = creatorUserId,
                CreatedByName = creatorName,
                Status = SurveyStatus.Draft,
                CreatedAt = DateTime.UtcNow
            };

            int order = 1;
            foreach (var qDto in dto.Questions)
            {
                var question = new Question
                {
                    Text = qDto.Text,
                    Type = qDto.Type,
                    IsRequired = qDto.IsRequired,
                    OrderIndex = order++
                };

                if (qDto.Type == QuestionType.Likert)
                {
                    question.Options = new List<QuestionOption>
                    {
                        new() { Text = "Çok Kötü",  Value = 1, OrderIndex = 1 },
                        new() { Text = "Kötü",       Value = 2, OrderIndex = 2 },
                        new() { Text = "Kararsızım", Value = 3, OrderIndex = 3 },
                        new() { Text = "İyi",        Value = 4, OrderIndex = 4 },
                        new() { Text = "Çok İyi",    Value = 5, OrderIndex = 5 },
                    };
                }
                else if (qDto.Type == QuestionType.MultipleChoice)
                {
                    var labels = new[] { "A", "B", "C", "D" };
                    question.Options = qDto.Options.Take(4)
                        .Select((o, i) => new QuestionOption
                        {
                            Text = $"{labels[i]}) {o.Text}",
                            OrderIndex = i + 1
                        }).ToList();
                }

                survey.Questions.Add(question);
            }

            _context.Surveys.Add(survey);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Anket oluşturuldu Id={Id}", survey.Id);
            return survey;
        }

        public async Task PublishSurveyAsync(int surveyId)
        {
            var s = await _context.Surveys.FindAsync(surveyId);
            if (s == null) return;
            s.Status = SurveyStatus.Active;
            s.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task CloseSurveyAsync(int surveyId)
        {
            var s = await _context.Surveys.FindAsync(surveyId);
            if (s == null) return;
            s.Status = SurveyStatus.Closed;
            s.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSurveyAsync(int surveyId)
        {
            var s = await _context.Surveys.FindAsync(surveyId);
            if (s == null) return;
            _context.Surveys.Remove(s);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateSurveyAsync(int surveyId, SurveyCreateDto dto)
        {
            var survey = await _context.Surveys
                .Include(s => s.Questions)
                    .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(s => s.Id == surveyId);

            if (survey == null) return;

            // Temel bilgileri güncelle
            survey.Title = dto.Title;
            survey.Description = dto.Description;
            survey.IsAnonymous = dto.IsAnonymous;
            survey.StartDate = dto.StartDate;
            survey.EndDate = dto.EndDate;
            survey.TargetRoles = string.Join(",", dto.TargetRoles);
            survey.UpdatedAt = DateTime.UtcNow;

            // Eski soruları ve seçenekleri sil
            foreach (var q in survey.Questions.ToList())
            {
                _context.RemoveRange(q.Options);
                _context.Remove(q);
            }

            // Yeni soruları ekle
            int order = 1;
            foreach (var qDto in dto.Questions)
            {
                var question = new Question
                {
                    Text = qDto.Text,
                    Type = qDto.Type,
                    IsRequired = qDto.IsRequired,
                    OrderIndex = order++
                };

                if (qDto.Type == QuestionType.Likert)
                {
                    question.Options = new List<QuestionOption>
                    {
                        new() { Text = "Çok Kötü",  Value = 1, OrderIndex = 1 },
                        new() { Text = "Kötü",       Value = 2, OrderIndex = 2 },
                        new() { Text = "Kararsızım", Value = 3, OrderIndex = 3 },
                        new() { Text = "İyi",        Value = 4, OrderIndex = 4 },
                        new() { Text = "Çok İyi",    Value = 5, OrderIndex = 5 },
                    };
                }
                else if (qDto.Type == QuestionType.MultipleChoice)
                {
                    var labels = new[] { "A", "B", "C", "D" };
                    question.Options = qDto.Options.Take(4)
                        .Select((o, i) => new QuestionOption
                        {
                            Text = $"{labels[i]}) {o.Text}",
                            OrderIndex = i + 1
                        }).ToList();
                }

                survey.Questions.Add(question);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Anket güncellendi Id={Id}", surveyId);
        }
    }
}