let allProducts = [];
let currentCategoryId = null;
let currentSort = "default";
let searchText = "";

async function loadProducts() {
    allProducts = await apiGet("/products");
    renderProducts();
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

    if (currentSort === "price-asc") products.sort((a, b) => a.price - b.price);
    if (currentSort === "price-desc") products.sort((a, b) => b.price - a.price);

    const grid = document.getElementById("product-grid");
    grid.innerHTML = "";

    if (products.length === 0) {
        grid.innerHTML = "<p class='empty'>Ürün bulunamadı.</p>";
        return;
    }

    for (const p of products) {
        const outOfStock = p.stock === 0;
        const card = document.createElement("div");
        card.className = "product-card";
        card.innerHTML = `
            <a class="card-media" href="product.html?id=${p.id}">
                ${p.imageUrl
                    ? `<img src="${p.imageUrl}" alt="${p.name}">`
                    : `<div class="img-placeholder">${p.name[0]}</div>`}
                ${outOfStock ? `<span class="stock-badge">Stokta yok</span>` : ""}
            </a>
            <h3><a href="product.html?id=${p.id}">${p.name}</a></h3>
            <p>${p.description ?? ""}</p>
            <div class="card-footer">
                <strong>${p.price} TL</strong>
                <span>Stok: ${p.stock}</span>
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

document.getElementById("product-grid").addEventListener("click", (e) => {
    const btn = e.target.closest(".add-btn");
    if (!btn) return;
    addToCart(Number(btn.dataset.id), btn);
});

setupNav();
updateCartCount();
loadProducts();
loadCategories();
