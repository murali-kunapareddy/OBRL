using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Obrl.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class BookSubscriptionLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            // Book subscriptions that Users and Contributors already hold get the default 30-day period from today,
            // so nobody loses a book the moment this release goes live. Their own uploads need no subscription.
            migrationBuilder.Sql("""
                UPDATE "Subscriptions" s SET "ExpiresAt" = now() + interval '30 days'
                WHERE s."BookId" IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM "AspNetUserRoles" ur JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                    WHERE ur."UserId" = s."UserId" AND r."Name" IN ('Admin', 'Moderator'))
                  AND NOT EXISTS (SELECT 1 FROM "Books" b WHERE b."Id" = s."BookId" AND b."UploadedById" = s."UserId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Subscriptions");
        }
    }
}
