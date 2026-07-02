using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OhMyBot.Core.src.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreEnumsAsInts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "PlatformUserProfiles"
                ALTER COLUMN "Platform" TYPE integer
                USING CASE "Platform"
                    WHEN 'Telegram' THEN 1
                    WHEN 'Qq' THEN 2
                    ELSE 0
                END;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "CoreUsers"
                ALTER COLUMN "Privilege" TYPE integer
                USING CASE "Privilege"
                    WHEN 'User' THEN 0
                    WHEN 'VerifiedUser' THEN 1
                    WHEN 'Admin' THEN 10
                    WHEN 'Owner' THEN 100
                    ELSE 0
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "PlatformUserProfiles"
                ALTER COLUMN "Platform" TYPE character varying(64)
                USING CASE "Platform"
                    WHEN 1 THEN 'Telegram'
                    WHEN 2 THEN 'Qq'
                    ELSE 'Unspecified'
                END;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "CoreUsers"
                ALTER COLUMN "Privilege" TYPE character varying(64)
                USING CASE "Privilege"
                    WHEN 0 THEN 'User'
                    WHEN 1 THEN 'VerifiedUser'
                    WHEN 10 THEN 'Admin'
                    WHEN 100 THEN 'Owner'
                    ELSE 'User'
                END;
                """);
        }
    }
}
