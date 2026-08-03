// Geçerli (süresi dolmamış) oturum yoksa login'e yönlendirir
if (!requireAuth()) throw new Error("Oturum gerekli");

// Uygulanmış kupon: yalnızca sunucunun onayladığı hali tutulur.
// null ise kupon yok. İndirim tutarı burada HESAPLANMAZ, sunucudan gelir —
// sepette gösterilen indirim ile siparişte kesilecek indirim aynı koddan çıkmalı.
let appliedCoupon = null;

// Profilden gelen kayıtlı teslimat bilgisi (yoksa null). Teslimat adımının formu mu yoksa
// özeti mi göstereceğine bu karar verir.
let kayitliAdres = null;

async function loadCart() {
    const items = await apiGet("/cart");
    const container = document.getElementById("cart-items");
    const summary = document.querySelector(".cart-summary");
    const couponBox = document.getElementById("coupon-box");
    container.innerHTML = "";

    if (items.length === 0) {
        container.innerHTML = "<p class='empty'>Sepetin boş. <a href='shop.html'>Alışverişe başla →</a></p>";
        summary.style.display = "none";
        couponBox.style.display = "none";
        teslimatAdimiKapat();
        appliedCoupon = null;   // Boş sepette kupon taşımanın anlamı yok
        return;
    }
    summary.style.display = "flex";
    couponBox.style.display = "block";

    // Fiyat ve "bir tane daha eklenebilir mi" kararını backend verir (item.unitPrice / item.canIncrease).
    // İndirim mantığının burada tekrar hesaplanması, sunucudan farklı sonuç verme riski taşıyordu;
    // ayrıca stok sayısı artık müşteriye hiç gönderilmiyor.
    let total = 0;
    for (const item of items) {
        total += item.lineTotal;
        const priceLabel = item.product.hasDiscount
            ? `<span class="price-old">${item.product.price} TL</span> <span class="price-new">${item.unitPrice} TL</span>`
            : `${item.unitPrice} TL`;
        const row = document.createElement("div");
        row.className = "cart-item";
        // Sepetteki ürünün görseli ve adı detay sayfasına götürür: müşteri "bu neydi?"
        // diye baktığında tıklanacak yer, zaten baktığı yerdir.
        const detayUrl = `product.html?id=${item.product.id}`;
        row.innerHTML = `
            <a href="${detayUrl}" class="cart-item-media" aria-label="${esc(item.product.name)} detayı">
                <img src="${esc(item.product.imageUrl)}" alt="">
            </a>
            <div class="cart-item-info">
                <h3><a href="${detayUrl}">${esc(item.product.name)}</a></h3>
                <span>${priceLabel}</span>
            </div>
           <div class="qty-controls">
                <button class="qty-btn" data-id="${item.id}" data-qty="${item.quantity - 1}">−</button>
                <span>${item.quantity}</span>
                <button class="qty-btn"
                        data-id="${item.id}"
                        data-qty="${item.quantity + 1}"
                        ${item.canIncrease ? "" : "disabled title='Bu üründen daha fazla alınamıyor'"}>+</button>
            </div>
            <strong>${item.lineTotal} TL</strong>
            <button class="remove-btn" data-id="${item.id}">🗑</button>`;
        container.appendChild(row);
    }

    await renderSummary(total);
}

// Ara toplam / indirim / toplam satırları.
// Kupon uygulanmışsa sepet değiştiği için YENİDEN doğrulanır: alt limitli bir kupon,
// müşteri sepetten ürün çıkarınca geçersizleşebilir. Sessizce eski indirimi göstermek,
// siparişte sürprizle karşılaşmak demekti.
async function renderSummary(subtotal) {
    const indirimSatiri = document.getElementById("discount-line");
    document.getElementById("cart-subtotal").textContent = `${subtotal} TL`;

    if (appliedCoupon) {
        try {
            const sonuc = await apiPost("/coupons/apply", { code: appliedCoupon.code });
            appliedCoupon = sonuc;
        } catch (err) {
            // Sepet değişince kupon şartları artık tutmuyor
            appliedCoupon = null;
            couponMesaji(err.message, "warn");
        }
    }

    if (appliedCoupon) {
        indirimSatiri.style.display = "flex";
        document.getElementById("discount-label").textContent =
            `İndirim (${appliedCoupon.code})`;
        document.getElementById("cart-discount").textContent = `−${appliedCoupon.discount} TL`;
        document.getElementById("cart-total").textContent = `${appliedCoupon.total} TL`;
    } else {
        indirimSatiri.style.display = "none";
        document.getElementById("cart-total").textContent = `${subtotal} TL`;
    }
}

