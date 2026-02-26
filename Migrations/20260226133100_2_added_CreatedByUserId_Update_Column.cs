using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnketOtomasyonu.Migrations
{
    /// <inheritdoc />
    public partial class _2_added_CreatedByUserId_Update_Column : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "Surveys",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "CreatedByUserId",
                table: "Surveys",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
