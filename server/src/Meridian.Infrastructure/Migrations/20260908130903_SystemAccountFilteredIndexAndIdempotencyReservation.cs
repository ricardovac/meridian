using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SystemAccountFilteredIndexAndIdempotencyReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_IsSystem_Currency",
                table: "Accounts");

            migrationBuilder.AlterColumn<int>(
                name: "ResponseStatusCode",
                table: "IdempotencyRecords",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "ResponseBody",
                table: "IdempotencyRecords",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "IdempotencyRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Currency_IsSystem",
                table: "Accounts",
                column: "Currency",
                unique: true,
                filter: "\"IsSystem\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_Currency_IsSystem",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "IdempotencyRecords");

            migrationBuilder.AlterColumn<int>(
                name: "ResponseStatusCode",
                table: "IdempotencyRecords",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ResponseBody",
                table: "IdempotencyRecords",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_IsSystem_Currency",
                table: "Accounts",
                columns: new[] { "IsSystem", "Currency" });
        }
    }
}
