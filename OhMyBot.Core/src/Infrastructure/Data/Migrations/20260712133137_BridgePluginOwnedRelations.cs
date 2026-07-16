using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OhMyBot.Core.src.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BridgePluginOwnedRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiRouterAccounts_CoreUsers_CoreUserId",
                table: "AiRouterAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_HappytukAccounts_CoreUsers_CoreUserId",
                table: "HappytukAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_KuroAccounts_CoreUsers_CoreUserId",
                table: "KuroAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_MihoyoAccounts_CoreUsers_CoreUserId",
                table: "MihoyoAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_SklandAccounts_CoreUsers_CoreUserId",
                table: "SklandAccounts");

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

            migrationBuilder.InsertData(
                table: "PluginOwnedRelations",
                columns: new[] { "CoreUserIdColumn", "SchemaName", "TableName", "PluginId" },
                values: new object[,]
                {
                    { "CoreUserId", "public", "AiRouterAccounts", "com.ohmybot.airouter" },
                    { "CoreUserId", "public", "HappytukAccounts", "com.ohmybot.happytuk" },
                    { "CoreUserId", "public", "KuroAccounts", "com.ohmybot.kuro" },
                    { "CoreUserId", "public", "MihoyoAccounts", "com.ohmybot.mihoyo" },
                    { "CoreUserId", "public", "SklandAccounts", "com.ohmybot.skland" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PluginOwnedRelations_PluginId",
                table: "PluginOwnedRelations",
                column: "PluginId");

            migrationBuilder.AddForeignKey(
                name: "FK_AiRouterAccounts_CoreUsers_CoreUserId",
                table: "AiRouterAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HappytukAccounts_CoreUsers_CoreUserId",
                table: "HappytukAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KuroAccounts_CoreUsers_CoreUserId",
                table: "KuroAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MihoyoAccounts_CoreUsers_CoreUserId",
                table: "MihoyoAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SklandAccounts_CoreUsers_CoreUserId",
                table: "SklandAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiRouterAccounts_CoreUsers_CoreUserId",
                table: "AiRouterAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_HappytukAccounts_CoreUsers_CoreUserId",
                table: "HappytukAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_KuroAccounts_CoreUsers_CoreUserId",
                table: "KuroAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_MihoyoAccounts_CoreUsers_CoreUserId",
                table: "MihoyoAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_SklandAccounts_CoreUsers_CoreUserId",
                table: "SklandAccounts");

            migrationBuilder.DropTable(
                name: "PluginOwnedRelations");

            migrationBuilder.AddForeignKey(
                name: "FK_AiRouterAccounts_CoreUsers_CoreUserId",
                table: "AiRouterAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HappytukAccounts_CoreUsers_CoreUserId",
                table: "HappytukAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_KuroAccounts_CoreUsers_CoreUserId",
                table: "KuroAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MihoyoAccounts_CoreUsers_CoreUserId",
                table: "MihoyoAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SklandAccounts_CoreUsers_CoreUserId",
                table: "SklandAccounts",
                column: "CoreUserId",
                principalTable: "CoreUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
