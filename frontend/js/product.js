// Tek görsel varsa düz <img>; birden fazlaysa tek bir <img> + ileri/geri butonları döndürür.
// Görseller bir dizide (images) tutulur, butonlar bu <img>'in src'sini değiştirir.
function sliderHtml(images, name) {
    if (images.length === 1) {
        return `<img src="${esc(images[0])}" alt="${esc(name)}">`;
    }
    const dots = images.map((_, i) => `<span class="dot${i === 0 ? " active" : ""}" data-i="${i}"></span>`).join("");
    return `
        <div class="slider" id="p-slider">
            <button type="button" class="slider-arrow slider-prev" aria-label="Önceki">‹</button>
            <img class="slider-img" src="${esc(images[0])}" alt="${esc(name)}">
            <button type="button" class="slider-arrow slider-next" aria-label="Sonraki">›</button>
            <div class="slider-dots">${dots}</div>
        </div>`;
}

// Slider mantığı: görsel dizisini closure'da tutar, index'i modülo ile döndürerek
// (başta geri / sonda ileri basılınca) tek <img>'in src'sini günceller — sona gelince başa, başta geri gidince sona döner (loop).
function setupSlider(images) {
    const slider = document.getElementById("p-slider");
    if (!slider || images.length <= 1) return;

    let index = 0;
    const img = slider.querySelector(".slider-img");
    const dots = [...slider.querySelectorAll(".dot")];

    function show(i) {
        // (i + n) % n: -1 -> son görsel, n -> ilk görsel (döngü)
        index = (i + images.length) % images.length;
        img.src = images[index];
        dots.forEach((d, di) => d.classList.toggle("active", di === index));
    }

    slider.querySelector(".slider-prev").addEventListener("click", () => show(index - 1));
    slider.querySelector(".slider-next").addEventListener("click", () => show(index + 1));
    dots.forEach(d => d.addEventListener("click", () => show(Number(d.dataset.i))));
}

// "Üst Kategori › Kategori" izi. Kullanıcının kategori adını okuyup oraya dönmek istemesi
// en doğal beklenti; düz metinken tıklanamıyordu. Her parça vitrini o kategoriyle açar.
function categoryTrailHtml(category) {
    if (!category) return "";
    const parcalar = [];
    if (category.parentName) {
        parcalar.push(`<a href="shop.html?category=${category.parentId}">${esc(category.parentName)}</a>`);
    }
    parcalar.push(`<a href="shop.html?category=${category.id}">${esc(category.name)}</a>`);
    return parcalar.join(" › ");
}

/* ===== "Bu ürünü daha önce aldın" geçmişi ===== */

const HISTORY_STATUS = {
    0: { text: "Bekliyor", class: "status-pending" },
    1: { text: "Onaylandı", class: "status-approved" },
    2: { text: "Reddedildi", class: "status-rejected" },
    3: { text: "Kargoda", class: "status-shipped" },
    4: { text: "Teslim Edildi", class: "status-delivered" },
    5: { text: "İptal Edildi", class: "status-cancelled" }
};

// Kullanıcının SADECE kendi alımları. Giriş yoksa hiç istek atılmaz, bölüm de basılmaz.
// Hiç almamışsa da basılmaz — "0 kez aldınız" kutusu gürültüden ibaret.
async function loadMyHistory(productId) {
    if (!authInfo()) return;

    let h;
    try {
        h = await apiGet(`/products/${productId}/my-history`);
    } catch {
        return;   // Geçmiş, sayfanın asıl işi değil; çekilemezse sessizce atlanır
    }
    if (h.items.length === 0) return;

    const rows = h.items.map(i => {
        // Sipariş iptal/reddedilmişse kalem "Bekliyor" kalabilir; o durumda siparişin
        // genel durumunu göstermek daha doğru — admin tarihçesindeki kuralın aynısı.
        const iptalVeyaRed = i.orderStatus === 5 || i.orderStatus === 2;
        const s = HISTORY_STATUS[iptalVeyaRed ? i.orderStatus : i.itemStatus];
        const soluk = iptalVeyaRed || i.itemStatus === 2;
        const tarih = new Date(i.createdAt).toLocaleDateString("tr-TR");
        return `<li class="${soluk ? "item-rejected" : ""}">
            <a href="orders.html">#${i.orderId}</a>
            <span class="muted">${tarih}</span>
            <span>${i.quantity} adet × ${i.unitPrice} TL</span>
            <span class="status ${s.class}">${s.text}</span>
        </li>`;
    }).join("");

    const ozet = h.totalQty > 0
        ? `Bu ürünü <strong>${h.totalOrders} siparişte ${h.totalQty} adet</strong> aldın.
           En düşük ödediğin fiyat <strong>${h.lowestUnitPrice} TL</strong>,
           son alışverişin <strong>${new Date(h.lastPurchasedAt).toLocaleDateString("tr-TR")}</strong>.`
        : `Bu ürünü sipariş etmiştin ama tamamlanmış bir alımın yok.`;

    document.getElementById("my-history").innerHTML = `
        <h3>📜 Senin geçmişin</h3>
        <p class="history-summary">${ozet}</p>
        <ul class="history-list">${rows}</ul>`;
}

