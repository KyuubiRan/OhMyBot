using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyLib.Migrations
{
    /// <inheritdoc />
    public partial class V5_AddCheckinDailyClaimed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyClaimed",
                table: "user_checkins",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyClaimed",
                table: "user_checkins");
        }
    }
}
