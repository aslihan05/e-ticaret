using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaret.Api.Migrations
{
    /// <inheritdoc />
    public partial class SiparisiUrunBazindaAyir : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Mevcut siparişlerin her biri kendi başına bir alışverişti (bu değişiklikten
            // önce sepetin tamamı tek sipariş oluyordu). Hepsine AYRI birer CheckoutId
            // verilir: hepsi boş Guid'de kalsaydı, kupon kullanım sayımı (CheckoutId
            // DISTINCT) geçmişteki tüm kullanımları tek kullanım sanardı.
            migrationBuilder.Sql("UPDATE Orders SET CheckoutId = NEWID() WHERE CheckoutId = '00000000-0000-0000-0000-000000000000';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckoutId",
                table: "Orders");
        }
    }
}
