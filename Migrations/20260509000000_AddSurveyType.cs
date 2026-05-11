using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnketOtomasyonu.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Survey tablosuna SurveyType (int, not null, default 0 = Normal)
            migrationBuilder.AddColumn<int>(
                name: "SurveyType",
                table: "Surveys",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SurveyType",
                table: "Surveys");
        }
    }
}
