using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyBot.Core.src.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSignSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GameSignSelection",
                table: "MihoyoAccounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GameSignSelection",
                table: "KuroAccounts",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GameSignSelection",
                table: "MihoyoAccounts");

            migrationBuilder.DropColumn(
                name: "GameSignSelection",
                table: "KuroAccounts");
        }
    }
}
