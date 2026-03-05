using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BearTrap.Hackathon.Migrations
{
    /// <inheritdoc />
    public partial class AddImageKeyToTokenSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageKey",
                table: "TokenSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenSnapshots_ImageKey",
                table: "TokenSnapshots",
                column: "ImageKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TokenSnapshots_ImageKey",
                table: "TokenSnapshots");

            migrationBuilder.DropColumn(
                name: "ImageKey",
                table: "TokenSnapshots");
        }
    }
}
