using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnketOtomasyonu.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyBirimTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminPermissions_Username_PersonelBirim",
                table: "AdminPermissions");

            migrationBuilder.CreateTable(
                name: "SurveyBirimler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyId = table.Column<int>(type: "int", nullable: false),
                    Birim = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyBirimler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyBirimler_Surveys_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "Surveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminPermissions_Username_PersonelBirim",
                table: "AdminPermissions",
                columns: new[] { "Username", "PersonelBirim" });

            migrationBuilder.CreateIndex(
                name: "IX_SurveyBirimler_SurveyId_Birim",
                table: "SurveyBirimler",
                columns: new[] { "SurveyId", "Birim" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SurveyBirimler");

            migrationBuilder.DropIndex(
                name: "IX_AdminPermissions_Username_PersonelBirim",
                table: "AdminPermissions");

            migrationBuilder.CreateIndex(
                name: "IX_AdminPermissions_Username_PersonelBirim",
                table: "AdminPermissions",
                columns: new[] { "Username", "PersonelBirim" },
                unique: true);
        }
    }
}
