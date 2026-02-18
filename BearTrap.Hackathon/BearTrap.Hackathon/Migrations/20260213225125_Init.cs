using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BearTrap.Hackathon.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiskReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TokenAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    FlagsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AiSummary = table.Column<string>(type: "TEXT", nullable: false),
                    AnalyzedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TokenSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Creator = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiskReports_TokenAddress",
                table: "RiskReports",
                column: "TokenAddress");

            migrationBuilder.CreateIndex(
                name: "IX_TokenSnapshots_Address",
                table: "TokenSnapshots",
                column: "Address",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiskReports");

            migrationBuilder.DropTable(
                name: "TokenSnapshots");
        }
    }
}