/* ===== Yorumlar ve puanlama ===== */

// Dolu/boş yıldız dizisi. Ortalama 4.3 gibi kesirliyse yuvarlanarak gösterilir —
// yarım yıldız çizmek yerine yanına sayısal değeri de bastığımız için bilgi kaybı olmuyor.
function starsHtml(rating) {
    const dolu = Math.round(rating);
    return `<span class="stars" aria-hidden="true">${"★".repeat(dolu)}${"☆".repeat(5 - dolu)}</span>`;
}

// Yıldız dağılımı çubukları: her satır "5 ★ ▓▓▓▓░░ 12" biçiminde.
// Yüzde, en çok oy alan yıldıza göre değil TOPLAM yoruma göre hesaplanır ki
// çubuk uzunluğu "yorumların kaçta kaçı" sorusunun cevabı olsun.
function distributionHtml(dist, total) {
    return [5, 4, 3, 2, 1].map(star => {
        const adet = dist[star] ?? 0;
        const yuzde = total > 0 ? (adet / total) * 100 : 0;
        return `
            <div class="dist-row">
                <span class="dist-label">${star} ★</span>
                <span class="dist-bar"><span class="dist-fill" style="width:${yuzde}%"></span></span>
                <span class="dist-count">${adet}</span>
            </div>`;
    }).join("");
}

// Yıldız seçici: gerçek radio input'ları (görsel olarak yıldıza dönüştürülüyor).
// <div>+JS ile yapılsaydı klavyeyle seçilemez, ekran okuyucu "5 seçenekli puan" demezdi.
//
// Sıra bilerek 5'ten 1'e: CSS'te "önceki kardeş" seçicisi olmadığı için, 3 seçilince
// 1-2-3'ü boyamanın tek yolu işaretli input'un SONRASINDAKİ kardeşleri (~) boyamak.
// Ters dizilim + .rating-input'taki row-reverse birlikte ekranda soldan sağa 1..5 verir.
// (İkisinden biri olmazsa yıldızlar hem ters sıralanır hem yanlış yönde dolar.)
function ratingInputHtml(selected = 0) {
    return [5, 4, 3, 2, 1].map(star => `
        <input type="radio" name="rating" id="star-${star}" value="${star}"
               ${star === selected ? "checked" : ""} required>
        <label for="star-${star}" title="${star} yıldız"><span class="sr-only">${star} yıldız</span></label>
    `).join("");
}

// Yorum formu (yeni yorum veya kendi yorumunu düzenleme).
function reviewFormHtml(mevcut = null) {
    const baslik = mevcut ? "Yorumunu düzenle" : "Bu ürünü değerlendir";
    return `
        <form class="review-form" id="review-form" data-id="${mevcut?.id ?? ""}">
            <h4>${baslik}</h4>
            <div class="rating-input">${ratingInputHtml(mevcut?.rating ?? 0)}</div>
            <textarea name="comment" rows="3" maxlength="1000"
                      placeholder="Ürün hakkındaki düşüncelerin (isteğe bağlı)">${esc(mevcut?.comment ?? "")}</textarea>
            <div class="review-form-actions">
                <button type="submit" class="add-btn">${mevcut ? "Güncelle" : "Yorumu Gönder"}</button>
                ${mevcut ? `<button type="button" class="link-btn" id="review-cancel">Vazgeç</button>` : ""}
            </div>
        </form>`;
}

