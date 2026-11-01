using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResetExpiresAt",
                schema: "identity",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResetTokenHash",
                schema: "identity",
                table: "Users",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ResetTokenHash",
                schema: "identity",
                table: "Users",
                column: "ResetTokenHash",
                filter: "[ResetTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_ResetTokenHash",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ResetExpiresAt",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ResetTokenHash",
                schema: "identity",
                table: "Users");
        }
    }
}
