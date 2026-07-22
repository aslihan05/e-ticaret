// Filtreleme, sıralama ve sayfalama artık SUNUCUDA yapılıyor (GET /products/browse).
// Eskiden tüm katalog tek seferde çekilip tarayıcıda süzülüyordu; katalog büyüdükçe
// bu, her ziyaretçiye binlerce ürünü indirtmek anlamına gelirdi.
// Bu yüzden burada tüm ürünler değil, YALNIZCA görüntülenen sayfa tutulur.
let pageProducts = [];      // Ekrandaki sayfanın ürünleri
let saleProducts = [];      // Banner altındaki indirim şeridi (sayfalamadan bağımsız)

// Tanıtım sayfasından "shop.html?category=3" gibi gelinirse o kategoriyle başla
let currentCategoryId = Number(new URLSearchParams(location.search).get("category")) || null;
let currentSort = "default";
let searchText = "";
let priceMin = null;
let priceMax = null;
let minRating = null;        // "en az X yıldız" filtresi (null = tüm puanlar)
let discountOnly = false;    // "sadece indirimli ürünler"
let inStockOnly = false;     // "sadece stokta olanlar"
let currentPage = 1;
let pageInfo = { page: 1, pageSize: 12, total: 0, totalPages: 0 };

// Ekrandaki filtrelerin sunucu sorgusuna çevrilmiş hali
function browseQuery() {
    const params = new URLSearchParams();
    if (searchText) params.set("search", searchText);
    if (currentCategoryId) params.set("categoryId", currentCategoryId);
    if (priceMin != null) params.set("minPrice", priceMin);
    if (priceMax != null) params.set("maxPrice", priceMax);
    if (minRating != null) params.set("minRating", minRating);
    if (discountOnly) params.set("discountOnly", "true");
    if (inStockOnly) params.set("inStockOnly", "true");
    if (currentSort !== "default") params.set("sort", currentSort);
    params.set("page", currentPage);
    return params.toString();
}

// Her filtre değişikliği 1. sayfaya döner: 3. sayfadayken "elbise" aratıp
// 3. sayfada kalmak, çoğu zaman boş liste demek olurdu.
function resetAndLoad() {
    currentPage = 1;
    loadProducts();
}

// Ürünler yüklenene kadar shimmer efektli iskelet kartlar gösterilir: boş ekran
// yerine yapının önden belirmesi bekleme hissini azaltır (algılanan performans).
function renderSkeletons(count = 8) {
    const grid = document.getElementById("product-grid");
    grid.innerHTML = Array.from({ length: count }, () => `
        <div class="product-card skeleton-card" aria-hidden="true">
            <div class="sk sk-media"></div>
            <div class="sk sk-line" style="width:80%"></div>
            <div class="sk sk-line" style="width:55%"></div>
            <div class="sk sk-line" style="width:40%;margin-top:auto"></div>
            <div class="sk sk-btn"></div>
        </div>`).join("");
}

async function loadProducts() {
    const grid = document.getElementById("product-grid");
    renderSkeletons(pageInfo?.pageSize || 8);

    let data;
    try {
        // Favori id'leri ürünlerle birlikte beklenir: önce kartlar boş kalpli basılıp
        // sonra dolsaydı, kalpler gözle görülür şekilde "zıplardı".
        [data] = await Promise.all([apiGet(`/products/browse?${browseQuery()}`), loadFavoriteIds()]);
    } catch {
        grid.innerHTML = "<p class='empty'>Ürünler yüklenemedi.</p>";
        document.getElementById("pagination").innerHTML = "";
        return;
    }

    pageProducts = data.items;
    pageInfo = data;
    renderProducts();
    renderPagination();
}

// İndirim şeridi sayfalamadan bağımsızdır: 2. sayfaya geçince kampanyanın kaybolmaması
// için kendi isteğini atar ve sayfa değiştikçe yeniden yüklenmez.
async function loadSaleStrip() {
    try {
        const data = await apiGet("/products/browse?discountOnly=true&pageSize=20");
        saleProducts = data.items;
    } catch {
        saleProducts = [];
    }
    renderSaleStrip();
}

