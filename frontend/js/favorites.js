// Favoriler kişisel bir liste: giriş yoksa gösterilecek bir şey yok, login'e yönlendirilir.
const auth = requireAuth();

// Kart, vitrindekiyle aynı görünür; farkı kalbin her zaman dolu başlaması
// ve çıkarılınca kartın listeden kalkması.
function favCardHtml(p) {
    const outOfStock = !p.inStock;
    const indirimYuzdesi = Math.round((1 - p.discountedPrice / p.price) * 100);
    const fiyat = p.hasDiscount
        ? `<span class="price-old">${p.price} TL</span><strong class="price-new">${p.discountedPrice} TL</strong>`
        : `<strong>${p.price} TL</strong>`;

    const yildiz = p.reviewCount
        ? `<div class="card-rating" title="${p.averageRating} / 5">
               <span class="stars" aria-hidden="true">${"★".repeat(Math.round(p.averageRating))}${"☆".repeat(5 - Math.round(p.averageRating))}</span>
               <span class="muted">${p.averageRating.toFixed(1)} (${p.reviewCount})</span>
           </div>`
        : "";

    return `
        <div class="product-card" data-card="${p.id}">
            <div class="card-media">
                ${p.imageUrl
                    ? `<img src="${esc(p.imageUrl)}" alt="${esc(p.name)}">`
                    : `<div class="img-placeholder">${esc(p.name[0])}</div>`}
                ${outOfStock ? `<span class="stock-badge">Stokta yok</span>` : ""}
                ${p.hasDiscount ? `<span class="discount-badge">-%${indirimYuzdesi}</span>` : ""}
                ${favButtonHtml(p.id)}
            </div>
            <h3><a class="card-link" href="product.html?id=${p.id}">${esc(p.name)}</a></h3>
            ${yildiz}
            <p>${esc(p.description ?? "")}</p>
            <div class="card-footer">${fiyat}</div>
            <button class="add-btn" data-id="${p.id}" ${outOfStock ? "disabled" : ""}>
                ${outOfStock ? "Tükendi" : "Sepete Ekle"}
            </button>
        </div>`;
}

async function loadFavorites() {
    const grid = document.getElementById("favorites");
    const baslik = document.getElementById("fav-title");

    let favoriler;
    try {
        // favoriteIds, kalplerin dolu basılması için gerekli (favButtonHtml onu okur)
        [favoriler] = await Promise.all([apiGet("/favorites"), loadFavoriteIds()]);
    } catch {
        grid.innerHTML = "<p class='empty'>Favoriler yüklenemedi.</p>";
        return;
    }

    baslik.textContent = favoriler.length
        ? `❤️ Favorilerim (${favoriler.length})`
        : "❤️ Favorilerim";

    if (favoriler.length === 0) {
        grid.innerHTML = `
            <p class="empty">
                Henüz favorin yok. Beğendiğin ürünlerin kalbine dokunarak buraya ekleyebilirsin.
                <br><a href="shop.html">Alışverişe başla →</a>
            </p>`;
        return;
    }

    grid.innerHTML = favoriler.map(favCardHtml).join("");
}

document.getElementById("favorites").addEventListener("click", async (e) => {
    const fav = e.target.closest(".fav-btn");
    if (fav) {
        e.preventDefault();   // Kalp, kartı kaplayan detay bağlantısının üstünde
        await toggleFavorite(Number(fav.dataset.fav), fav);
        // Vitrinden farklı olarak burada liste, favorilerin ta kendisi: çıkarılan ürünün
        // kartı listede kalamaz. Listeyi tazeleyince kart düşer, başlıktaki sayı da güncellenir.
        await loadFavorites();
        return;
    }

    const btn = e.target.closest(".add-btn");
    if (btn) addToCart(Number(btn.dataset.id), btn);
});

setupNav();
updateCartCount();
loadFavorites();
