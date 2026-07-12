using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OhMyBot.Core.src.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHappytukCouponRedemption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HappytukAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoreUserId = table.Column<long>(type: "bigint", nullable: false),
                    LoginAccount = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HappytukAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HappytukAccounts_CoreUsers_CoreUserId",
                        column: x => x.CoreUserId,
                        principalTable: "CoreUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HappytukAccounts_CoreUserId",
                table: "HappytukAccounts",
                column: "CoreUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HappytukAccounts_LoginAccount",
                table: "HappytukAccounts",
                column: "LoginAccount",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HappytukAccounts");
        }
    }
}