function priceHtml(p) {
    return p.hasDiscount
        ? `<span class="price-old">${p.price} TL</span><strong class="price-new">${p.discountedPrice} TL</strong>`
        : `<strong>${p.price} TL</strong>`;
}

const discountPercent = (p) => Math.round((1 - p.discountedPrice / p.price) * 100);

// Karttaki yıldız satırı. Hiç yorumu olmayan üründe boş yıldız dizisi basmak
// ürünü "kötü puanlı" gösterirdi; o yüzden yorum yoksa satır hiç çizilmez.
function ratingHtml(p) {
    if (!p.reviewCount) return "";
    const dolu = Math.round(p.averageRating);
    return `<div class="card-rating" title="${p.averageRating} / 5">
                <span class="stars" aria-hidden="true">${"★".repeat(dolu)}${"☆".repeat(5 - dolu)}</span>
                <span class="muted">${p.averageRating.toFixed(1)} (${p.reviewCount})</span>
            </div>`;
}

// İndirimdeki ürünler "Sezon indirimi başladı" bannerının altında şerit olarak listelenir
function renderSaleStrip() {
    const strip = document.getElementById("sale-strip");
    const discounted = saleProducts;

    if (discounted.length === 0) {
        strip.style.display = "none";
        return;
    }

    strip.style.display = "flex";
    // Gerçek <button>: tıklanabilir olduğu için değil, tıklandığında bir KUTU açtığı için.
    // (Detay sayfasına gitseydi <a> olurdu.) <div> + JS click olsaydı klavyeyle
    // seçilemez ve ekran okuyucuya tıklanabilir olduğunu söylemezdi.
    strip.innerHTML = discounted.map(p => `
        <button type="button" class="sale-card" data-id="${p.id}"
                aria-label="${esc(p.name)} — detayı aç">
            ${p.imageUrl
                ? `<img src="${esc(p.imageUrl)}" alt="">`
                : `<div class="img-placeholder">${esc(p.name[0])}</div>`}
            <span class="discount-badge">-%${discountPercent(p)}</span>
            <h4>${esc(p.name)}</h4>
            <div>${priceHtml(p)}</div>
        </button>`).join("");

    // Karta tıklayınca yeni sayfaya gitmeden detayı kutu (modal) olarak aç
    strip.onclick = (e) => {
        const card = e.target.closest(".sale-card");
        if (!card) return;
        const p = saleProducts.find(x => x.id === Number(card.dataset.id));
        if (p) openProductModal(p);
    };
}

// Modalı ilk ihtiyaçta bir kez oluşturur, sonraki çağrılarda aynısını kullanır
function ensureModal() {
    let overlay = document.getElementById("product-modal");
    if (overlay) return overlay;

    overlay = document.createElement("div");
    overlay.id = "product-modal";
    overlay.className = "modal-overlay";
    overlay.innerHTML = `
        <div class="modal-box">
            <button class="modal-close" aria-label="Kapat">✕</button>
            <div class="modal-content"></div>
        </div>`;
    document.body.appendChild(overlay);

    overlay.addEventListener("click", (e) => {
        // Boş alana veya ✕'e tıklayınca kapat
        if (e.target === overlay || e.target.classList.contains("modal-close")) {
            overlay.classList.remove("open");
            return;
        }
        // Modal içindeki kalp
        const fav = e.target.closest(".fav-btn");
        if (fav) {
            toggleFavorite(Number(fav.dataset.fav), fav);
            return;
        }
        // Modal içindeki "Sepete Ekle"
        const btn = e.target.closest(".add-btn");
        if (btn) addToCart(Number(btn.dataset.id), btn);
    });

    // Fare ile boş alana tıklayıp kapatabilen kullanıcının klavyedeki karşılığı Esc'tir.
    // Olmazsa klavye kullanıcısı kutuyu kapatmak için ✕'e kadar sekmelemek zorunda kalır.
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") overlay.classList.remove("open");
    });

    return overlay;
}

