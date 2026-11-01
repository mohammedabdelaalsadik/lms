using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Assessment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<int>(
                name: "DrawCount",
                schema: "assessment",
                table: "Quizzes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Shuffle",
                schema: "assessment",
                table: "Quizzes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Tolerance",
                schema: "assessment",
                table: "Questions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchAr",
                schema: "assessment",
                table: "Options",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchEn",
                schema: "assessment",
                table: "Options",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionIds",
                schema: "assessment",
                table: "Attempts",
                type: "varchar(max)",
                unicode: false,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Seed",
                schema: "assessment",
                table: "Attempts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Response",
                schema: "assessment",
                table: "AttemptAnswers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BankQuestions",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Tolerance = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Options = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TextAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TextEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankQuestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankQuestions_CourseId_Category",
                schema: "assessment",
                table: "BankQuestions",
                columns: new[] { "CourseId", "Category" });

            // Saved answers keep their meaning: the chosen option's id becomes the response.
            migrationBuilder.Sql("UPDATE assessment.AttemptAnswers SET Response = LOWER(CONVERT(nvarchar(36), OptionId));");

            migrationBuilder.DropColumn(
                name: "OptionId",
                schema: "assessment",
                table: "AttemptAnswers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankQuestions",
                schema: "assessment");

            migrationBuilder.DropColumn(
                name: "DrawCount",
                schema: "assessment",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "Shuffle",
                schema: "assessment",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "Tolerance",
                schema: "assessment",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "MatchAr",
                schema: "assessment",
                table: "Options");

            migrationBuilder.DropColumn(
                name: "MatchEn",
                schema: "assessment",
                table: "Options");

            migrationBuilder.DropColumn(
                name: "QuestionIds",
                schema: "assessment",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "Seed",
                schema: "assessment",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "Response",
                schema: "assessment",
                table: "AttemptAnswers");

            migrationBuilder.AddColumn<Guid>(
                name: "OptionId",
                schema: "assessment",
                table: "AttemptAnswers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }
    }
}
