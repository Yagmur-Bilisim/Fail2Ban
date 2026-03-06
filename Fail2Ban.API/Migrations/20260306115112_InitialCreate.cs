using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fail2Ban.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BanRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BannedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAbuseReported = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirewallRuleName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BanRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FailedAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogPointers",
                columns: table => new
                {
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    LastReadPosition = table.Column<long>(type: "INTEGER", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogPointers", x => x.FilePath);
                });

            migrationBuilder.CreateTable(
                name: "WhitelistedIps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhitelistedIps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BanRecords_IpAddress",
                table: "BanRecords",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_BanRecords_IsActive",
                table: "BanRecords",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_FailedAttempts_IpAddress_Source",
                table: "FailedAttempts",
                columns: new[] { "IpAddress", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhitelistedIps_IpAddress",
                table: "WhitelistedIps",
                column: "IpAddress",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BanRecords");

            migrationBuilder.DropTable(
                name: "FailedAttempts");

            migrationBuilder.DropTable(
                name: "LogPointers");

            migrationBuilder.DropTable(
                name: "WhitelistedIps");
        }
    }
}
