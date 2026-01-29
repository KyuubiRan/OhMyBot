using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyLib.Migrations
{
    /// <inheritdoc />
    public partial class V3_AddMissingFieldsForKuroUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "kuro_users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.AddColumn<long>(
                name: "BbsUserId",
                table: "kuro_users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DevCode",
                table: "kuro_users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistinctId",
                table: "kuro_users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "kuro_users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_kuro_users_BbsUserId",
                table: "kuro_users",
                column: "BbsUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_kuro_users_BbsUserId",
                table: "kuro_users");

            migrationBuilder.DropColumn(
                name: "BbsUserId",
                table: "kuro_users");

            migrationBuilder.DropColumn(
                name: "DevCode",
                table: "kuro_users");

            migrationBuilder.DropColumn(
                name: "DistinctId",
                table: "kuro_users");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "kuro_users");

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "kuro_users",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }
    }
}
