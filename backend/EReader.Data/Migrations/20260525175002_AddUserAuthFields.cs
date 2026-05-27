using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAuthFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Any rows that existed before auth was added have an empty PasswordHash
            // (the column's NOT NULL default). Deactivate them so they fail closed at
            // the IsActive check instead of looking like a valid login with the wrong
            // password. Real users created by AuthService.RegisterAsync always set a
            // hash, so this only targets the pre-auth seed rows.
            migrationBuilder.Sql(
                """UPDATE "Users" SET "IsActive" = false WHERE "PasswordHash" = '';""");

            // Functional unique index on LOWER(Username) so 'Alice' and 'alice'
            // can't both exist. Hand-written here (instead of CreateIndex) since
            // EF Core can't express functional indexes declaratively.
            //
            // IMPORTANT: any future migration that alters the Users.Username column
            // will cause EF to emit DropIndex/CreateIndex for a *non-functional*
            // index, silently breaking case-insensitive uniqueness. If you touch
            // Username, hand-edit the generated migration to re-create this index
            // in functional form. See best-practices.md → "Functional indexes".
            migrationBuilder.Sql(
                """CREATE UNIQUE INDEX "IX_Users_Username_Lower" ON "Users" (LOWER("Username"));""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Users_Username_Lower";""");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }
    }
}
