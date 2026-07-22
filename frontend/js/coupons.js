// Müşterinin kullanabileceği kuponlar. Liste sunucudan gelir: hangi kuponun bu kullanıcıya
// görüneceği kararı (kişiye özel mi, süresi/limiti dolmuş mu) burada tekrar edilmez —
// istemcide süzmek, kişiye özel kuponları herkese göndermek demek olurdu.
if (!requireAuth()) throw new Error("Oturum gerekli");

const KUPON_TIPI = { 0: "Yüzde", 1: "Tutar" };

// Bitişe kalan süre: "3 gün kaldı" gibi. Müşteri için tarihten daha okunur bir bilgi.
function bitisMetni(endsAt) {
    if (!endsAt) return "Süresiz";

    const bitis = new Date(endsAt);
    const kalanGun = Math.ceil((bitis - Date.now()) / 86400000);
    const tarih = bitis.toLocaleDateString("tr-TR");

    if (kalanGun <= 0) return `Bugün son gün (${tarih})`;
    if (kalanGun === 1) return `Yarın bitiyor (${tarih})`;
    if (kalanGun <= 7) return `${kalanGun} gün kaldı (${tarih})`;
    return `Son gün: ${tarih}`;
}

async function loadCoupons() {
    const kutu = document.getElementById("coupons");

    let coupons;
    try {
        coupons = await apiGet("/coupons/my");
    } catch {
        kutu.innerHTML = "<p class='empty'>Kuponlar yüklenemedi.</p>";
        return;
    }

    if (coupons.length === 0) {
        kutu.innerHTML = `<p class='empty'>Şu an kullanabileceğin kupon yok.
            <a href='shop.html'>Alışverişe göz at →</a></p>`;
        return;
    }

    kutu.innerHTML = coupons.map(c => {
        const sonGun = bitisMetni(c.endsAt);
        const altLimit = c.minOrderTotal != null
            ? `En az ${c.minOrderTotal} TL'lik sepette geçerli`
            : "Alt limit yok";
        // Kişiye özel kuponun rozetle ayrılması, müşterinin "bu bana özel" bilgisini görmesi için
        const rozet = c.kisiyeOzel ? `<span class="coupon-badge">Sana özel</span>` : "";
        const kalan = c.kalanHakkim != null
            ? `<li>Kalan kullanım hakkın: ${c.kalanHakkim}</li>`
            : "";

        return `
            <article class="coupon-card${c.kisiyeOzel ? " coupon-card-personal" : ""}">
                <div class="coupon-amount">${esc(c.ozet)}<span>indirim</span></div>
                <div class="coupon-body">
                    ${rozet}
                    <button type="button" class="coupon-code" data-code="${esc(c.code)}"
                            title="Kodu kopyalamak için tıkla">${esc(c.code)}</button>
                    <ul class="coupon-meta">
                        <li>${altLimit}</li>
                        <li>${sonGun}</li>
                        ${kalan}
                    </ul>
                    <a class="add-btn coupon-use" href="cart.html?coupon=${encodeURIComponent(c.code)}">Sepette Kullan</a>
                </div>
            </article>`;
    }).join("");
}

// Kodu panoya kopyalar. navigator.clipboard yalnızca güvenli bağlamda (https/localhost)
// çalışır; başarısız olursa kod zaten ekranda yazılı olduğu için sessizce geçiyoruz.
document.getElementById("coupons").addEventListener("click", async (e) => {
    const btn = e.target.closest(".coupon-code");
    if (!btn) return;

    try {
        await navigator.clipboard.writeText(btn.dataset.code);
        showToast(`${btn.dataset.code} kopyalandı`);
    } catch {
        showToast("Kod kopyalanamadı, elle yazabilirsin.", "warn");
    }
});

setupNav();
updateCartCount();
loadCoupons();
