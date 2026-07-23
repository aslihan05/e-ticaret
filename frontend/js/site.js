// Kullanıcı/veritabanı kaynaklı metinler innerHTML'e basılmadan önce kaçışlanır.
// Aksi halde kullanıcı adı/adres/ürün adı gibi alanlara gömülen HTML çalışır (stored XSS).
function escapeHtml(value) {
    if (value == null) return "";
    return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}
const esc = escapeHtml;

// ===== Erişim denetimi (JWT tabanlı) =====
// Rol ve oturum, ayrı ve düzenlenebilir bir localStorage değerine ("role") güvenmek
// yerine imzalı JWT'nin içinden okunur. Böylece DevTools'tan role="Admin" yazıp
// yönetim paneline erişme (Broken Access Control) engellenir.
function parseJwt(token) {
    try {
        const base64 = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
        const json = decodeURIComponent(
            atob(base64).split("").map(c => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2)).join("")
        );
        return JSON.parse(json);
    } catch {
        return null;   // Bozuk/kurcalanmış token
    }
}

// Geçerli (çözülebilen ve süresi dolmamış) oturumun bilgilerini döndürür; yoksa null.
function authInfo() {
    const token = localStorage.getItem("token");
    if (!token) return null;
    const p = parseJwt(token);
    if (!p) return null;
    if (p.exp && p.exp * 1000 <= Date.now()) return null;   // Süresi dolmuş token geçersiz sayılır
    const role = p["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] ?? p.role ?? null;
    const username = p["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"] ?? p.name ?? localStorage.getItem("username");
    return { token, role, username, userId: p.UserId ?? null };
}

// Sayfa koruması: geçerli oturum yoksa login'e, gerekli rol JWT içinde tutmuyorsa
// vitrine yönlendirir. requiredRole verilmezse yalnızca giriş yapılmış olması yeterli.
function requireAuth(requiredRole) {
    const auth = authInfo();
    if (!auth) {
        localStorage.clear();
        window.location.replace("login.html");
        return null;
    }
    if (requiredRole && auth.role !== requiredRole) {
        window.location.replace("shop.html");   // Yetkisiz kullanıcı kendi alanına döner
        return null;
    }
    return auth;
}

// Üye bağlantıları tek bir açılır menüde toplanır. Sepet de buraya girdiği için
// sepetteki ürün sayısı menü DÜĞMESİNDE gösterilir (#cart-count): menü kapalıyken
// de görünmesi gerekiyor, yoksa müşteri sepetinde ürün olduğunu fark etmez.
function setupNav() {
    const nav = document.getElementById("nav");
    const auth = authInfo();

    if (!auth) {
        nav.innerHTML = `<a href="login.html">Giriş Yap</a>`;
        return;
    }

    const adminLink = auth.role === "Admin"
        ? `<a href="admin.html" role="menuitem">🛠️ Yönetim</a>`
        : "";

    // Hediye kutusu bugün açıldıysa dikkat çekici efekt (titreşim/parıltı) kapatılır:
    // hatırlatmanın amacı kutuyu açtırmaktı, açıldıysa artık gerekmez. mysterybox.js
    // kutu açılınca/cooldown'da bu işareti bugünün tarihiyle localStorage'a yazar.
    // Not: localStorage tek başına güvenilir değil (başka cihaz, temizlenmiş önbellek,
    // ya da kutunun chatbot üzerinden açılması). İlk render'ı bu hızlı işaretle yapıp
    // hemen ardından sunucudan teyit alırız (aşağıdaki gitfDurumTeyit).
    const hediyeAcildi = localStorage.getItem("giftOpenedDate") === new Date().toDateString();
    const giftClass = hediyeAcildi ? "gift-link opened" : "gift-link";
    const giftTitle = hediyeAcildi ? "Hediye Kutusu" : "Hediye Kutusu — açmayı unutma!";

    nav.innerHTML = `
        <a href="cart.html" id="nav-cart-link" class="nav-quick" title="Sepetim">🛒 Sepetim<span id="cart-count" class="cart-count"></span></a>
        <a href="favorites.html" title="Favorilerim">❤️ Favorilerim</a>
        <a href="mysterybox.html" class="${giftClass}" title="${giftTitle}"><span class="gift-emoji" aria-hidden="true">🎁</span> Hediye Kutusu</a>
        <div class="nav-menu">
            <button type="button" class="nav-toggle" id="nav-toggle"
                    aria-haspopup="true" aria-expanded="false" aria-controls="nav-dropdown">
                <span class="nav-username">${esc(auth.username ?? "Hesabım")}</span>
                <span class="nav-caret" aria-hidden="true">▾</span>
            </button>
            <div class="nav-dropdown" id="nav-dropdown" role="menu" hidden>
                <a href="orders.html" role="menuitem">📦 Siparişlerim</a>
                <a href="coupons.html" role="menuitem">🎟️ Kuponlarım</a>
                ${adminLink}
                <a href="profil.html" role="menuitem">👤 Hesabım</a>
                <a href="#" id="logout-link" role="menuitem" class="nav-logout">🚪 Çıkış Yap</a>
            </div>
        </div>`;

    const toggle = document.getElementById("nav-toggle");
    const dropdown = document.getElementById("nav-dropdown");

    const ac = () => {
        dropdown.hidden = false;
        toggle.setAttribute("aria-expanded", "true");
    };
    const kapat = () => {
        dropdown.hidden = true;
        toggle.setAttribute("aria-expanded", "false");
    };

    toggle.addEventListener("click", (e) => {
        e.stopPropagation();   // aşağıdaki "dışarı tıklama" dinleyicisi hemen kapatmasın
        dropdown.hidden ? ac() : kapat();
    });

    // Menü dışına tıklayınca ve Esc'e basınca kapanır
    document.addEventListener("click", (e) => {
        if (!dropdown.hidden && !e.target.closest(".nav-menu")) kapat();
    });
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape" && !dropdown.hidden) {
            kapat();
            toggle.focus();
        }
    });

    document.getElementById("logout-link").addEventListener("click", (e) => {
        e.preventDefault();
        localStorage.clear();
        window.location.href = "login.html";
    });

    // "Sepetim" sayfadan ayrılmadan kayan panelde açılır. Zaten sepet sayfasındaysak
    // (cart.html) linkin normal davranıp sayfayı yenilemesi daha doğru.
    const cartLink = document.getElementById("nav-cart-link");
    const onCartPage = /(^|\/)cart\.html$/.test(location.pathname);
    if (cartLink && !onCartPage) {
        cartLink.addEventListener("click", (e) => {
            e.preventDefault();
            kapat();
            openCartDrawer();
        });
    }

    giftDurumTeyit();
}