function openProductModal(p) {
    const overlay = ensureModal();
    const outOfStock = !p.inStock;

    overlay.querySelector(".modal-content").innerHTML = `
        <div class="modal-media">
            ${p.imageUrl
                ? `<img src="${esc(p.imageUrl)}" alt="${esc(p.name)}">`
                : `<div class="img-placeholder">${esc(p.name[0])}</div>`}
            ${p.hasDiscount ? `<span class="discount-badge">-%${discountPercent(p)}</span>` : ""}
            ${favButtonHtml(p.id)}
        </div>
        <h3>${esc(p.name)}</h3>
        ${ratingHtml(p)}
        <p class="modal-desc">${esc(p.description ?? "")}</p>
        <div class="modal-price">${priceHtml(p)}</div>
        <button class="add-btn" data-id="${p.id}" ${outOfStock ? "disabled" : ""}>
            ${outOfStock ? "Tükendi" : "Sepete Ekle"}
        </button>
        <a class="modal-detail-link" href="product.html?id=${p.id}">Ürün sayfasına git →</a>`;

    overlay.classList.add("open");
}

// Sunucu zaten filtreli, sıralı ve sayfalanmış bir liste döndüğü için burada
// yalnızca çizim yapılır — süzme/sıralama mantığı artık tek yerde (backend'de) yaşıyor.
function renderProducts() {
    const products = pageProducts;
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
        // Kartın TAMAMI tıklanabilir: başlıktaki bağlantı CSS'te (::after) kartın üstüne
        // görünmez bir katman serer, böylece açıklamaya/fiyata/boşluğa tıklamak da detaya götürür.
        // Görsel ve başlık ayrı ayrı <a> yapılsaydı aynı hedefe giden iki sekme durağı olur,
        // klavyeyle gezen kullanıcı aynı ürünü iki kez geçmek zorunda kalırdı.
        card.innerHTML = `
            <div class="card-media">
                ${p.imageUrl
                    ? `<img src="${esc(p.imageUrl)}" alt="${esc(p.name)}">`
                    : `<div class="img-placeholder">${esc(p.name[0])}</div>`}
                ${outOfStock ? `<span class="stock-badge">Stokta yok</span>` : ""}
                ${p.hasDiscount ? `<span class="discount-badge">-%${discountPercent(p)}</span>` : ""}
                ${favButtonHtml(p.id)}
            </div>
            <h3><a class="card-link" href="product.html?id=${p.id}">${esc(p.name)}</a></h3>
            ${ratingHtml(p)}
            <p>${esc(p.description ?? "")}</p>
            <div class="card-footer">
                ${priceHtml(p)}
            </div>
            <button class="add-btn" data-id="${p.id}" ${outOfStock ? "disabled" : ""}>
                ${outOfStock ? "Tükendi" : "Sepete Ekle"}
            </button>`;
        grid.appendChild(card);
    }
}

/* ===== Sayfalama ===== */

// Gösterilecek sayfa numaraları. 100 sayfa varsa 100 düğme basmak anlamsız;
// mevcut sayfanın iki yanındaki pencere + ilk/son sayfa gösterilir, arası "…" olur.
function pageNumbers(current, total) {
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);

    const sayfalar = new Set([1, total, current]);
    for (const n of [current - 1, current + 1]) {
        if (n > 1 && n < total) sayfalar.add(n);
    }

    const sirali = [...sayfalar].sort((a, b) => a - b);
    const sonuc = [];
    for (let i = 0; i < sirali.length; i++) {
        // Ardışık olmayan iki numara arasına üç nokta konur
        if (i > 0 && sirali[i] - sirali[i - 1] > 1) sonuc.push("...");
        sonuc.push(sirali[i]);
    }
    return sonuc;
}

