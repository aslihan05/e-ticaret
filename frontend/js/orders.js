// Geçerli (süresi dolmamış) oturum yoksa login'e yönlendirir
if (!requireAuth()) throw new Error("Oturum gerekli");

const STATUS = {
    0: { text: "Bekliyor", class: "status-pending" },
    1: { text: "Onaylandı", class: "status-approved" },
    2: { text: "Reddedildi", class: "status-rejected" },
    3: { text: "Kargoda", class: "status-shipped" },
    4: { text: "Teslim Edildi", class: "status-delivered" },
    5: { text: "İptal Edildi", class: "status-cancelled" }
};

// İptal butonu: siparişi yalnızca backend "canCancel" derse gösterilir.
// Süresi dolmuş bekleyen siparişte buton yerine neden gösterilir — kullanıcı
// butonun neden kaybolduğunu anlasın diye.
function cancelHtml(o) {
    if (o.canCancel) {
        const kalan = remainingText(o.cancelDeadline);
        return `<button class="cancel-btn" data-id="${o.id}">Siparişi İptal Et</button>
                <span class="cancel-note">İptal hakkın ${kalan} sonra doluyor</span>`;
    }
    // Sadece "Bekliyor" durumundayken süre dolmuş olabilir; diğer durumlarda zaten iptal edilemez
    if (o.status === 0) {
        return `<span class="cancel-note">İptal süresi doldu — siparişin hazırlanıyor.</span>`;
    }
    return "";
}

// Sipariş verildikten sonra müşteriye durumuna göre kısa, güven veren bir bilgilendirme.
// "Bundan sonra ne olacak?" sorusunu sipariş kartının içinde, ürünlerin hemen altında yanıtlar.
function orderInfo(o) {
    const anyRejected = o.orderItems.some(i => i.status === 2);

    let msg = "";
    switch (o.status) {
        case 0: // Bekliyor
            msg = "📝 Siparişin alındı ve onay bekliyor. Onaylandığında hazırlanıp kargoya verilecek; her aşamada e-posta ile bilgilendirileceksin.";
            break;
        case 1: // Onaylandı
            msg = "✅ Siparişin onaylandı ve hazırlanıyor. Kargoya verildiğinde sana haber vereceğiz.";
            break;
        case 3: // Kargoda
            msg = "🚚 Siparişin kargoya verildi, yolda! Kısa süre içinde adresine teslim edilecek.";
            break;
        case 4: // Teslim edildi
            msg = "🎉 Siparişin teslim edildi. Afiyet olsun — ürünlere puan vermeyi unutma!";
            break;
        case 2: // Reddedildi
            msg = "❌ Siparişin reddedildi ve senden bir ücret alınmadı.";
            break;
        case 5: // İptal edildi
            msg = "🚫 Bu siparişi iptal ettin.";
            break;
    }

    // Kısmi onay: sipariş onaylandı ama bazı kalemler reddedildiyse müşteriyi ayrıca bilgilendir.
    if (o.status === 1 && anyRejected) {
        msg += " Not: Bazı ürünler stok/uygunluk nedeniyle siparişten çıkarıldı; yalnızca onaylanan ürünler için ödeme alınır.";
    }

    return msg ? `<div class="order-info order-info-${o.status}">${msg}</div>` : "";
}

// "42 dakika" / "1 saat 5 dakika" gibi okunabilir kalan süre metni
function remainingText(deadline) {
    const ms = new Date(deadline) - new Date();
    if (ms <= 0) return "birazdan";
    const dakika = Math.round(ms / 60000);
    if (dakika < 60) return `${dakika} dakika`;
    const saat = Math.floor(dakika / 60);
    const kalanDk = dakika % 60;
    return kalanDk ? `${saat} saat ${kalanDk} dakika` : `${saat} saat`;
}

async function loadOrders() {
    const orders = await apiGet("/orders");
    const container = document.getElementById("orders-list");
    container.innerHTML = "";

    if (orders.length === 0) {
        container.innerHTML = "<p class='empty'>Henüz siparişin yok. <a href='shop.html'>Alışverişe başla →</a></p>";
        return;
    }

    for (const o of orders) {
        const s = STATUS[o.status];
        const date = new Date(o.createdAt).toLocaleString("tr-TR");
        // Reddedilen kalemler üstü çizili gösterilir ve toplama katılmaz
        // Geçmiş siparişteki ürün, "bunu tekrar alayım" için en doğal başlangıç noktası —
        // ama hiçbir yere tıklanamıyordu. Görsel ve ad artık ürün detayına götürür.
        const itemsHtml = o.orderItems.map(i => {
            const rejected = i.status === 2;
            const detayUrl = `product.html?id=${i.product.id}`;
            const thumb = i.product.imageUrl
                ? `<img class="order-item-img" src="${esc(i.product.imageUrl)}" alt="">`
                : `<span class="order-item-img img-placeholder">${esc(i.product.name[0])}</span>`;
            return `<li class="${rejected ? "item-rejected" : ""}">
                <a class="order-item-link" href="${detayUrl}">
                    ${thumb}
                    <span class="order-item-text">${esc(i.product.name)} × ${i.quantity} — ${i.unitPrice * i.quantity} TL${rejected ? " (reddedildi)" : ""}</span>
                </a>
            </li>`;
        }).join("");
        // Ara toplam / indirim / toplam artık sunucudan geliyor (kupon hesabı orada yapılır).
        // Kısmi onayda taban da sunucuda küçülür, indirim ona göre yeniden hesaplanır.
        const totalHtml = o.discount > 0
            ? `<div class="order-totals">
                   <div><span>Ara toplam</span><span>${o.subtotal} TL</span></div>
                   <div class="order-discount"><span>İndirim (${esc(o.couponCode)})</span><span>−${o.discount} TL</span></div>
                   <div class="order-grand"><span>Toplam</span><strong>${o.total} TL</strong></div>
               </div>`
            : `<div class="order-total">Toplam: <strong>${o.total} TL</strong></div>`;

        // Teslimat bilgisi (eski siparişlerde olmayabilir)
        const delivery = o.recipientName
            ? `<div class="order-delivery">📦 ${esc(o.recipientName)} • ${esc(o.phone)}<br>${esc(o.city)}${o.district ? " / " + esc(o.district) : ""} — ${esc(o.address)}</div>`
            : "";

        const card = document.createElement("div");
        card.className = "order-card";
        card.innerHTML = `
            <div class="order-header">
                <div>
                    <strong>Sipariş #${o.id}</strong>
                    <span class="order-date">${date}</span>
                </div>
                <span class="status ${s.class}">${s.text}</span>
            </div>
            <ul class="order-items">${itemsHtml}</ul>
            ${orderInfo(o)}
            ${delivery}
            ${totalHtml}
            ${cancelHtml(o)}`;
        container.appendChild(card);
    }
}

// "Bekliyor" durumundaki siparişi iptal et
document.getElementById("orders-list").addEventListener("click", async (e) => {
    const btn = e.target.closest(".cancel-btn");
    if (!btn) return;

    if (!confirm("Siparişi iptal etmek istediğine emin misin?")) return;

    btn.disabled = true;
    try {
        await apiPost(`/orders/${btn.dataset.id}/cancel`, {});
        showToast("Sipariş iptal edildi.", "info");
        loadOrders();
    } catch (err) {
        btn.disabled = false;
        alert(err.message);
    }
});

setupNav();
updateCartCount();
loadOrders();
