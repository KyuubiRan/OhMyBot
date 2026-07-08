using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OhMyBot.Core.src.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSklandSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SklandAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    HgTokenCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CredCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SignTokenCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SklandUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AutoSignEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SklandAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SklandAccounts_CoreUsers_CoreUserId",
                        column: x => x.CoreUserId,
                        principalTable: "CoreUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SklandGameRoles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SklandAccountId = table.Column<long>(type: "bigint", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    AppCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GameName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NickName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Level = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ServerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RoleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AutoSignEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SklandGameRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SklandGameRoles_SklandAccounts_SklandAccountId",
                        column: x => x.SklandAccountId,
                        principalTable: "SklandAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SklandAccounts_CoreUserId",
                table: "SklandAccounts",
                column: "CoreUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SklandAccounts_SklandUserId",
                table: "SklandAccounts",
                column: "SklandUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SklandGameRoles_SklandAccountId_GameId_Uid_RoleId",
                table: "SklandGameRoles",
                columns: new[] { "SklandAccountId", "GameId", "Uid", "RoleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SklandGameRoles");

            migrationBuilder.DropTable(
                name: "SklandAccounts");
        }
    }
}