function renderPagination() {
    const kutu = document.getElementById("pagination");
    const { page, pageSize, total, totalPages } = pageInfo;

    // Tek sayfa (veya hiç sonuç) varsa sayfalama çizmek gürültüden ibaret
    if (totalPages <= 1) {
        kutu.innerHTML = total > 0
            ? `<p class="page-info">${total} ürün</p>`
            : "";
        return;
    }

    const ilk = (page - 1) * pageSize + 1;
    const son = Math.min(page * pageSize, total);

    const dugmeler = pageNumbers(page, totalPages).map(n =>
        n === "..."
            ? `<span class="page-gap">…</span>`
            : `<button type="button" class="page-btn${n === page ? " active" : ""}"
                       data-page="${n}" ${n === page ? 'aria-current="page"' : ""}>${n}</button>`
    ).join("");

    kutu.innerHTML = `
        <p class="page-info">${total} üründen ${ilk}-${son} arası gösteriliyor</p>
        <nav class="page-nav" aria-label="Sayfalar">
            <button type="button" class="page-btn" data-page="${page - 1}" ${page === 1 ? "disabled" : ""}>‹ Önceki</button>
            ${dugmeler}
            <button type="button" class="page-btn" data-page="${page + 1}" ${page === totalPages ? "disabled" : ""}>Sonraki ›</button>
        </nav>`;
}

document.getElementById("pagination").addEventListener("click", (e) => {
    const btn = e.target.closest(".page-btn");
    if (!btn || btn.disabled) return;

    currentPage = Number(btn.dataset.page);
    loadProducts();
    // Sayfa değişince listenin başına dön: aksi halde kullanıcı yeni sayfanın
    // ortasına düşer ve ilk ürünleri görmez.
    document.getElementById("product-grid").scrollIntoView({ behavior: "smooth", block: "start" });
});

let allCategories = [];
// Alt kategorileri açık tutulan üst kategorilerin id'leri (varsayılan: hiçbiri açık değil)
const expandedParents = new Set();

async function loadCategories() {
    allCategories = await apiGet("/categories");

    // Sayfa doğrudan bir alt kategoriyle açıldıysa (örn. tanıtımdan gelindi), üst kategorisi
    // otomatik açık başlasın ki seçili alt kategori görünür olsun
    const current = allCategories.find(c => c.id === currentCategoryId);
    if (current?.parentId != null) expandedParents.add(current.parentId);

    renderCategoryList();

    const list = document.getElementById("category-list");
    list.addEventListener("click", onCategoryClick);

    // Klavye karşılığı: <button> olsalardı bunu tarayıcı bedava verirdi, role="button"
    // verilen elemanda Enter/Space'i elle bağlamak gerekir. Space'in varsayılanı
    // sayfayı kaydırmaktır; preventDefault olmazsa seçim yaparken sayfa zıplar.
    list.addEventListener("keydown", (e) => {
        if (e.key !== "Enter" && e.key !== " ") return;
        if (!e.target.closest("li")) return;
        e.preventDefault();
        onCategoryClick(e);
    });
}

function renderCategoryList() {
    const list = document.getElementById("category-list");
    list.innerHTML = "";

    // Kategori satırları <li> + JS tıklama olduğu için klavyeye kapalıydı: fareyle
    // tıklanabilen her şey Tab ile de seçilebilmeli, Enter/Space ile çalışmalı.
    // (Bu iş <button> ile "doğru" olurdu ama mevcut liste stilini baştan yazmak gerekirdi;
    // role+tabindex aynı davranışı bozmadan verir. Enter/Space onCategoryClick'e bağlı.)
    const tiklanabilirYap = (li) => {
        li.tabIndex = 0;
        li.setAttribute("role", "button");
    };

    const all = document.createElement("li");
    all.textContent = "Tümü";
    if (!currentCategoryId) all.className = "active";
    tiklanabilirYap(all);
    list.appendChild(all);

    // Ana kategoriler her zaman görünür; alt kategoriler sadece üst kategorisi
    // "açık" (expandedParents içinde) ise listelenir — üst kategoriye tıklanınca açılır/kapanır
    const parents = allCategories.filter(c => c.parentId == null);
    for (const p of parents) {
        const children = allCategories.filter(c => c.parentId === p.id);
        const expanded = expandedParents.has(p.id);

        const li = document.createElement("li");
        li.dataset.id = p.id;
        li.dataset.name = p.name;
        li.innerHTML = children.length
            ? `<span class="cat-toggle">${expanded ? "▾" : "▸"}</span>${esc(p.name)}`
            : esc(p.name);
        if (p.id === currentCategoryId) li.classList.add("active");
        // aria-expanded yalnızca gerçekten açılıp kapanan (alt kategorisi olan) satırlara konur;
        // alt kategorisi olmayana konsaydı ekran okuyucu olmayan bir katmanı haber verirdi.
        if (children.length) li.setAttribute("aria-expanded", String(expanded));
        tiklanabilirYap(li);
        list.appendChild(li);

        if (children.length && expanded) {
            for (const ch of children) {
                const cli = document.createElement("li");
                cli.textContent = "— " + ch.name;
                cli.dataset.id = ch.id;
                cli.dataset.name = ch.name;
                cli.classList.add("subcat-item");
                if (ch.id === currentCategoryId) cli.classList.add("active");
                tiklanabilirYap(cli);
                list.appendChild(cli);
            }
        }
    }

    document.getElementById("list-title").textContent =
        currentCategoryId ? (allCategories.find(c => c.id === currentCategoryId)?.name ?? "Tüm Ürünler") : "Tüm Ürünler";
}

