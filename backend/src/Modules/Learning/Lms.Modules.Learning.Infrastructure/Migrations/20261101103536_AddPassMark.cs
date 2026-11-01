using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Learning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPassMark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FinalGradePercent",
                schema: "learning",
                table: "CourseProgress",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PassMarkPercent",
                schema: "learning",
                table: "CourseContents",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalGradePercent",
                schema: "learning",
                table: "CourseProgress");

            migrationBuilder.DropColumn(
                name: "PassMarkPercent",
                schema: "learning",
                table: "CourseContents");
        }
    }
}
