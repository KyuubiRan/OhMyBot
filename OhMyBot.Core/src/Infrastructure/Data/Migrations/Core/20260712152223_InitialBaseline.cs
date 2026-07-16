using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OhMyBot.Core.src.Infrastructure.Data.Migrations.Core
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
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
                    Privilege = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoreUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PluginOwnedRelations",
                columns: table => new
                {
                    SchemaName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    TableName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    CoreUserIdColumn = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    PluginId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginOwnedRelations", x => new { x.SchemaName, x.TableName, x.CoreUserIdColumn });
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
                    Platform = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_PluginOwnedRelations_PluginId",
                table: "PluginOwnedRelations",
                column: "PluginId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationSubscriptions");

            migrationBuilder.DropTable(
                name: "PlatformUserProfiles");

            migrationBuilder.DropTable(
                name: "PluginOwnedRelations");

            migrationBuilder.DropTable(
                name: "CoreUsers");
        }
    }
}