// Sunucudan bugünkü hediye kutusu durumunu teyit eder. localStorage bayrağı bugünü
// göstermiyorsa (stale/eksik) ama sunucu "bugün zaten açıldı" diyorsa, nav'daki 🎁
// linkinin titreşimini susturur ve bayrağı düzeltir; böylece müşteri ödülünü aldıysa
// kutu günün geri kalanında (kutular yenilenene kadar) titremez.
async function giftDurumTeyit() {
    const link = document.querySelector(".gift-link");
    if (!link || link.classList.contains("opened")) return;   // zaten susturulmuş

    try {
        const durum = await apiGet("/mysterybox");
        if (durum && durum.canOpen === false) {
            link.classList.add("opened");
            link.title = "Hediye Kutusu";
            localStorage.setItem("giftOpenedDate", new Date().toDateString());
        }
    } catch {
        // Durum alınamazsa mevcut (localStorage'a dayalı) haliyle bırak.
    }
}

async function updateCartCount() {
    const badge = document.getElementById("cart-count");
    if (!badge) return;

    if (!localStorage.getItem("token")) {
        badge.style.display = "none";
        return;
    }

    try {
        const items = await apiGet("/cart");
        const total = items.reduce((sum, item) => sum + item.quantity, 0);
        badge.textContent = total;
        badge.style.display = total > 0 ? "flex" : "none";
    } catch {
        badge.style.display = "none";
    }
}

