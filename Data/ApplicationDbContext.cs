using Microsoft.EntityFrameworkCore;
using AnketOtomasyonu.Models.Entities;

namespace AnketOtomasyonu.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Survey> Surveys { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<QuestionOption> QuestionOptions { get; set; }
        public DbSet<SurveyResponse> SurveyResponses { get; set; }
        public DbSet<SurveyAnswer> SurveyAnswers { get; set; }
        public DbSet<AdminPermission> AdminPermissions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AdminPermission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
                entity.Property(e => e.PersonelBirim).HasMaxLength(300).IsRequired();
                entity.Property(e => e.Note).HasMaxLength(500);
                entity.HasIndex(e => new { e.Username, e.PersonelBirim }).IsUnique();
            });

            modelBuilder.Entity<Survey>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.TargetRoles).HasMaxLength(500);
                entity.Property(e => e.CreatedByName).HasMaxLength(200);
                entity.Property(e => e.CreatedByBirim).HasMaxLength(300);

                entity.HasMany(s => s.Questions)
                      .WithOne(q => q.Survey)
                      .HasForeignKey(q => q.SurveyId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(s => s.Responses)
                      .WithOne(r => r.Survey)
                      .HasForeignKey(r => r.SurveyId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Question>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Text).HasMaxLength(500).IsRequired();

                entity.HasMany(q => q.Options)
                      .WithOne(o => o.Question)
                      .HasForeignKey(o => o.QuestionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<QuestionOption>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Text).HasMaxLength(300).IsRequired();
            });

            modelBuilder.Entity<SurveyResponse>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.SurveyId, e.UserId }).IsUnique();
                entity.Property(e => e.UserId).HasMaxLength(50).IsRequired();
                entity.Property(e => e.IpAddress).HasMaxLength(45);
                entity.Property(e => e.UserFullName).HasMaxLength(200);
                entity.Property(e => e.FakulteAdi).HasMaxLength(300);
                entity.Property(e => e.BolumAdi).HasMaxLength(300);

                entity.HasMany(r => r.Answers)
                      .WithOne(a => a.SurveyResponse)
                      .HasForeignKey(a => a.SurveyResponseId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SurveyAnswer>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OpenEndedAnswer).HasMaxLength(2000);

                entity.HasOne(a => a.Question)
                      .WithMany()
                      .HasForeignKey(a => a.QuestionId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(a => a.SelectedOption)
                      .WithMany()
                      .HasForeignKey(a => a.SelectedOptionId)
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}