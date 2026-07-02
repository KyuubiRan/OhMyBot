using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OhMyBot.Core.src.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMihoyoSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MihoyoAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    Region = table.Column<int>(type: "integer", nullable: false),
                    Stuid = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CookieCiphertext = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    StokenCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Mid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AutoSignEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BbsTaskFlags = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MihoyoAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MihoyoAccounts_CoreUsers_CoreUserId",
                        column: x => x.CoreUserId,
                        principalTable: "CoreUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MihoyoGameRoles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MihoyoAccountId = table.Column<long>(type: "bigint", nullable: false),
                    GameBiz = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GameName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Region = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GameUid = table.Column<long>(type: "bigint", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Level = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AutoSignEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MihoyoGameRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MihoyoGameRoles_MihoyoAccounts_MihoyoAccountId",
                        column: x => x.MihoyoAccountId,
                        principalTable: "MihoyoAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MihoyoAccounts_CoreUserId",
                table: "MihoyoAccounts",
                column: "CoreUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MihoyoAccounts_Region_Stuid",
                table: "MihoyoAccounts",
                columns: new[] { "Region", "Stuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MihoyoGameRoles_MihoyoAccountId_GameBiz_GameUid",
                table: "MihoyoGameRoles",
                columns: new[] { "MihoyoAccountId", "GameBiz", "GameUid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MihoyoGameRoles");

            migrationBuilder.DropTable(
                name: "MihoyoAccounts");
        }
    }
}
