async function loadProduct() {
    const params = new URLSearchParams(window.location.search);
    const id = params.get("id");
    const container = document.getElementById("product-detail");

    if (!id) {
        container.innerHTML = "<p>Ürün bulunamadı.</p>";
        return;
    }

    try {
        const p = await apiGet(`/products/${id}`);
        const outOfStock = p.stock === 0;
        document.title = `${p.name} - E-Ticaret`;

        container.innerHTML = `
            <div class="detail-media">
                ${p.imageUrl
                    ? `<img src="${p.imageUrl}" alt="${p.name}">`
                    : `<div class="img-placeholder">${p.name[0]}</div>`}
            </div>
            <div class="detail-info">
                <span class="detail-category">${p.category?.name ?? ""}</span>
                <h2>${p.name}</h2>
                <p class="detail-desc">${p.description ?? ""}</p>
                <div class="detail-price">${p.price} TL</div>
                <p class="detail-stock">${outOfStock ? "Stokta yok" : `Stok: ${p.stock} adet`}</p>
                <button class="add-btn" data-id="${p.id}" ${outOfStock ? "disabled" : ""}>
                    ${outOfStock ? "Tükendi" : "Sepete Ekle"}
                </button>
                <a href="index.html" class="back-link">← Alışverişe devam et</a>
            </div>`;

        container.querySelector(".add-btn").addEventListener("click", (e) => {
            addToCart(p.id, e.target);
        });
    } catch {
        container.innerHTML = "<p>Ürün yüklenemedi.</p>";
    }
}

setupNav();
updateCartCount();
loadProduct();
