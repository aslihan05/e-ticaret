using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaret.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExpandLogDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "Logs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Exception",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpMethod",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Logs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Path",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestBody",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseBody",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StackTrace",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusCode",
                table: "Logs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Logs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Exception",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "HttpMethod",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Path",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "RequestBody",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "ResponseBody",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "StackTrace",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "StatusCode",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Logs");
        }
    }
}
