using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Certification.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Competencies",
                schema: "certification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompetencySignOffs",
                schema: "certification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraineeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetencySignOffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearningPlans",
                schema: "certification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanAssignments",
                schema: "certification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraineeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AssignedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompetencyCourses",
                schema: "certification",
                columns: table => new
                {
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetencyCourses", x => new { x.CompetencyId, x.CourseId });
                    table.ForeignKey(
                        name: "FK_CompetencyCourses_Competencies_CompetencyId",
                        column: x => x.CompetencyId,
                        principalSchema: "certification",
                        principalTable: "Competencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearningPlanItems",
                schema: "certification",
                columns: table => new
                {
                    CompetencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningPlanItems", x => new { x.PlanId, x.CompetencyId });
                    table.ForeignKey(
                        name: "FK_LearningPlanItems_LearningPlans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "certification",
                        principalTable: "LearningPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Competencies_Code",
                schema: "certification",
                table: "Competencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetencySignOffs_CompetencyId_TraineeId",
                schema: "certification",
                table: "CompetencySignOffs",
                columns: new[] { "CompetencyId", "TraineeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetencySignOffs_TraineeId",
                schema: "certification",
                table: "CompetencySignOffs",
                column: "TraineeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssignments_PlanId_TraineeId",
                schema: "certification",
                table: "PlanAssignments",
                columns: new[] { "PlanId", "TraineeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssignments_TraineeId",
                schema: "certification",
                table: "PlanAssignments",
                column: "TraineeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetencyCourses",
                schema: "certification");

            migrationBuilder.DropTable(
                name: "CompetencySignOffs",
                schema: "certification");

            migrationBuilder.DropTable(
                name: "LearningPlanItems",
                schema: "certification");

            migrationBuilder.DropTable(
                name: "PlanAssignments",
                schema: "certification");

            migrationBuilder.DropTable(
                name: "Competencies",
                schema: "certification");

            migrationBuilder.DropTable(
                name: "LearningPlans",
                schema: "certification");
        }
    }
}
