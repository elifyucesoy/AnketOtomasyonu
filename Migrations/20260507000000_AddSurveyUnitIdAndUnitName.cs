using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnketOtomasyonu.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyUnitIdAndUnitName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Survey tablosuna UnitId (int, nullable) — UnitList API'den gelen birim Id'si
            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "Surveys",
                type: "int",
                nullable: true);

            // Survey tablosuna UnitName (nvarchar(300), nullable) — birim adı metin olarak
            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "Surveys",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "Surveys");
        }
    }
}
