using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnketOtomasyonu.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyAnswerQuestionType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DECLARE @schema sysname;
                SELECT TOP 1 @schema = s.name
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.name = N'SurveyAnswers'
                ORDER BY CASE WHEN s.name = N'dbo' THEN 0 ELSE 1 END;
                IF @schema IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM sys.columns c
                    INNER JOIN sys.tables t ON c.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE t.name = N'SurveyAnswers' AND s.name = @schema AND c.name = N'QuestionType')
                BEGIN
                    DECLARE @add nvarchar(max) = N'ALTER TABLE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(N'SurveyAnswers') + N' ADD [QuestionType] INT NULL';
                    EXEC sp_executesql @add;
                END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DECLARE @schema sysname;
                SELECT TOP 1 @schema = s.name
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.name = N'SurveyAnswers'
                ORDER BY CASE WHEN s.name = N'dbo' THEN 0 ELSE 1 END;
                IF @schema IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM sys.columns c
                    INNER JOIN sys.tables t ON c.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE t.name = N'SurveyAnswers' AND s.name = @schema AND c.name = N'QuestionType')
                BEGIN
                    DECLARE @drop nvarchar(max) = N'ALTER TABLE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(N'SurveyAnswers') + N' DROP COLUMN [QuestionType]';
                    EXEC sp_executesql @drop;
                END");
        }
    }
}
