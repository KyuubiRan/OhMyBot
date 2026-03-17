using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyLib.Migrations
{
    /// <inheritdoc />
    public partial class V4_AddCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Coin",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "user_checkins",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TotalDays = table.Column<int>(type: "integer", nullable: false),
                    StreakDays = table.Column<int>(type: "integer", nullable: false),
                    MaxStreakDays = table.Column<int>(type: "integer", nullable: false),
                    LastCheckinTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_checkins", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_user_checkins_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_checkins");

            migrationBuilder.DropColumn(
                name: "Coin",
                table: "users");
        }
    }
}
