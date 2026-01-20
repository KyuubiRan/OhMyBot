using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OhMyLib.Migrations
{
    /// <inheritdoc />
    public partial class V1_Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerType = table.Column<byte>(type: "smallint", nullable: false),
                    Privilege = table.Column<byte>(type: "smallint", nullable: false),
                    CreateAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kuro_users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: false),
                    Region = table.Column<byte>(type: "smallint", nullable: false),
                    BbsTask = table.Column<long>(type: "bigint", nullable: false),
                    Token = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreateAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kuro_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kuro_users_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kuro_game_configs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KuroUserId = table.Column<long>(type: "bigint", nullable: false),
                    GameType = table.Column<byte>(type: "smallint", nullable: false),
                    GameCharacterUid = table.Column<long>(type: "bigint", nullable: false),
                    TaskType = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kuro_game_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kuro_game_configs_kuro_users_KuroUserId",
                        column: x => x.KuroUserId,
                        principalTable: "kuro_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kuro_game_configs_KuroUserId",
                table: "kuro_game_configs",
                column: "KuroUserId");

            migrationBuilder.CreateIndex(
                name: "IX_kuro_users_OwnerUserId",
                table: "kuro_users",
                column: "OwnerUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kuro_game_configs");

            migrationBuilder.DropTable(
                name: "kuro_users");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