function onCategoryClick(e) {
    const li = e.target.closest("li");
    if (!li) return;

    const id = li.dataset.id ? Number(li.dataset.id) : null;

    // Çocuğu olan bir üst kategoriye tıklanınca alt kategoriler aç/kapa olur
    if (id != null && allCategories.some(c => c.parentId === id)) {
        if (expandedParents.has(id)) expandedParents.delete(id);
        else expandedParents.add(id);
    }

    currentCategoryId = id;
    renderCategoryList();
    resetAndLoad();
}

// Arama artık her tuşta sunucuya gidiyor. Debounce olmasaydı "elbise" yazan kullanıcı
// 6 istek attırır, üstelik cevaplar sırasız dönerse ekranda "elb" sonucu kalabilirdi.
// Kullanıcı yazmayı 350 ms bıraktığında tek istek gider.
let aramaZamanlayici;
document.getElementById("search").addEventListener("input", (e) => {
    searchText = e.target.value.trim();
    clearTimeout(aramaZamanlayici);
    aramaZamanlayici = setTimeout(resetAndLoad, 350);
});

document.getElementById("sort").addEventListener("change", (e) => {
    // Sıralama menüsünden seçim yapmak, aktif bir hızlı butonu (satan/favori/öneri) iptal eder:
    // iki sıralama aynı anda geçerli olamaz.
    quickCatTemizle();
    currentSort = e.target.value;
    resetAndLoad();
});

document.getElementById("price-apply").addEventListener("click", () => {
    const min = document.getElementById("price-min").value;
    const max = document.getElementById("price-max").value;
    priceMin = min === "" ? null : Number(min);
    priceMax = max === "" ? null : Number(max);
    resetAndLoad();
});

// Puan filtresi ve durum kutuları anında uygulanır (tek tıkla sonuç); fiyat ise
// iki sayı gerektirdiği için "Uygula" butonuna bağlı kalır.
document.getElementById("rating-filter").addEventListener("change", (e) => {
    minRating = e.target.value === "" ? null : Number(e.target.value);
    resetAndLoad();
});

document.getElementById("filter-discount").addEventListener("change", (e) => {
    discountOnly = e.target.checked;
    resetAndLoad();
});

document.getElementById("filter-instock").addEventListener("change", (e) => {
    inStockOnly = e.target.checked;
    resetAndLoad();
});

// "Filtreleri Temizle": fiyatla birlikte puan ve durum filtrelerini de sıfırlar,
// aksi halde temizledim sanıp hâlâ süzülmüş liste görmek kafa karıştırırdı.
document.getElementById("price-clear").addEventListener("click", () => {
    priceMin = priceMax = null;
    minRating = null;
    discountOnly = inStockOnly = false;
    document.getElementById("price-min").value = "";
    document.getElementById("price-max").value = "";
    document.getElementById("rating-filter").value = "";
    document.getElementById("filter-discount").checked = false;
    document.getElementById("filter-instock").checked = false;
    resetAndLoad();
});

