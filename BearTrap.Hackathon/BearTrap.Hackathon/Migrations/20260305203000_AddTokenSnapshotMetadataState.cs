using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BearTrap.Hackathon.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenSnapshotMetadataState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "TokenSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageChangeCount24h",
                table: "TokenSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImageChangeWindowStartedAt",
                table: "TokenSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastObservedAt",
                table: "TokenSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotCount",
                table: "TokenSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TelegramUrl",
                table: "TokenSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwitterUrl",
                table: "TokenSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebUrl",
                table: "TokenSnapshots",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "TokenSnapshots");

            migrationBuilder.DropColumn(
                name: "ImageChangeCount24h",
                table: "TokenSnapshots");

            migrationBuilder.DropColumn(
                name: "ImageChangeWindowStartedAt",
                table: "TokenSnapshots");

            migrationBuilder.DropColumn(
                name: "LastObservedAt",
                table: "TokenSnapshots");

            migrationBuilder.DropColumn(
                name: "SnapshotCount",
                table: "TokenSnapshots");

            migrationBuilder.DropColumn(
                name: "TelegramUrl",
                table: "TokenSnapshots");

            migrationBuilder.DropColumn(
                name: "TwitterUrl",
                table: "TokenSnapshots");

            migrationBuilder.DropColumn(
                name: "WebUrl",
                table: "TokenSnapshots");
        }
    }
}
