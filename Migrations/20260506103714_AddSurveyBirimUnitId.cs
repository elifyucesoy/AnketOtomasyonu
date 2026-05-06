using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnketOtomasyonu.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyBirimUnitId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SurveyBirimler_SurveyId_Birim",
                table: "SurveyBirimler");

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "SurveyBirimler",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SurveyBirimler_SurveyId",
                table: "SurveyBirimler",
                column: "SurveyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SurveyBirimler_SurveyId",
                table: "SurveyBirimler");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "SurveyBirimler");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyBirimler_SurveyId_Birim",
                table: "SurveyBirimler",
                columns: new[] { "SurveyId", "Birim" },
                unique: true);
        }
    }
}
