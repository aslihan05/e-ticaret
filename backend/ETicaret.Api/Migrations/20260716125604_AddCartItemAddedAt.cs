using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaret.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCartItemAddedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AddedAt",
                table: "CartItems",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Sütunun varsayılanı 0001-01-01; bu, migration'dan önce sepete eklenmiş ürünlerin
            // rezervasyonunun anında "süresi dolmuş" sayılması demek olurdu. Mevcut sepetler
            // haksızlığa uğramasın diye şu ana çekilir — herkes 30 dakikalık süreyle başlar.
            migrationBuilder.Sql("UPDATE CartItems SET AddedAt = GETUTCDATE();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedAt",
                table: "CartItems");
        }
    }
}
