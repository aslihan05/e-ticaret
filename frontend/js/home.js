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
let newOnly = false;         // "sadece yeni gelenler" (son 7 gün)
let reviewedOnly = false;    // "sadece değerlendirilenler" (en az bir yorumu olan)
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
    if (newOnly) params.set("newOnly", "true");
    if (reviewedOnly) params.set("reviewedOnly", "true");
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

    // "Tümü" (ana sayfa görünümü): bir kategoriden çıkılıyorsa eklenen geçmiş adımını
    // geri sararak (history.back → popstate → goHomeView) temiz biçimde ana sayfaya dön;
    // böylece geçmişte sahte adım birikmez.
    if (id == null) {
        catmenuKapat();
        if (categoryHistoryPushed) history.back();
        else goHomeView();
        return;
    }

    // Alt kategorisi olan bir üst kategoriye tıklamak "seçim" değil, "aç/kapa"dır:
    // menü açık kalır ki kullanıcı beliren alt kategoriyi seçebilsin. Menü yalnızca
    // nihai bir seçimde (alt kategorisi olmayan bir kategori) kapanır.
    const hasChildren = allCategories.some(c => c.parentId === id);
    if (hasChildren) {
        if (expandedParents.has(id)) expandedParents.delete(id);
        else expandedParents.add(id);
    }

    currentCategoryId = id;
    renderCategoryList();
    // Vitrin öğeleri (hızlı butonlar / sezon indirimi / haftanın fırsatı) yalnızca
    // ana sayfa görünümünde; kategori seçilince gizlenir, "Tümü"ye dönünce geri gelir.
    updateHomeSectionsVisibility();
    // Filtreler her zaman görünür değil: kullanıcı bir kategoriye dokununca solda açılır.
    openFilterPanel();
    // Ana sayfadan kategoriye ilk geçişte geçmişe tek adım ekle: Geri tuşu tek adımda
    // login'e değil, ana sayfaya (Tümü) döndürsün.
    pushCategoryHistory();
    // Üst kategoriye tıklandıysa menüyü AÇIK bırak (alt kategori henüz seçilmedi);
    // yaprak kategori seçildiyse iş bitti, kutuyu kapatıp ürünlere odaklan.
    if (!hasChildren) catmenuKapat();
    resetAndLoad();
}

/* ===== Solda açılan filtre paneli ===== */

// Panel varsayılan olarak gizli (hidden). Bir kategori seçilince açılır; ✕ ile kapanır.
const filterPanel = document.getElementById("filter-panel");
const shopLayout = document.getElementById("shop-layout");

// Kategori seçiliyken (.category-active) vitrin öğeleri gizlenir; "Tümü"de geri gelir.
// Ayrıca arama yapılırken veya bir hızlı buton (satan/favori/öneri/yeni) aktifken
// (.showcase-hidden) sezon indirimi + Haftanın Fırsatı bloğu gizlenir: sonuçlar ekranın
// çok altına kaymasın. Bu durumda hızlı butonlar görünür kalır (aktif olan kapatılabilsin).
function updateHomeSectionsVisibility() {
    shopLayout.classList.toggle("category-active", currentCategoryId != null);
    const quickActive = !!document.querySelector(".quick-cat.active");
    shopLayout.classList.toggle("showcase-hidden", quickActive || searchText !== "");
}

// "Geri" tuşu davranışı: ana sayfadan çıkılmış her durumda (kategori seçimi, hızlı buton
// veya arama) Geri tuşu sayfadan çıkıp login'e dönmemeli; TEK adımda ana sayfaya dönmeli.
// Bunun için ana sayfadan böyle bir görünüme ilk geçişte geçmişe TEK bir adım ekleriz; bu
// adım "ana sayfada değilim" durumunu temsil eder. Geri tuşu (popstate) bu adımı tüketip ana
// sayfa görünümünü geri getirir. Filtre panelinin açılıp kapanması artık geçmişe dokunmaz —
// panel ile kategori zaten birlikte var olur.
let categoryHistoryPushed = false;

