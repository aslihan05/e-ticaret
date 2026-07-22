using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaret.Api.Migrations
{
    /// <inheritdoc />
    public partial class CouponMultipleUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SIRA ÖNEMLİ, iki ayrı sebeple:
            //
            // 1. Coupons.UserId üzerindeki eski yabancı anahtar ÖNCE düşürülür. Durduğu sürece
            //    SQL Server, Users'tan CouponUsers'a iki cascade yolu görüyor
            //    (Users → Coupons → CouponUsers ve Users → CouponUsers) ve tabloyu reddediyor.
            // 2. Sütunun KENDİSİ en sonda silinir: EF onu veriyi taşımadan siliyordu ve
            //    mevcut kişiye özel kupon atamaları kaybolurdu.
            migrationBuilder.DropForeignKey(
                name: "FK_Coupons_Users_UserId",
                table: "Coupons");

            migrationBuilder.DropIndex(
                name: "IX_Coupons_UserId",
                table: "Coupons");

            migrationBuilder.CreateTable(
                name: "CouponUsers",
                columns: table => new
                {
                    CouponId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponUsers", x => new { x.CouponId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CouponUsers_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CouponUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsers_UserId",
                table: "CouponUsers",
                column: "UserId");

            // Eski tek kişilik atamalar ara tabloya taşınır
            migrationBuilder.Sql(@"
                INSERT INTO CouponUsers (CouponId, UserId)
                SELECT Id, UserId FROM Coupons WHERE UserId IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Coupons");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Coupons",
                type: "int",
                nullable: true);

            // Geri dönüşte tek sütuna yalnızca BİR kullanıcı sığar: birden çok kişiye
            // tanımlanmış kuponlarda en küçük UserId korunur, diğer atamalar kaybolur.
            // Bu kayıp kaçınılmaz — eski şema çoklu atamayı ifade edemiyor.
            migrationBuilder.Sql(@"
                UPDATE c SET UserId = (SELECT MIN(cu.UserId) FROM CouponUsers cu WHERE cu.CouponId = c.Id)
                FROM Coupons c
                WHERE EXISTS (SELECT 1 FROM CouponUsers cu WHERE cu.CouponId = c.Id);");

            migrationBuilder.DropTable(
                name: "CouponUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_UserId",
                table: "Coupons",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Coupons_Users_UserId",
                table: "Coupons",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
