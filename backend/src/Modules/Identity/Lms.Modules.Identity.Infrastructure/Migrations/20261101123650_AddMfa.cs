using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MfaEnabledAt",
                schema: "identity",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MfaLastStep",
                schema: "identity",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "MfaRecoveryHashes",
                schema: "identity",
                table: "Users",
                type: "varchar(1000)",
                unicode: false,
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecretCipher",
                schema: "identity",
                table: "Users",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MfaEnabledAt",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaLastStep",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaRecoveryHashes",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaSecretCipher",
                schema: "identity",
                table: "Users");
        }
    }
}
