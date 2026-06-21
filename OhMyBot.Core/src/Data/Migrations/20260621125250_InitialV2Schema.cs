using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OhMyBot.Core.src.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialV2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoreUsers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Privilege = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoreUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiRouterAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    LoginEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AutoSignEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiRouterAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiRouterAccounts_CoreUsers_CoreUserId",
                        column: x => x.CoreUserId,
                        principalTable: "CoreUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KuroAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    BbsUserId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TokenCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    DevCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DistinctId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AutoSignEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BbsTaskFlags = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KuroAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KuroAccounts_CoreUsers_CoreUserId",
                        column: x => x.CoreUserId,
                        principalTable: "CoreUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    NotificationType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    EnabledPlatforms = table.Column<int>(type: "integer", nullable: false),
                    TelegramBotInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TelegramChatId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    QqBotInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    QqChatId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationSubscriptions_CoreUsers_CoreUserId",
                        column: x => x.CoreUserId,
                        principalTable: "CoreUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformUserProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: true),
                    Username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FirstName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Nickname = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUserProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformUserProfiles_CoreUsers_CoreUserId",
                        column: x => x.CoreUserId,
                        principalTable: "CoreUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "KuroGameRoles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KuroAccountId = table.Column<long>(type: "bigint", nullable: false),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    GameName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ServerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ServerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    RoleName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    GameLevel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AutoSignEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KuroGameRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KuroGameRoles_KuroAccounts_KuroAccountId",
                        column: x => x.KuroAccountId,
                        principalTable: "KuroAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiRouterAccounts_CoreUserId",
                table: "AiRouterAccounts",
                column: "CoreUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiRouterAccounts_LoginEmail",
                table: "AiRouterAccounts",
                column: "LoginEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KuroAccounts_BbsUserId",
                table: "KuroAccounts",
                column: "BbsUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KuroAccounts_CoreUserId",
                table: "KuroAccounts",
                column: "CoreUserId");

            migrationBuilder.CreateIndex(
                name: "IX_KuroGameRoles_KuroAccountId_GameId_ServerId_RoleId",
                table: "KuroGameRoles",
                columns: new[] { "KuroAccountId", "GameId", "ServerId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSubscriptions_CoreUserId_NotificationType_Targe~",
                table: "NotificationSubscriptions",
                columns: new[] { "CoreUserId", "NotificationType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserProfiles_CoreUserId",
                table: "PlatformUserProfiles",
                column: "CoreUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserProfiles_Platform_Uid",
                table: "PlatformUserProfiles",
                columns: new[] { "Platform", "Uid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiRouterAccounts");

            migrationBuilder.DropTable(
                name: "KuroGameRoles");

            migrationBuilder.DropTable(
                name: "NotificationSubscriptions");

            migrationBuilder.DropTable(
                name: "PlatformUserProfiles");

            migrationBuilder.DropTable(
                name: "KuroAccounts");

            migrationBuilder.DropTable(
                name: "CoreUsers");
        }
    }
}