function pushCategoryHistory() {
    if (!categoryHistoryPushed) {
        history.pushState({ shopCategory: true }, "");
        categoryHistoryPushed = true;
    }
}

// Filtre paneli aç/kapa artık yalnızca görsel — geçmiş yönetimi kategoriye taşındı.
function openFilterPanel() {
    filterPanel.hidden = false;
    shopLayout.classList.add("filters-open");
}

function closeFilterPanel() {
    shopLayout.classList.remove("filters-open");
    filterPanel.hidden = true;
}

// Ana sayfa görünümüne tam dönüş: kategori, hızlı buton ve arama temizlenir, panel kapanır,
// vitrin öğeleri geri gelir ve tüm ürünler (Tümü) yeniden yüklenir.
function goHomeView() {
    currentCategoryId = null;
    // Hızlı buton / arama da ana sayfadan çıkaran birer görünüm; Geri tuşu hepsini sıfırlar.
    quickCatTemizle();
    currentSort = "default";
    document.getElementById("sort").value = "default";
    searchText = "";
    document.getElementById("search").value = "";
    closeFilterPanel();
    updateHomeSectionsVisibility();
    renderCategoryList();
    document.getElementById("list-title").textContent = "Tüm Ürünler";
    resetAndLoad();
}

window.addEventListener("popstate", () => {
    // Kategori görünümündeyken Geri: sayfadan çıkma, ana sayfaya dön.
    if (categoryHistoryPushed || currentCategoryId != null) {
        categoryHistoryPushed = false;
        goHomeView();
    }
});

// ✕ ile filtre panelini kapatmak yalnızca paneli gizler; kategori seçili kalır, dolayısıyla
// vitrin öğeleri de gizli kalır (Geri tuşu yine ana sayfaya döndürür).
document.getElementById("filter-close").addEventListener("click", () => closeFilterPanel());

// Arama artık her tuşta sunucuya gidiyor. Debounce olmasaydı "elbise" yazan kullanıcı
// 6 istek attırır, üstelik cevaplar sırasız dönerse ekranda "elb" sonucu kalabilirdi.
// Kullanıcı yazmayı 350 ms bıraktığında tek istek gider.
let aramaZamanlayici;
document.getElementById("search").addEventListener("input", (e) => {
    searchText = e.target.value.trim();
    // Vitrin, arama kutusu dolar dolmaz (istek beklenmeden) kalksın; kutu boşalınca geri gelir.
    updateHomeSectionsVisibility();
    // Arama da ana sayfadan çıkaran bir görünüm: Geri tuşu login'e değil, ana sayfaya dönsün.
    if (searchText !== "") pushCategoryHistory();
    clearTimeout(aramaZamanlayici);
    aramaZamanlayici = setTimeout(resetAndLoad, 350);
});

document.getElementById("sort").addEventListener("change", (e) => {
    // Sıralama menüsünden seçim yapmak, aktif bir hızlı butonu (satan/favori/öneri) iptal eder:
    // iki sıralama aynı anda geçerli olamaz.
    quickCatTemizle();
    currentSort = e.target.value;
    // Hızlı buton iptal edildi: arama da yoksa vitrin geri gelir.
    updateHomeSectionsVisibility();
    resetAndLoad();
});

