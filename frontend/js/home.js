let allProducts = [];
let currentCategoryId = null;
let currentSort = "default";
let searchText = "";
let priceMin = null;
let priceMax = null;

async function loadProducts() {
    allProducts = await apiGet("/products");
    renderProducts();
    renderSaleStrip();
}

function priceHtml(p) {
    return p.hasDiscount
        ? `<span class="price-old">${p.price} TL</span><strong class="price-new">${p.discountedPrice} TL</strong>`
        : `<strong>${p.price} TL</strong>`;
}

const discountPercent = (p) => Math.round((1 - p.discountedPrice / p.price) * 100);

// İndirimdeki ürünler "Sezon indirimi başladı" bannerının altında şerit olarak listelenir
function renderSaleStrip() {
    const strip = document.getElementById("sale-strip");
    const discounted = allProducts.filter(p => p.hasDiscount);

    if (discounted.length === 0) {
        strip.style.display = "none";
        return;
    }

    strip.style.display = "flex";
    strip.innerHTML = discounted.map(p => `
        <a class="sale-card" href="product.html?id=${p.id}">
            ${p.imageUrl
                ? `<img src="${p.imageUrl}" alt="${p.name}">`
                : `<div class="img-placeholder">${p.name[0]}</div>`}
            <span class="discount-badge">-%${discountPercent(p)}</span>
            <h4>${p.name}</h4>
            <div>${priceHtml(p)}</div>
        </a>`).join("");
}

function renderProducts() {
    let products = [...allProducts];

    if (currentCategoryId) {
        products = products.filter(p => p.categoryId === currentCategoryId);
    }

    if (searchText) {
        const query = searchText.toLowerCase();
        products = products.filter(p => p.name.toLowerCase().includes(query));
    }

    // Filtre ve sıralama geçerli (indirimliyse indirimli) fiyat üzerinden yapılır
    const eff = (p) => p.discountedPrice ?? p.price;

    if (priceMin != null) products = products.filter(p => eff(p) >= priceMin);
    if (priceMax != null) products = products.filter(p => eff(p) <= priceMax);

    if (currentSort === "price-asc") products.sort((a, b) => eff(a) - eff(b));
    if (currentSort === "price-desc") products.sort((a, b) => eff(b) - eff(a));

    const grid = document.getElementById("product-grid");
    grid.innerHTML = "";

    if (products.length === 0) {
        grid.innerHTML = "<p class='empty'>Ürün bulunamadı.</p>";
        return;
    }

    for (const p of products) {
        const outOfStock = !p.inStock;
        const card = document.createElement("div");
        card.className = "product-card";
        card.innerHTML = `
            <a class="card-media" href="product.html?id=${p.id}">
                ${p.imageUrl
                    ? `<img src="${p.imageUrl}" alt="${p.name}">`
                    : `<div class="img-placeholder">${p.name[0]}</div>`}
                ${outOfStock ? `<span class="stock-badge">Stokta yok</span>` : ""}
                ${p.hasDiscount ? `<span class="discount-badge">-%${discountPercent(p)}</span>` : ""}
            </a>
            <h3><a href="product.html?id=${p.id}">${p.name}</a></h3>
            <p>${p.description ?? ""}</p>
            <div class="card-footer">
                ${priceHtml(p)}
            </div>
            <button class="add-btn" data-id="${p.id}" ${outOfStock ? "disabled" : ""}>
                ${outOfStock ? "Tükendi" : "Sepete Ekle"}
            </button>`;
        grid.appendChild(card);
    }
}

async function loadCategories() {
    const categories = await apiGet("/categories");
    const list = document.getElementById("category-list");
    list.innerHTML = "";

    const all = document.createElement("li");
    all.textContent = "Tümü";
    all.className = "active";
    list.appendChild(all);

    for (const c of categories) {
        const li = document.createElement("li");
        li.textContent = c.name;
        li.dataset.id = c.id;
        list.appendChild(li);
    }

    list.addEventListener("click", (e) => {
        const li = e.target.closest("li");
        if (!li) return;

        list.querySelectorAll("li").forEach(item => item.classList.remove("active"));
        li.classList.add("active");

        currentCategoryId = li.dataset.id ? Number(li.dataset.id) : null;
        document.getElementById("list-title").textContent = li.dataset.id ? li.textContent : "Tüm Ürünler";
        renderProducts();
    });
}

document.getElementById("search").addEventListener("input", (e) => {
    searchText = e.target.value.trim();
    renderProducts();
});

document.getElementById("sort").addEventListener("change", (e) => {
    currentSort = e.target.value;
    renderProducts();
});

document.getElementById("price-apply").addEventListener("click", () => {
    const min = document.getElementById("price-min").value;
    const max = document.getElementById("price-max").value;
    priceMin = min === "" ? null : Number(min);
    priceMax = max === "" ? null : Number(max);
    renderProducts();
});

document.getElementById("price-clear").addEventListener("click", () => {
    priceMin = priceMax = null;
    document.getElementById("price-min").value = "";
    document.getElementById("price-max").value = "";
    renderProducts();
});

document.getElementById("product-grid").addEventListener("click", (e) => {
    const btn = e.target.closest(".add-btn");
    if (!btn) return;
    addToCart(Number(btn.dataset.id), btn);
});

setupNav();
updateCartCount();
loadProducts();
loadCategories();