// Kenar çubuğu panelleri (Kategoriler / Filtreler) başlığına tıklanınca açılıp kapanır.
// Buton + aria-expanded ile: klavye ve ekran okuyucu bedavaya çalışır.
for (const head of document.querySelectorAll(".sidebar-panel .panel-head")) {
    head.addEventListener("click", () => {
        const panel = head.closest(".sidebar-panel");
        const acik = panel.classList.toggle("collapsed");
        head.setAttribute("aria-expanded", String(!acik));
    });
}

/* ===== Header'daki "Kategoriler & Filtreler" açılır kutusu ===== */

const catmenuWrap = document.getElementById("catmenu");
const catmenuTrigger = document.getElementById("catmenu-trigger");
const catmenuBox = document.getElementById("catmenu-box");

function catmenuAc() {
    catmenuBox.classList.add("open");
    catmenuTrigger.setAttribute("aria-expanded", "true");
}
function catmenuKapat() {
    catmenuBox.classList.remove("open");
    catmenuTrigger.setAttribute("aria-expanded", "false");
}

catmenuTrigger.addEventListener("click", (e) => {
    e.stopPropagation();
    catmenuBox.classList.contains("open") ? catmenuKapat() : catmenuAc();
});

// Kutunun içine tıklamak onu kapatmamalı (kategori/filtre seçilirken açık kalsın);
// dışına tıklamak ve Esc kapatır.
catmenuBox.addEventListener("click", (e) => e.stopPropagation());
document.addEventListener("click", () => catmenuKapat());
document.addEventListener("keydown", (e) => { if (e.key === "Escape") catmenuKapat(); });

/* ===== Yuvarlak hızlı kategori butonları (En çok satan / favori / öneri) ===== */

// Bu butonlar sıralamayı (currentSort) belirler. Aktif butona tekrar basmak varsayılana
// döner. Sıralama menüsü ile aynı anda geçerli olamazlar; birini seçmek diğerini sıfırlar.
const QUICK_LABELS = {
    "best-selling": "En Çok Satanlar",
    "most-favorited": "En Çok Favori Seçilenler",
    "recommended": "Senin İçin Önerilenler",
    "new-arrivals": "Yeni Gelenler"
};

function quickCatTemizle() {
    for (const b of document.querySelectorAll(".quick-cat")) b.classList.remove("active");
}

document.getElementById("quick-cats").addEventListener("click", (e) => {
    const btn = e.target.closest(".quick-cat");
    if (!btn) return;

    const mode = btn.dataset.mode;
    const zatenAktif = btn.classList.contains("active");

    quickCatTemizle();
    // Sıralama menüsü bu modları içermez; görsel tutarlılık için "Önerilen"e çekilir.
    document.getElementById("sort").value = "default";

    if (zatenAktif) {
        // Aktif butona tekrar basıldı: hızlı sıralamayı kapat, varsayılana dön
        currentSort = "default";
        document.getElementById("list-title").textContent =
            currentCategoryId ? (allCategories.find(c => c.id === currentCategoryId)?.name ?? "Tüm Ürünler") : "Tüm Ürünler";
    } else {
        btn.classList.add("active");
        currentSort = mode;
        document.getElementById("list-title").textContent = QUICK_LABELS[mode] ?? "Ürünler";
    }

    resetAndLoad();
});

document.getElementById("product-grid").addEventListener("click", (e) => {
    // Kalp, kartın tamamını kaplayan detay bağlantısının üstünde duruyor; tıklama
    // oraya sızarsa favoriye eklerken ürün sayfasına gideriz.
    const fav = e.target.closest(".fav-btn");
    if (fav) {
        e.preventDefault();
        toggleFavorite(Number(fav.dataset.fav), fav);
        return;
    }

    const btn = e.target.closest(".add-btn");
    if (!btn) return;
    addToCart(Number(btn.dataset.id), btn);
});

setupNav();
updateCartCount();
loadProducts();
loadSaleStrip();
loadCategories();