// Sağ altta birkaç saniyeliğine görünen bildirim
function showToast(message, type = "info") {
    let container = document.getElementById("toast-container");
    if (!container) {
        container = document.createElement("div");
        container.id = "toast-container";
        document.body.appendChild(container);
    }

    const toast = document.createElement("div");
    toast.className = `toast toast-${type}`;
    toast.textContent = message;
    container.appendChild(toast);

    requestAnimationFrame(() => toast.classList.add("show"));
    setTimeout(() => {
        toast.classList.remove("show");
        setTimeout(() => toast.remove(), 300);
    }, 2600);
}

/* ===== Favoriler ===== */

// Kullanıcının favori ürün id'leri. Vitrin 36 kart basarken her kart için ayrı istek
// atmak yerine liste bir kez çekilip burada tutulur.
let favoriteIds = new Set();

// Giriş yoksa istek atılmaz: 401 dönecek bir isteği göndermenin anlamı yok.
async function loadFavoriteIds() {
    if (!authInfo()) {
        favoriteIds = new Set();
        return favoriteIds;
    }
    try {
        favoriteIds = new Set(await apiGet("/favorites/ids"));
    } catch {
        favoriteIds = new Set();   // Favoriler sayfanın asıl işi değil; çekilemezse kalpler boş kalır
    }
    return favoriteIds;
}

// Kalp butonu. Gerçek <button> + aria-pressed: ekran okuyucu "favoride mi" bilgisini
// buradan okur, klavyeyle de basılabilir.
function favButtonHtml(productId) {
    const dolu = favoriteIds.has(productId);
    return `<button type="button" class="fav-btn${dolu ? " is-fav" : ""}" data-fav="${productId}"
                aria-pressed="${dolu}" title="${dolu ? "Favorilerden çıkar" : "Favorilere ekle"}">
                <span class="sr-only">${dolu ? "Favorilerden çıkar" : "Favorilere ekle"}</span>
            </button>`;
}

// Kalbe basınca ekler/çıkarır ve butonun görünümünü günceller.
// Sunucunun döndüğü isFavorite'e göre boyanır — iki sekme açıkken yerel tahmin şaşabilir,
// sunucunun cevabı her zaman doğrudur.
async function toggleFavorite(productId, button) {
    if (!authInfo()) {
        showToast("Favorilere eklemek için giriş yapmalısınız.", "warn");
        return;
    }

    const suanDolu = button.classList.contains("is-fav");
    button.disabled = true;

    try {
        const sonuc = suanDolu
            ? await apiDelete(`/favorites/${productId}`)
            : await apiPost(`/favorites/${productId}`, {});

        if (sonuc.isFavorite) favoriteIds.add(productId);
        else favoriteIds.delete(productId);

        // Aynı ürün sayfada birden fazla yerde olabilir (vitrin kartı + açık modal);
        // hepsi birden güncellenir ki ikisi farklı görünmesin.
        document.querySelectorAll(`[data-fav="${productId}"]`).forEach(b => {
            b.classList.toggle("is-fav", sonuc.isFavorite);
            b.setAttribute("aria-pressed", String(sonuc.isFavorite));
            const baslik = sonuc.isFavorite ? "Favorilerden çıkar" : "Favorilere ekle";
            b.title = baslik;
            const etiket = b.querySelector(".sr-only");
            if (etiket) etiket.textContent = baslik;
        });

        showToast(sonuc.message);
    } catch (err) {
        showToast(err.message, "warn");
    } finally {
        button.disabled = false;
    }
}

