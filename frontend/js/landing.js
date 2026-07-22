// Tanıtım sayfası: kategorileri ve birkaç ürünü ÖNİZLEME olarak gösterir.
// Burada satın alma yok; ürün kartları yalnızca detay sayfasına yönlendirir.

async function loadLanding() {
    try {
        const [categories, products] = await Promise.all([
            apiGet("/categories"),
            apiGet("/products")
        ]);
        renderLandingCategories(categories);
        renderLandingProducts(products);
    } catch {
        // Backend kapalıysa bu bölümleri kaldır; sayfanın gerisi çalışmaya devam eder
        document.getElementById("categories-section")?.remove();
        document.getElementById("products-section")?.remove();
    }
}

function renderLandingCategories(categories) {
    if (!categories || categories.length === 0) {
        document.getElementById("categories-section")?.remove();
        return;
    }

    document.getElementById("landing-categories").innerHTML = categories
        .map(c => `<a class="cat-chip" href="shop.html?category=${c.id}">${esc(c.name)}</a>`)
        .join("");
}

function renderLandingProducts(products) {
    if (!products || products.length === 0) {
        document.getElementById("products-section")?.remove();
        return;
    }

    // İndirimli ürünler öne gelsin, en fazla 8 tane gösterelim
    const list = [...products]
        .sort((a, b) => (b.hasDiscount ? 1 : 0) - (a.hasDiscount ? 1 : 0))
        .slice(0, 8);

    document.getElementById("landing-products").innerHTML = list.map(p => {
        const media = p.imageUrl
            ? `<img class="pcard-media" src="${esc(p.imageUrl)}" alt="${esc(p.name)}">`
            : `<div class="pcard-ph">${esc(p.name[0])}</div>`;

        const price = p.hasDiscount
            ? `<span class="p-old">${p.price} TL</span><span class="p-new">${p.discountedPrice} TL</span>`
            : `<span class="p-price">${p.price} TL</span>`;

        const badge = p.hasDiscount ? `<span class="p-badge">İNDİRİM</span>` : "";

        return `
            <a class="pcard" href="product.html?id=${p.id}">
                ${badge}
                ${media}
                <h3>${esc(p.name)}</h3>
                <div>${price}</div>
                <span class="view-hint">İncele →</span>
            </a>`;
    }).join("");
}

loadLanding();
