using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Assessment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEssays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AwaitingMarking",
                schema: "assessment",
                table: "Attempts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "Response",
                schema: "assessment",
                table: "AttemptAnswers",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<decimal>(
                name: "AwardedPoints",
                schema: "assessment",
                table: "AttemptAnswers",
                type: "decimal(7,2)",
                precision: 7,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwaitingMarking",
                schema: "assessment",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "AwardedPoints",
                schema: "assessment",
                table: "AttemptAnswers");

            migrationBuilder.AlterColumn<string>(
                name: "Response",
                schema: "assessment",
                table: "AttemptAnswers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 8000);
        }
    }
}