async function addToCart(productId, button) {
    if (!localStorage.getItem("token")) {
        showToast("Lütfen giriş yapınız.", "warn");
        return;
    }

    // Mikro-etkileşim: butonun genişliği içerik değişince "zıplamasın" diye tıklama
    // anındaki genişlik sabitlenir; ardından yükleniyor → başarılı durumları gösterilir.
    const oldHtml = button.innerHTML;
    button.style.minWidth = `${button.offsetWidth}px`;
    button.disabled = true;
    button.classList.remove("btn-success");
    button.classList.add("btn-loading");
    button.innerHTML = `<span class="btn-spinner" aria-hidden="true"></span>`;

    const reset = () => {
        button.classList.remove("btn-loading", "btn-success");
        button.innerHTML = oldHtml;
        button.style.minWidth = "";
        button.disabled = false;
    };

    try {
        const data = await apiPost("/cart", { productId: productId, quantity: 1 });
        await Promise.all([updateCartCount(), refreshCartDrawer()]);
        pulseCartBubble();

        // Sepete eklenebilecek stok bittiyse buton anında "Tükendi" olur
        if (data?.remaining === 0) {
            button.classList.remove("btn-loading");
            button.textContent = "Tükendi";
            button.style.minWidth = "";
            return;
        }

        button.classList.remove("btn-loading");
        button.classList.add("btn-success");
        button.innerHTML = `Eklendi ✔`;
        setTimeout(reset, 1300);
    } catch (err) {
        reset();
        showToast(err.message, "warn");
    }
}

/* ===== Off-Canvas (kayan) sepet ===== */
// Sepet, sayfadan ayrılmadan sağdan kayarak açılır. Tek bir panel gövdeye bir kez
// enjekte edilir; site.js'i içeren tüm sayfalarda (vitrin, ürün detay...) çalışır.

let cartDrawerBuilt = false;

function ensureCartDrawer() {
    if (cartDrawerBuilt) return;
    cartDrawerBuilt = true;

    const wrap = document.createElement("div");
    wrap.innerHTML = `
        <div id="cart-drawer-backdrop" class="drawer-backdrop" hidden></div>
        <aside id="cart-drawer" class="cart-drawer" aria-hidden="true" aria-label="Sepetim">
            <header class="cart-drawer-head">
                <h3>🛒 Sepetim</h3>
                <button type="button" id="cart-drawer-close" class="cart-drawer-close" aria-label="Kapat">×</button>
            </header>
            <div id="cart-drawer-body" class="cart-drawer-body"></div>
            <footer id="cart-drawer-foot" class="cart-drawer-foot" hidden>
                <div class="cart-drawer-total"><span>Ara toplam</span><strong id="cart-drawer-total">0 TL</strong></div>
                <a href="cart.html" class="add-btn cart-drawer-checkout">Sepete Git &amp; Öde →</a>
            </footer>
        </aside>`;
    document.body.appendChild(wrap);

    document.getElementById("cart-drawer-close").addEventListener("click", closeCartDrawer);
    document.getElementById("cart-drawer-backdrop").addEventListener("click", closeCartDrawer);
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") closeCartDrawer();
    });

    // Miktar / silme işlemleri panelin içinden yapılır; panel açık kalır.
    document.getElementById("cart-drawer-body").addEventListener("click", async (e) => {
        const qtyBtn = e.target.closest(".qty-btn");
        const removeBtn = e.target.closest(".remove-btn");
        if (!qtyBtn && !removeBtn) return;
        try {
            if (qtyBtn) {
                const qty = Number(qtyBtn.dataset.qty);
                if (qty < 1) await apiDelete(`/cart/${qtyBtn.dataset.id}`);
                else await apiPut(`/cart/${qtyBtn.dataset.id}`, { quantity: qty });
            } else {
                await apiDelete(`/cart/${removeBtn.dataset.id}`);
            }
            await Promise.all([updateCartCount(), refreshCartDrawer()]);
            // cart.html açıksa oradaki liste de tazelensin
            if (typeof loadCart === "function") loadCart();
        } catch (err) {
            showToast(err.message, "warn");
        }
    });
}

async function openCartDrawer() {
    if (!authInfo()) {
        showToast("Sepeti görmek için giriş yapınız.", "warn");
        return;
    }
    ensureCartDrawer();
    const drawer = document.getElementById("cart-drawer");
    const backdrop = document.getElementById("cart-drawer-backdrop");
    backdrop.hidden = false;
    drawer.setAttribute("aria-hidden", "false");
    requestAnimationFrame(() => {
        backdrop.classList.add("show");
        drawer.classList.add("open");
    });
    document.body.style.overflow = "hidden";
    // Önce iskelet göster, sonra doldur — algılanan hız artar.
    document.getElementById("cart-drawer-body").innerHTML = cartDrawerSkeleton();
    await refreshCartDrawer();
}

