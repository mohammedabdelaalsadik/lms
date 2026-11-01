using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Learning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequireContentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Removing content used to unlink rows instead of deleting them. Nothing can reach these rows.
            migrationBuilder.Sql("DELETE FROM [learning].[Activities] WHERE [SectionId] IS NULL;");
            migrationBuilder.Sql("DELETE FROM [learning].[Sections] WHERE [CourseContentId] IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "CourseContentId",
                schema: "learning",
                table: "Sections",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SectionId",
                schema: "learning",
                table: "Activities",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CourseContentId",
                schema: "learning",
                table: "Sections",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "SectionId",
                schema: "learning",
                table: "Activities",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");
        }
    }
}