function reviewItemHtml(r) {
    const tarih = new Date(r.createdAt).toLocaleDateString("tr-TR");
    // Yorum sahibi kendi yorumunu düzenleyip silebilir; admin (moderasyon için) yalnız silebilir.
    const adminMi = authInfo()?.role === "Admin";
    const actions = r.isMine
        ? `<button type="button" class="link-btn" data-edit="${r.id}">Düzenle</button>
           <button type="button" class="link-btn danger" data-delete="${r.id}">Sil</button>`
        : (adminMi ? `<button type="button" class="link-btn danger" data-delete="${r.id}">Sil</button>` : "");

    return `
        <li class="review-item${r.isMine ? " review-mine" : ""}">
            <div class="review-head">
                ${starsHtml(r.rating)}
                <strong>${esc(r.username)}</strong>
                ${r.isMine ? `<span class="review-badge">Senin yorumun</span>` : ""}
                <span class="muted">${tarih}${r.updatedAt ? " · düzenlendi" : ""}</span>
                <span class="review-actions">${actions}</span>
            </div>
            ${r.comment ? `<p class="review-comment">${esc(r.comment)}</p>` : ""}
        </li>`;
}

// Yorum bölümünün tamamını (özet + form + liste) çizer ve olaylarını bağlar.
async function loadReviews(productId) {
    const bolum = document.getElementById("reviews");
    if (!bolum) return;

    let data;
    try {
        data = await apiGet(`/products/${productId}/reviews`);
    } catch {
        bolum.innerHTML = "";   // Yorumlar sayfanın asıl işi değil; çekilemezse sessizce atlanır
        return;
    }

    const ozet = data.count > 0
        ? `<div class="review-summary">
               <div class="review-score">
                   <strong>${data.average.toFixed(1)}</strong>
                   ${starsHtml(data.average)}
                   <span class="muted">${data.count} değerlendirme</span>
               </div>
               <div class="review-dist">${distributionHtml(data.distribution, data.count)}</div>
           </div>`
        : `<p class="review-empty">Bu ürün henüz değerlendirilmemiş. Satın aldıysan ilk yorumu sen yazabilirsin.</p>`;

    // Form yalnızca ürünü gerçekten satın almış ve henüz yazmamış kullanıcıya gösterilir.
    // Yazamayacak kullanıcıya boş bir form göstermek, doldurup hata almasına yol açardı.
    const form = data.canReview ? reviewFormHtml() : "";

    bolum.innerHTML = `
        <h3>⭐ Değerlendirmeler</h3>
        ${ozet}
        ${form}
        <ul class="review-list">${data.items.map(reviewItemHtml).join("")}</ul>`;

    bagla(bolum, productId, data);
}

function bagla(bolum, productId, data) {
    const form = document.getElementById("review-form");
    if (form) {
        form.addEventListener("submit", (e) => gonder(e, productId));
        document.getElementById("review-cancel")
            ?.addEventListener("click", () => loadReviews(productId));
    }

    bolum.querySelector(".review-list")?.addEventListener("click", async (e) => {
        const duzenle = e.target.closest("[data-edit]");
        if (duzenle) {
            const r = data.items.find(x => x.id === Number(duzenle.dataset.edit));
            // Düzenleme formu listenin üstünde açılır; mevcut form varsa onun yerini alır
            const eski = document.getElementById("review-form");
            if (eski) eski.remove();
            bolum.querySelector(".review-list").insertAdjacentHTML("beforebegin", reviewFormHtml(r));
            bagla(bolum, productId, data);
            document.getElementById("review-form").scrollIntoView({ behavior: "smooth", block: "center" });
            return;
        }

        const sil = e.target.closest("[data-delete]");
        if (sil) {
            if (!confirm("Yorum silinsin mi?")) return;
            try {
                await apiDelete(`/reviews/${sil.dataset.delete}`);
                showToast("Yorum silindi.");
                await yenile(productId);
            } catch (err) {
                showToast(err.message, "warn");
            }
        }
    });
}