function closeCartDrawer() {
    if (!cartDrawerBuilt) return;
    const drawer = document.getElementById("cart-drawer");
    const backdrop = document.getElementById("cart-drawer-backdrop");
    if (drawer.getAttribute("aria-hidden") === "true") return;
    drawer.classList.remove("open");
    backdrop.classList.remove("show");
    drawer.setAttribute("aria-hidden", "true");
    document.body.style.overflow = "";
    setTimeout(() => { backdrop.hidden = true; }, 300);
}

function cartDrawerSkeleton() {
    const row = `<div class="cart-drawer-item skeleton-row">
        <div class="sk sk-thumb"></div>
        <div class="sk-lines"><div class="sk sk-line"></div><div class="sk sk-line short"></div></div>
    </div>`;
    return row.repeat(3);
}

// Panel kapalıysa hiçbir istek atmaz; açıkken çağrılırsa sepeti yeniden çizer.
async function refreshCartDrawer() {
    if (!cartDrawerBuilt) return;
    const drawer = document.getElementById("cart-drawer");
    if (drawer.getAttribute("aria-hidden") === "true") return;

    const body = document.getElementById("cart-drawer-body");
    const foot = document.getElementById("cart-drawer-foot");
    let items;
    try {
        items = await apiGet("/cart");
    } catch {
        body.innerHTML = `<p class="empty">Sepet yüklenemedi.</p>`;
        foot.hidden = true;
        return;
    }

    if (!items.length) {
        body.innerHTML = `<div class="cart-drawer-empty">
            <div class="cart-drawer-empty-ico">🛍️</div>
            <p>Sepetin henüz boş.</p>
            <a href="shop.html" class="add-btn">Alışverişe Başla</a>
        </div>`;
        foot.hidden = true;
        return;
    }

    let total = 0;
    body.innerHTML = items.map(item => {
        total += item.lineTotal;
        const priceLabel = item.product.hasDiscount
            ? `<span class="price-old">${item.product.price} TL</span> <span class="price-new">${item.unitPrice} TL</span>`
            : `${item.unitPrice} TL`;
        return `<div class="cart-drawer-item">
            <a href="product.html?id=${item.product.id}" class="cd-media">
                ${item.product.imageUrl ? `<img src="${esc(item.product.imageUrl)}" alt="">`
                    : `<span class="cd-noimg">${esc((item.product.name || "?")[0])}</span>`}
            </a>
            <div class="cd-info">
                <a href="product.html?id=${item.product.id}" class="cd-name">${esc(item.product.name)}</a>
                <span class="cd-price">${priceLabel}</span>
                <div class="qty-controls">
                    <button class="qty-btn" data-id="${item.id}" data-qty="${item.quantity - 1}">−</button>
                    <span>${item.quantity}</span>
                    <button class="qty-btn" data-id="${item.id}" data-qty="${item.quantity + 1}"
                        ${item.canIncrease ? "" : "disabled title='Daha fazla alınamıyor'"}>+</button>
                </div>
            </div>
            <div class="cd-right">
                <strong>${item.lineTotal} TL</strong>
                <button class="remove-btn" data-id="${item.id}" aria-label="Kaldır">🗑</button>
            </div>
        </div>`;
    }).join("");

    document.getElementById("cart-drawer-total").textContent = `${total} TL`;
    foot.hidden = false;
}

// Sepete ürün eklenince rozeti kısa bir "pop" animasyonuyla vurgular.
function pulseCartBubble() {
    const badge = document.getElementById("cart-count");
    if (!badge) return;
    badge.classList.remove("cart-pop");
    void badge.offsetWidth;   // reflow: animasyon üst üste tetiklenebilsin
    badge.classList.add("cart-pop");
}