function couponMesaji(metin, tur = "info") {
    const el = document.getElementById("coupon-message");
    el.textContent = metin;
    el.className = `coupon-message ${tur}`;
}

document.getElementById("cart-items").addEventListener("click", async (e) => {
    const qtyBtn = e.target.closest(".qty-btn");
    const removeBtn = e.target.closest(".remove-btn");

    try {
        if (qtyBtn) {
            const qty = Number(qtyBtn.dataset.qty);
            if (qty < 1) {
                await apiDelete(`/cart/${qtyBtn.dataset.id}`);
            } else {
                await apiPut(`/cart/${qtyBtn.dataset.id}`, { quantity: qty });
            }
        } else if (removeBtn) {
            await apiDelete(`/cart/${removeBtn.dataset.id}`);
        } else {
            return;
        }
        await loadCart();
        await updateCartCount();
    } catch (err) {
        alert(err.message);
    }
});

/* ===== Kupon ===== */

document.getElementById("coupon-apply").addEventListener("click", async () => {
    const kutu = document.getElementById("coupon-code");
    const buton = document.getElementById("coupon-apply");

    // Kupon zaten uygulanmışsa buton "Kaldır" olur
    if (appliedCoupon) {
        appliedCoupon = null;
        kutu.value = "";
        kutu.disabled = false;
        buton.textContent = "Uygula";
        couponMesaji("");
        await loadCart();
        return;
    }

    const kod = kutu.value.trim();
    if (!kod) {
        couponMesaji("Kupon kodu girmelisiniz.", "warn");
        return;
    }

    buton.disabled = true;
    try {
        const sonuc = await apiPost("/coupons/apply", { code: kod });
        appliedCoupon = sonuc;
        // Kod sunucunun döndüğü (büyük harfe çevrilmiş) haliyle gösterilir
        kutu.value = sonuc.code;
        kutu.disabled = true;
        buton.textContent = "Kaldır";
        couponMesaji(sonuc.message, "ok");
        await loadCart();
    } catch (err) {
        couponMesaji(err.message, "warn");
    } finally {
        buton.disabled = false;
    }
});

// Enter'a basmak da uygular: kod yazan kullanıcının refleksi bu.
document.getElementById("coupon-code").addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
        e.preventDefault();
        document.getElementById("coupon-apply").click();
    }
});

/* ===== Teslimat adımı ===== */

// Teslimat bilgileri sepet açılır açılmaz görünmüyor: müşteri önce ne aldığına bakar,
// adres formu o aşamada gürültü. "Siparişi Tamamla" ile bu adıma geçilir.
function teslimatAdimiAc() {
    document.getElementById("checkout-step").style.display = "block";
    document.getElementById("checkout-btn").style.display = "none";

    // Kayıtlı adres varsa form yerine özet gösterilir: müşterinin tek yapması gereken onaylamak.
    if (kayitliAdres) {
        adresOzetiGoster();
    } else {
        adresFormuGoster();
    }

    document.getElementById("checkout-step").scrollIntoView({ behavior: "smooth", block: "nearest" });
}

function teslimatAdimiKapat() {
    document.getElementById("checkout-step").style.display = "none";
    document.getElementById("checkout-btn").style.display = "inline-block";
}

function adresOzetiGoster() {
    const a = kayitliAdres;
    document.getElementById("address-summary-text").innerHTML = `
        <strong>${esc(a.recipientName)}</strong><br>
        ${esc(a.phone)}<br>
        <span class="muted">${esc(a.city)} / ${esc(a.district)} — ${esc(a.address)}</span>`;
    document.getElementById("address-summary").style.display = "flex";
    document.getElementById("checkout-form").style.display = "none";
}