document.getElementById("price-apply").addEventListener("click", () => {
    const min = document.getElementById("price-min").value;
    const max = document.getElementById("price-max").value;
    priceMin = min === "" ? null : Number(min);
    priceMax = max === "" ? null : Number(max);
    // Elle girilen aralık bir hazır çiple birebir örtüşmeyebilir; seçili çip vurgusunu kaldır.
    for (const c of document.querySelectorAll(".price-chip")) c.classList.remove("active");
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

document.getElementById("filter-new").addEventListener("change", (e) => {
    newOnly = e.target.checked;
    resetAndLoad();
});

document.getElementById("filter-reviewed").addEventListener("change", (e) => {
    reviewedOnly = e.target.checked;
    resetAndLoad();
});

// Hazır fiyat aralığı çipleri: tek tıkla Min/Max kutularını doldurup filtreyi uygular.
// Aynı çipe tekrar basmak aralığı kaldırır (seçili çipi bir aç/kapa düğmesi gibi kullanır).
document.getElementById("price-presets").addEventListener("click", (e) => {
    const chip = e.target.closest(".price-chip");
    if (!chip) return;

    const min = chip.dataset.min;
    const max = chip.dataset.max;
    const zatenAktif = chip.classList.contains("active");

    for (const c of document.querySelectorAll(".price-chip")) c.classList.remove("active");

    if (zatenAktif) {
        priceMin = priceMax = null;
        document.getElementById("price-min").value = "";
        document.getElementById("price-max").value = "";
    } else {
        chip.classList.add("active");
        priceMin = min === "" ? null : Number(min);
        priceMax = max === "" ? null : Number(max);
        document.getElementById("price-min").value = min;
        document.getElementById("price-max").value = max;
    }
    resetAndLoad();
});

// "Filtreleri Temizle": fiyatla birlikte puan ve durum filtrelerini de sıfırlar,
// aksi halde temizledim sanıp hâlâ süzülmüş liste görmek kafa karıştırırdı.
document.getElementById("price-clear").addEventListener("click", () => {
    priceMin = priceMax = null;
    minRating = null;
    discountOnly = inStockOnly = newOnly = reviewedOnly = false;
    document.getElementById("price-min").value = "";
    document.getElementById("price-max").value = "";
    document.getElementById("rating-filter").value = "";
    document.getElementById("filter-discount").checked = false;
    document.getElementById("filter-instock").checked = false;
    document.getElementById("filter-new").checked = false;
    document.getElementById("filter-reviewed").checked = false;
    for (const c of document.querySelectorAll(".price-chip")) c.classList.remove("active");
    resetAndLoad();
});

/* ===== Header'daki "Kategoriler" açılır kutusu ===== */

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

    // Hızlı buton açıldıysa vitrini kaldır, kapatıldıysa (ve arama da yoksa) geri getir.
    updateHomeSectionsVisibility();
    // Hızlı buton listesi de ana sayfadan çıkarır: Geri tuşu tek adımda ana sayfaya dönsün,
    // sayfadan (login'e) çıkmasın.
    if (!zatenAktif) pushCategoryHistory();
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

/* ===== Haftanın Fırsatı vitrini ===== */
// Adminin bu hafta için seçtiği ürünler, gerçek indirimleriyle bir slider'da; üstte
// indirimin kaç gün daha geçerli olduğunu gösteren geri sayım. Veri sunucudan gelir
// (GET /weeklydeals); bölüm pasif ya da boşsa banner tek başına satırı kaplar.

let wdCountdownTimer = null;
// Karta tıklandığında detay kutusunu açabilmek için o anki vitrin ürünleri
let weeklyProducts = [];

async function loadWeeklyDeal() {
    const showcase = document.getElementById("showcase");
    const section = document.getElementById("weekly-deal");

    let data;
    try {
        data = await apiGet("/weeklydeals");
    } catch {
        data = null;
    }

    // Aktif değil ya da hiç ürün yoksa bölümü gizle, banner tüm satırı kaplasın
    if (!data || !data.isActive || !data.items || data.items.length === 0) {
        section.hidden = true;
        showcase.classList.add("no-deal");
        return;
    }

    showcase.classList.remove("no-deal");
    section.hidden = false;

    document.getElementById("wd-title-text").textContent = data.title || "Haftanın Fırsatı";
    weeklyProducts = data.items;
    renderWeeklyDealItems(data.items);
    startWeeklyCountdown(data.endsAt);
}

// Kartlar sezon indirimi şeridindekilerle birebir aynı: aynı .sale-card görünümü ve
// aynı davranış — tıklayınca yeni sayfaya gitmek yerine ürün detay kutusu (modal)
// açılır; sepete ekleme, favori ve puan bilgisi o kutunun içinde yaşar.
function renderWeeklyDealItems(items) {
    const track = document.getElementById("wd-track");
    track.innerHTML = items.map(p => `
        <button type="button" class="sale-card" data-id="${p.id}"
                aria-label="${esc(p.name)} — detayı aç">
            ${p.imageUrl
                ? `<img src="${esc(p.imageUrl)}" alt="">`
                : `<div class="img-placeholder">${esc(p.name[0])}</div>`}
            <span class="discount-badge">-%${discountPercent(p)}</span>
            <h4>${esc(p.name)}</h4>
            <div>${priceHtml(p)}</div>
        </button>`).join("");
}

// Karta tıklayınca sezon indirimi kartlarındaki gibi detay kutusunu aç
document.getElementById("wd-track").addEventListener("click", (e) => {
    const card = e.target.closest(".sale-card");
    if (!card) return;
    const p = weeklyProducts.find(x => x.id === Number(card.dataset.id));
    if (p) openProductModal(p);
});

// Slider okları: her tıklamada TAM BİR kart ilerler (kart genişliği + aradaki 14px boşluk),
// böylece her kaydırışta vitrine yalnızca bir yeni ürün girer.
function scrollWeekly(dir) {
    const track = document.getElementById("wd-track");
    const card = track.querySelector(".sale-card");
    const step = card ? card.getBoundingClientRect().width + 14 : 224;
    track.scrollBy({ left: dir * step, behavior: "smooth" });
}
document.getElementById("wd-prev").addEventListener("click", () => scrollWeekly(-1));
document.getElementById("wd-next").addEventListener("click", () => scrollWeekly(1));

// Geri sayım: "kaç gün daha" öne çıkar, altında saat:dakika:saniye tıklar.
function startWeeklyCountdown(endsAt) {
    const box = document.getElementById("wd-countdown");
    if (wdCountdownTimer) clearInterval(wdCountdownTimer);

    if (!endsAt) {
        box.innerHTML = `<span class="wd-cd-label">Süresiz fırsat</span>`;
        return;
    }

    const end = new Date(endsAt).getTime();

    const tick = () => {
        const diff = end - Date.now();
        if (diff <= 0) {
            // Süre doldu: bölümü gizle (indirim de sunucuda düşmüştür)
            clearInterval(wdCountdownTimer);
            document.getElementById("weekly-deal").hidden = true;
            document.getElementById("showcase").classList.add("no-deal");
            return;
        }
        const gun = Math.floor(diff / 86400000);
        const saat = Math.floor((diff % 86400000) / 3600000);
        const dk = Math.floor((diff % 3600000) / 60000);
        const sn = Math.floor((diff % 60000) / 1000);
        const iki = (n) => String(n).padStart(2, "0");
        box.innerHTML = `
            <span class="wd-cd-label">Bitmesine</span>
            <span class="wd-cd-days"><b>${gun}</b> gün</span>
            <span class="wd-cd-clock">${iki(saat)}:${iki(dk)}:${iki(sn)}</span>`;
    };

    tick();
    wdCountdownTimer = setInterval(tick, 1000);
}

setupNav();
updateCartCount();
// Tanıtımdan "shop.html?category=3" gibi bir kategoriyle gelindiyse filtre paneli açık başlar
// ve vitrin öğeleri gizli başlar (doğrudan kategoriyle gelindiği için ana sayfa görünümü değil).
// Geçmişe bir adım eklenir ki Geri tuşu önce ana sayfa görünümüne (Tümü) dönsün.
if (currentCategoryId) {
    openFilterPanel();
    pushCategoryHistory();
}
updateHomeSectionsVisibility();
loadProducts();
loadSaleStrip();
loadWeeklyDeal();
loadCategories();