async function gonder(e, productId) {
    e.preventDefault();
    const form = e.target;
    const buton = form.querySelector("button[type=submit]");
    const rating = Number(form.querySelector("input[name=rating]:checked")?.value ?? 0);

    if (!rating) {
        showToast("Lütfen 1-5 arası bir puan seçin.", "warn");
        return;
    }

    buton.disabled = true;
    const govde = { rating, comment: form.comment.value };
    const reviewId = form.dataset.id;

    try {
        // data-id doluysa mevcut yorumun düzenlenmesi, boşsa yeni yorum
        if (reviewId) {
            await apiPut(`/reviews/${reviewId}`, govde);
            showToast("Yorumun güncellendi.");
        } else {
            await apiPost(`/products/${productId}/reviews`, govde);
            showToast("Yorumun yayınlandı. Teşekkürler!");
        }
        await yenile(productId);
    } catch (err) {
        buton.disabled = false;
        showToast(err.message, "warn");
    }
}

// Yorum değişince başlıktaki ortalama da değişir; ikisini birlikte tazeliyoruz.
async function yenile(productId) {
    await loadReviews(productId);
    try {
        const p = await apiGet(`/products/${productId}`);
        const ozet = document.getElementById("rating-summary");
        if (ozet) ozet.innerHTML = ratingSummaryHtml(p);
    } catch { /* Başlıktaki özet tazelenemezse yorum listesi yine de doğru */ }
}

// Ürün başlığının altındaki küçük puan özeti — tıklayınca yorumlara kaydırır.
function ratingSummaryHtml(p) {
    if (!p.reviewCount) return `<span class="muted">Henüz değerlendirilmemiş</span>`;
    return `<a href="#reviews">${starsHtml(p.averageRating)}
            <strong>${p.averageRating.toFixed(1)}</strong>
            <span class="muted">(${p.reviewCount} değerlendirme)</span></a>`;
}

async function loadProduct() {
    const params = new URLSearchParams(window.location.search);
    const id = params.get("id");
    const container = document.getElementById("product-detail");

    if (!id) {
        container.innerHTML = "<p>Ürün bulunamadı.</p>";
        return;
    }

    try {
        // Kalbin doğru dolulukta basılabilmesi için favori listesi ürünle birlikte beklenir
        const [p] = await Promise.all([apiGet(`/products/${id}`), loadFavoriteIds()]);
        const outOfStock = !p.inStock;
        document.title = `${p.name} - Ongima`;

        // Ana görsel + admin'in eklediği ek görseller, slider'da sırayla gösterilir
        const images = [p.imageUrl, ...(p.imageUrls ?? [])].filter(Boolean);

        container.innerHTML = `
            <div class="detail-media">
                ${images.length ? sliderHtml(images, p.name) : `<div class="img-placeholder">${esc(p.name[0])}</div>`}
            </div>
            <div class="detail-info">
                <span class="detail-category">${categoryTrailHtml(p.category)}</span>
                <h2>${esc(p.name)}</h2>
                <div class="rating-summary" id="rating-summary">${ratingSummaryHtml(p)}</div>
                <p class="detail-desc">${esc(p.description ?? "")}</p>
                <div class="detail-price">${p.hasDiscount
                    ? `<span class="price-old">${p.price} TL</span> <span class="price-new">${p.discountedPrice} TL</span>`
                    : `${p.price} TL`}</div>
                <p class="detail-stock">${outOfStock ? "Stokta yok" : "Stokta var ✓"}</p>
                <div class="detail-actions">
                    <button class="add-btn" data-id="${p.id}" ${outOfStock ? "disabled" : ""}>
                        ${outOfStock ? "Tükendi" : "Sepete Ekle"}
                    </button>
                    ${favButtonHtml(p.id)}
                </div>
                <a href="shop.html" class="back-link">← Alışverişe devam et</a>
            </div>
            <section id="my-history" class="my-history"></section>
            <section id="reviews" class="reviews"></section>`;

        container.querySelector(".add-btn").addEventListener("click", (e) => {
            addToCart(p.id, e.target);
        });
        container.querySelector(".fav-btn").addEventListener("click", (e) => {
            toggleFavorite(p.id, e.currentTarget);
        });
        setupSlider(images);
        loadMyHistory(p.id);
        loadReviews(p.id);
    } catch {
        container.innerHTML = "<p>Ürün yüklenemedi.</p>";
    }
}

setupNav();
updateCartCount();
loadProduct();