function adresFormuGoster() {
    // Form, kayıtlı adresle doldurulur: "Değiştir" diyen müşteri sıfırdan yazmasın.
    if (kayitliAdres) {
        document.getElementById("recipient-name").value = kayitliAdres.recipientName ?? "";
        document.getElementById("phone").value = kayitliAdres.phone ?? "";
        document.getElementById("city").value = kayitliAdres.city ?? "";
        document.getElementById("district").value = kayitliAdres.district ?? "";
        document.getElementById("address").value = kayitliAdres.address ?? "";
    }

    // Not yalnızca ilk kez adres girenler için doğru: kayıtlı adresi olan müşterinin
    // buradaki değişikliği o siparişe özeldir, profiline yazılmaz.
    document.getElementById("address-save-note").style.display = kayitliAdres ? "none" : "block";

    document.getElementById("address-summary").style.display = "none";
    document.getElementById("checkout-form").style.display = "block";
}

document.getElementById("checkout-btn").addEventListener("click", teslimatAdimiAc);
document.getElementById("checkout-cancel").addEventListener("click", teslimatAdimiKapat);
document.getElementById("address-edit").addEventListener("click", adresFormuGoster);

document.getElementById("confirm-btn").addEventListener("click", async () => {
    const message = document.getElementById("message");
    const ozetGorunuyor = document.getElementById("address-summary").style.display !== "none";

    // Özet ekranındaysak kayıtlı adres gönderilir; form açıksa formdaki değerler.
    const body = ozetGorunuyor
        ? { ...kayitliAdres }
        : {
            recipientName: document.getElementById("recipient-name").value.trim(),
            phone: document.getElementById("phone").value.trim(),
            city: document.getElementById("city").value.trim(),
            district: document.getElementById("district").value.trim(),
            address: document.getElementById("address").value.trim()
        };

    // Kupon kodu gönderilir, indirim TUTARI değil: tutarı istemciden almak,
    // isteği düzenleyen birinin indirimi kendi belirlemesi demekti.
    body.couponCode = appliedCoupon?.code ?? null;

    if (!body.recipientName || !body.phone || !body.city || !body.district || !body.address) {
        message.style.color = "red";
        message.textContent = "Lütfen tüm teslimat bilgilerini doldurun.";
        return;
    }

    try {
        const siparis = await apiPost("/orders", body);

        // Sipariş verildi; kupon ve kutusu sıfırlanır
        appliedCoupon = null;
        const kutu = document.getElementById("coupon-code");
        kutu.value = "";
        kutu.disabled = false;
        document.getElementById("coupon-apply").textContent = "Uygula";
        couponMesaji("");

        teslimatAdimiKapat();
        await adresiYukle();   // ilk siparişte adres profile kaydedildi, özet artık dolu gelmeli
        await updateCartCount();
        // Boş sepet ekranı yerine, ne satın alındığını gösteren "siparişin alındı" ekranı
        siparisTamamlandiGoster(siparis);
    } catch (err) {
        message.style.color = "red";
        message.textContent = err.message;
    }
});

// Sipariş sonrası onay ekranı: satın alınan ürünlerin görseli, adı, adedi ve tutarıyla
// birlikte sipariş özeti. Eskiden burada yalnızca tek satırlık bir metin vardı; müşteri
// "ne aldım, ne kadar tuttu" bilgisini göremeden sepetin boşaldığını görüyordu.
function siparisTamamlandiGoster(siparis) {
    document.getElementById("message").textContent = "";
    document.getElementById("coupon-box").style.display = "none";
    document.querySelector(".cart-summary").style.display = "none";

    const kalemler = (siparis.items ?? []).map(it => `
        <div class="success-item">
            <a href="product.html?id=${it.productId}" class="success-item-media" aria-label="${esc(it.name)} detayı">
                ${it.imageUrl
                    ? `<img src="${esc(it.imageUrl)}" alt="">`
                    : `<div class="img-placeholder">${esc((it.name || "?")[0])}</div>`}
            </a>
            <div class="success-item-info">
                <h4><a href="product.html?id=${it.productId}">${esc(it.name)}</a></h4>
                <span class="muted">${it.quantity} adet × ${it.unitPrice} TL</span>
                <span class="success-item-no">Sipariş no #${it.orderId}</span>
            </div>
            <strong>${it.lineTotal} TL</strong>
        </div>`).join("");

    // Her ürün kendi sipariş numarasını alır: tek üründe "Sipariş no #5", birden çok
    // üründe hepsi listelenir — müşteri hangi numaranın hangi ürün olduğunu kalem
    // satırlarında da görür ve tek bir ürünü iptal ettirmek istediğinde numarayı bilir.
    const nolar = siparis.orderIds ?? [];
    const numaraMetni = nolar.length === 1
        ? `Sipariş no <strong>#${nolar[0]}</strong>`
        : `${nolar.length} ayrı sipariş oluştu: ${nolar.map(n => `<strong>#${n}</strong>`).join(", ")}`;

    const indirimSatiri = siparis.discount > 0
        ? `<div class="summary-line summary-discount">
               <span>İndirim (${esc(siparis.couponCode ?? "")})</span>
               <span>−${siparis.discount} TL</span>
           </div>`
        : "";

    document.getElementById("cart-items").innerHTML = `
        <div class="order-success">
            <div class="order-success-head">
                <div class="order-success-check" aria-hidden="true">✓</div>
                <h3>Siparişin alındı! 🎉</h3>
                <p class="muted">${numaraMetni} — admin onayından sonra hazırlanacak.</p>
            </div>

            <div class="success-items">${kalemler}</div>

            <div class="summary-lines order-success-summary">
                <div class="summary-line">
                    <span>Ara toplam</span>
                    <span>${siparis.subtotal} TL</span>
                </div>
                ${indirimSatiri}
                <div class="summary-line summary-total">
                    <span>Toplam</span>
                    <strong>${siparis.total} TL</strong>
                </div>
            </div>

            <div class="order-success-actions">
                <a class="add-btn" href="orders.html">📦 Siparişlerim</a>
                <a class="continue-link" href="shop.html">Alışverişe devam et →</a>
            </div>
        </div>`;

    document.getElementById("cart-items").scrollIntoView({ behavior: "smooth", block: "start" });
}

// Kayıtlı teslimat bilgisi profilden okunur. Eksik alan varsa (eski hesaplar) adres
// "kayıtlı" sayılmaz ve form açılır — yarım bir özeti onaylatmak sipariş hatası olurdu.
async function adresiYukle() {
    try {
        const p = await apiGet("/profile");
        kayitliAdres = (p.fullName && p.phone && p.city && p.district && p.address)
            ? {
                recipientName: p.fullName,
                phone: p.phone,
                city: p.city,
                district: p.district,
                address: p.address
            }
            : null;
    } catch {
        kayitliAdres = null;   // Profil çekilemezse form açılır; sipariş vermeye engel değil
    }
}

// Kuponlarım sayfasındaki "Sepette Kullan" bağlantısı kodu adresle taşır (?coupon=YAZ10):
// müşteri kodu elle yazmasın diye kutu doldurulup doğrudan uygulanır.
async function adresBaridenKuponUygula() {
    const kod = new URLSearchParams(location.search).get("coupon");
    if (!kod) return;

    document.getElementById("coupon-code").value = kod;
    document.getElementById("coupon-apply").click();

    // Kod adres çubuğunda kalırsa sayfa her yenilendiğinde tekrar uygulanmaya çalışılır
    history.replaceState(null, "", location.pathname);
}

setupNav();
updateCartCount();

// Sıra önemli: adres okunmadan teslimat adımı açılırsa kayıtlı adresi olan müşteriye
// boş form gösterilirdi. Sepet yüklendikten sonra kupon uygulanır (sepet boşsa kupon anlamsız).
(async () => {
    await adresiYukle();
    await loadCart();
    await adresBaridenKuponUygula();
})();
