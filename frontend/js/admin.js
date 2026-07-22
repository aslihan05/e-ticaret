// Rol, kurcalanabilen localStorage yerine imzalı JWT'den doğrulanır
if (!requireAuth("Admin")) throw new Error("Yetkisiz erişim");

const STATUS = {
    0: { text: "Bekliyor", class: "status-pending" },
    1: { text: "Onaylandı", class: "status-approved" },
    2: { text: "Reddedildi", class: "status-rejected" },
    3: { text: "Kargoda", class: "status-shipped" },
    4: { text: "Teslim Edildi", class: "status-delivered" },
    5: { text: "İptal Edildi", class: "status-cancelled" }
};

const fmtDate = (d) => new Date(d).toLocaleString("tr-TR");

function showMessage(text, ok = true) {
    const el = document.getElementById("message");
    el.style.color = ok ? "green" : "#dc2626";
    el.textContent = text;
    setTimeout(() => { el.textContent = ""; }, 4000);
}

/* ===== Sekmeler ===== */

const LOADERS = {
    orders: loadAdminOrders,
    products: loadAdminProducts,
    discounts: loadAdminDiscounts,
    coupons: loadAdminCoupons,
    categories: loadAdminCategories,
    users: loadAdminUsers,
    logs: loadAdminLogs,
    analytics: loadAnalytics
};

document.querySelectorAll(".tab-btn").forEach(btn => {
    btn.addEventListener("click", () => {
        document.querySelectorAll(".tab-btn").forEach(b => b.classList.remove("active"));
        document.querySelectorAll(".tab-panel").forEach(p => p.classList.remove("active"));
        btn.classList.add("active");
        document.getElementById(`tab-${btn.dataset.tab}`).classList.add("active");
        // Analiz sekmesinden çıkılırsa canlı akış interval'i boşuna dönmesin (Böl ve Yönet).
        if (btn.dataset.tab !== "analytics") stopLiveFeed();
        LOADERS[btn.dataset.tab]();
    });
});

/* ===== Siparişler ===== */

// Duruma göre gösterilecek işlem butonları:
// Bekliyor -> Seçilileri Onayla / Tümünü Reddet, Onaylandı -> Kargoya Ver,
// Kargoda -> Teslim Edildi, Reddedildi/Teslim Edildi -> Sil
function orderActions(o) {
    if (o.status === 0) {
        return `<button class="btn-approve" data-id="${o.id}">Seçilileri Onayla</button>
                <button class="btn-reject" data-id="${o.id}">Tümünü Reddet</button>`;
    }
    if (o.status === 1) return `<button class="btn-status" data-id="${o.id}" data-status="3">Kargoya Ver</button>`;
    if (o.status === 3) return `<button class="btn-status" data-id="${o.id}" data-status="4">Teslim Edildi</button>`;
    return `<button class="btn-delete btn-order-del" data-id="${o.id}">Sil</button>`;
}

// adminOrders: tabloda/İl-İlçe görünümünde GÖSTERİLEN siparişler — arama kutusuyla
// filtrelenmiş olabilir. allOrders: müşteri modalının "toplam harcama / sipariş sayısı"
// özetini doğru çıkarabilmesi için tutulan FİLTRESİZ tam liste (bkz. ensureAdminOrders).
let adminOrders = [];
let allOrders = [];
// Filtresiz tam liste hiç çekilmediyse allOrders boştur; bu "hiç sipariş yok"
// ile karıştırılmasın diye ayrı bir bayrakla izlenir (bkz. ensureAdminOrders).
let allOrdersLoaded = false;

async function loadAdminOrders() {
    const q = document.getElementById("order-search")?.value.trim() ?? "";
    // Arama VERİTABANINDA yapılır: filtreli sipariş listesi backend'den gelir.
    const orders = await apiGet(`/admin/orders${q ? "?searchTerm=" + encodeURIComponent(q) : ""}`);
    adminOrders = orders;
    // Filtresiz çekildiyse modal için de sakla; aramalı sonuç modalın özetini bozmamalı.
    if (!q) { allOrders = orders; allOrdersLoaded = true; }
    const container = document.getElementById("admin-orders");

    if (orders.length === 0) {
        container.innerHTML = q
            ? "<p class='empty'>Aramayla eşleşen sipariş bulunamadı.</p>"
            : "<p class='empty'>Henüz sipariş yok.</p>";
        refreshRegionIfVisible();   // İl/İlçe paneli açıksa özeti de tazele
        return;
    }

    const rows = orders.map(o => {
        const s = STATUS[o.status];
        // Reddedilen kalemler toplama katılmaz; bekleyen siparişte kalemler seçilebilir
        const total = o.orderItems.filter(i => i.status !== 2)
            .reduce((sum, i) => sum + i.unitPrice * i.quantity, 0);
        const items = o.status === 0
            ? o.orderItems.map(i =>
                `<label class="item-pick"><input type="checkbox" class="item-check" value="${i.id}" checked> ${esc(i.product.name)} ×${i.quantity}</label>`).join("")
            : o.orderItems.map(i => i.status === 2
                ? `<span class="item-rejected">${esc(i.product.name)} ×${i.quantity}</span>`
                : `${esc(i.product.name)} ×${i.quantity}`).join(", ");
        // Telefon ve adres artık ayrı sütunlarda (eski siparişlerde boş olabilir)
        const phone = esc(o.phone || "-");
        const address = o.recipientName
            ? `<span class="addr-recipient">${esc(o.recipientName)}</span><br>${esc(o.city)}${o.district ? " / " + esc(o.district) : ""} — ${esc(o.address)}`
            : "-";
        return `<tr>
            <td>#${o.id}</td>
            <td><button class="customer-link" data-user-id="${o.userId}">${esc(o.user.username)}</button></td>
            <td>${phone}</td>
            <td class="addr-cell">${address}</td>
            <td>${fmtDate(o.createdAt)}</td>
            <td>${items}</td>
            <td>${total > 0 ? `${total} TL` : "-"}</td>
            <td><span class="status ${s.class}">${s.text}</span></td>
            <td>${orderActions(o)}</td>
        </tr>`;
    }).join("");

    container.innerHTML = `<table class="admin-table">
        <thead><tr><th>No</th><th>Müşteri</th><th>Telefon</th><th>Adres</th><th>Tarih</th><th>Ürünler</th><th>Toplam</th><th>Durum</th><th>İşlem</th></tr></thead>
        <tbody>${rows}</tbody></table>`;

    refreshRegionIfVisible();   // İl/İlçe paneli açıksa özetini de tazele
}

/* ===== Siparişleri İl > İlçe olarak grupla (Task #6) =====
   Eskiden tüm siparişler tarayıcıya çekilip burada gruplanıyordu; on binlerce siparişte
   bu hem ağı hem arayüzü boğar. Artık gruplama SUNUCUDA (SQL GROUP BY) yapılır: önce yalnızca
   İL özeti gelir; bir ile tıklanınca o ilin İLÇE kırılımı ayrıca (lazy) yüklenir. */

const money = (v) => `${(v ?? 0).toLocaleString("tr-TR")} TL`;
const sayi = (v) => (v ?? 0).toLocaleString("tr-TR");

// İl paneli görünürken (veya sipariş işlemi sonrası) özeti tazele
function refreshRegionIfVisible() {
    const panel = document.getElementById("admin-orders-grouped");
    if (panel.style.display !== "none") loadRegionStats();
}

async function loadRegionStats() {
    const container = document.getElementById("admin-orders-grouped");
    container.innerHTML = "<p class='empty'>Yükleniyor…</p>";

    let provinces;
    try {
        provinces = await apiGet("/admin/orders/region-stats");
    } catch (err) {
        container.innerHTML = `<p class='empty'>İl/İlçe özeti yüklenemedi: ${esc(err.message)}</p>`;
        return;
    }

    if (provinces.length === 0) {
        container.innerHTML = "<p class='empty'>Henüz sipariş yok.</p>";
        return;
    }

    // Yalnızca iller listelenir; ilçeler tıklanınca açılır (accordion). Tek DOM ağacı,
    // birkaç yüz il değil (Türkiye'de en çok 81), her biri hafif bir satır.
    container.innerHTML = `<div class="region-accordion">` + provinces.map(p => `
        <div class="region-il" data-city="${esc(p.city)}">
            <button type="button" class="region-il-head" data-city="${esc(p.city)}" aria-expanded="false">
                <span class="region-caret">▶</span>
                <span class="region-il-name">📍 ${esc(p.city)}</span>
                <span class="region-meta">${sayi(p.orderCount)} sipariş • ${sayi(p.districtCount)} ilçe • <strong>${money(p.totalRevenue)}</strong></span>
            </button>
            <div class="region-districts" data-city="${esc(p.city)}" data-loaded="false" style="display:none"></div>
        </div>`).join("") + `</div>`;
}

// İl başlığına tıklama: ilçe kırılımını (bir kez) yükle ve aç/kapat
document.getElementById("admin-orders-grouped").addEventListener("click", async (e) => {
    const head = e.target.closest(".region-il-head");
    if (!head) return;

    const city = head.dataset.city;
    const panel = document.querySelector(`.region-districts[data-city="${CSS.escape(city)}"]`);
    const caret = head.querySelector(".region-caret");
    const acik = panel.style.display !== "none";

    if (acik) {
        panel.style.display = "none";
        caret.textContent = "▶";
        head.setAttribute("aria-expanded", "false");
        return;
    }

    // Aç
    panel.style.display = "";
    caret.textContent = "▼";
    head.setAttribute("aria-expanded", "true");

    // İlçeler yalnızca ilk açılışta çekilir (sonra bellekte kalır)
    if (panel.dataset.loaded === "false") {
        panel.innerHTML = "<p class='region-loading'>Yükleniyor…</p>";
        try {
            const districts = await apiGet(`/admin/orders/region-stats/districts?city=${encodeURIComponent(city)}`);
            panel.innerHTML = districts.map(d => `
                <div class="region-ilce-row">
                    <span class="region-ilce-name">${esc(d.district)}</span>
                    <span class="region-ilce-meta">${sayi(d.orderCount)} sipariş • <strong>${money(d.totalRevenue)}</strong></span>
                </div>`).join("") || "<p class='region-loading'>İlçe bilgisi yok.</p>";
            panel.dataset.loaded = "true";
        } catch (err) {
            panel.innerHTML = `<p class='region-loading'>Yüklenemedi: ${esc(err.message)}</p>`;
        }
    }
});

// Tablo <-> İl/İlçe görünümü geçişi
document.querySelectorAll(".order-view-toggle .ov-btn").forEach(btn => {
    btn.addEventListener("click", () => {
        document.querySelectorAll(".order-view-toggle .ov-btn").forEach(b => b.classList.remove("active"));
        btn.classList.add("active");
        const region = btn.dataset.view === "region";
        document.getElementById("admin-orders").style.display = region ? "none" : "";
        document.getElementById("admin-orders-grouped").style.display = region ? "" : "none";
        // İl/İlçe görünümüne geçilince özet (lazy) yüklenir
        if (region) loadRegionStats();
    });
});

// Arama kutusu: her tuşta değil, yazma durunca (300 ms) sorgu atılır — böylece
// her harf için ayrı istek gitmez ama tablo neredeyse anında güncellenir.
let orderSearchTimer;
document.getElementById("order-search").addEventListener("input", () => {
    clearTimeout(orderSearchTimer);
    orderSearchTimer = setTimeout(loadAdminOrders, 300);
});

document.getElementById("admin-orders").addEventListener("click", async (e) => {
    const approve = e.target.closest(".btn-approve");
    const reject = e.target.closest(".btn-reject");
    const statusBtn = e.target.closest(".btn-status");
    const delBtn = e.target.closest(".btn-order-del");
    if (!approve && !reject && !statusBtn && !delBtn) return;

    try {
        let result;
        if (approve) {
            // İşaretli kalemler onaylanır, işaretsiz kalanlar reddedilir
            const ids = [...approve.closest("tr").querySelectorAll(".item-check:checked")]
                .map(cb => Number(cb.value));
            result = await apiPut(`/admin/orders/${approve.dataset.id}/decide`, { approvedItemIds: ids });
        } else if (reject) {
            result = await apiPut(`/admin/orders/${reject.dataset.id}/reject`, {});
        } else if (statusBtn) {
            result = await apiPut(`/admin/orders/${statusBtn.dataset.id}/status`,
                { status: Number(statusBtn.dataset.status) });
        } else {
            if (!confirm("Bu sipariş kalıcı olarak silinsin mi?")) return;
            result = await apiDelete(`/admin/orders/${delBtn.dataset.id}`);
        }
        showMessage(result?.message ?? "Tamam.");
        loadAdminOrders();
    } catch (err) {
        showMessage(err.message, false);
    }
});

/* ===== Müşteri bilgileri penceresi ===== */

// Modal, Kullanıcılar sekmesinden de açılabiliyor; oradan gelindiğinde Siparişler
// sekmesi hiç yüklenmemiş olabilir. Sipariş listesi bu yüzden gerektiğinde çekilir.
async function ensureAdminOrders() {
    // Modal her zaman FİLTRESİZ tam listeyi kullanır: arama kutusu tabloyu daraltsa bile
    // müşterinin toplam harcaması/siparişleri eksiksiz görünsün.
    if (!allOrdersLoaded) {
        allOrders = await apiGet("/admin/orders");
        allOrdersLoaded = true;
    }
}

// Bir müşterinin tüm siparişlerinden özet bilgi çıkarıp modalda gösterir.
// fallbackUser: siparişi olmayan kullanıcıda kimlik bilgisi siparişlerden okunamaz,
// Kullanıcılar sekmesi kendi kaydını verir.
// Modalda gösterilen müşteri ve siparişleri; filtre çubuğu bunlar üzerinde çalışır.
let custModalUser = null;
let custModalOrders = [];

async function openCustomerModal(userId, fallbackUser = null) {
    await ensureAdminOrders();

    custModalOrders = allOrders.filter(o => o.userId === userId);
    custModalUser = custModalOrders[0]?.user ?? fallbackUser;
    if (!custModalUser) return;

    const user = custModalUser;
    const orders = custModalOrders;

    // Üstteki özet TÜM zamanları kapsar (filtreden etkilenmez): iptaller harcamaya sayılmaz.
    const spent = orders
        .flatMap(o => o.orderItems)
        .filter(i => i.status !== 2)
        .reduce((sum, i) => sum + i.unitPrice * i.quantity, 0);

    // İletişim bilgisi önce profilden okunur (Kullanıcılar sekmesinden gelindiyse elimizde
    // vardır), yoksa en son teslimat bilgisinden. Profildeki kayıt daha günceldir.
    const withAddr = orders.find(o => o.recipientName);
    const eposta = fallbackUser?.email;
    const telefon = fallbackUser?.phone || withAddr?.phone;
    const adres = fallbackUser?.address
        || (withAddr ? `${withAddr.city}${withAddr.district ? " / " + withAddr.district : ""} — ${withAddr.address}` : null);

    const satir = (etiket, deger) =>
        deger ? `<p><strong>${etiket}:</strong> ${esc(deger)}</p>` : "";

    // Sabit bilgi + filtre çubuğu bir kez basılır; sipariş listesi filtreye göre yeniden çizilir.
    const statusOptions = ['<option value="">Tüm durumlar</option>']
        .concat(Object.entries(STATUS).map(([k, v]) => `<option value="${k}">${v.text}</option>`))
        .join("");

    document.getElementById("customer-modal-body").innerHTML = `
        <h3>${esc(user.username)}</h3>
        <p><strong>Müşteri No:</strong> ${user.id}</p>
        <p><strong>Üyelik:</strong> ${fmtDate(user.createdAt)}</p>
        <p><strong>Sipariş sayısı:</strong> ${orders.length}</p>
        <p><strong>Toplam harcama:</strong> ${spent} TL</p>
        ${satir("Alıcı", withAddr?.recipientName)}
        ${satir("E-posta", eposta)}
        ${satir("Telefon", telefon)}
        ${satir("Adres", adres)}
        <h4>Siparişleri</h4>
        <div class="cust-filter-bar">
            <select id="cust-f-status" title="Duruma göre süz">${statusOptions}</select>
            <label>Başlangıç <input type="date" id="cust-f-from"></label>
            <label>Bitiş <input type="date" id="cust-f-to"></label>
            <button type="button" id="cust-f-clear" class="link-btn">Temizle</button>
        </div>
        <p id="cust-order-count" class="muted"></p>
        <ul class="modal-orders" id="cust-order-list"></ul>`;

    // Filtre kontrollerini bağla (modal içeriği yeni basıldığı için her açılışta yeniden bağlanır)
    document.getElementById("cust-f-status").addEventListener("change", renderCustomerOrders);
    document.getElementById("cust-f-from").addEventListener("change", renderCustomerOrders);
    document.getElementById("cust-f-to").addEventListener("change", renderCustomerOrders);
    document.getElementById("cust-f-clear").addEventListener("click", () => {
        document.getElementById("cust-f-status").value = "";
        document.getElementById("cust-f-from").value = "";
        document.getElementById("cust-f-to").value = "";
        renderCustomerOrders();
    });

    renderCustomerOrders();
    document.getElementById("customer-modal").style.display = "flex";
}

// Müşterinin siparişlerini duruma ve tarih aralığına göre süzüp listeler.
function renderCustomerOrders() {
    const durum = document.getElementById("cust-f-status").value;
    const fromVal = document.getElementById("cust-f-from").value;
    const toVal = document.getElementById("cust-f-to").value;
    const fromTs = fromVal ? new Date(fromVal + "T00:00:00").getTime() : null;
    const toTs = toVal ? new Date(toVal + "T23:59:59").getTime() : null;

    const suzulmus = custModalOrders.filter(o => {
        if (durum !== "" && String(o.status) !== durum) return false;
        const t = new Date(o.createdAt).getTime();
        if (fromTs != null && t < fromTs) return false;
        if (toTs != null && t > toTs) return false;
        return true;
    });

    const list = document.getElementById("cust-order-list");
    const sayac = document.getElementById("cust-order-count");

    if (custModalOrders.length === 0) {
        sayac.textContent = "";
        list.innerHTML = `<li class="empty">Henüz siparişi yok.</li>`;
        return;
    }

    sayac.textContent = `${suzulmus.length} / ${custModalOrders.length} sipariş gösteriliyor`;
    list.innerHTML = suzulmus.length > 0
        ? suzulmus.map(o => {
            const s = STATUS[o.status];
            return `<li>#${o.id} — <span class="status ${s.class}">${s.text}</span> <span class="muted">${fmtDate(o.createdAt)}</span></li>`;
        }).join("")
        : `<li class="empty">Bu filtreye uyan sipariş yok.</li>`;
}

document.getElementById("admin-orders").addEventListener("click", (e) => {
    const link = e.target.closest(".customer-link");
    if (link) openCustomerModal(Number(link.dataset.userId));
});

const customerModal = document.getElementById("customer-modal");
document.getElementById("customer-modal-close").addEventListener("click", () => {
    customerModal.style.display = "none";
});
customerModal.addEventListener("click", (e) => {
    if (e.target === customerModal) customerModal.style.display = "none";
});


/* ===== Ürünün sipariş tarihçesi ===== */

// Bir ürünün hangi siparişte, kim tarafından, ne zaman, kaç adet alındığını gösterir.
// Analiz sekmesindeki toplam satış rakamının satır satır dökümü.
let currentHistoryProductId = null;

async function openProductHistory(productId) {
    currentHistoryProductId = productId;
    // Filtre kutularını sıfırla ve göster
    ["hf-customer", "hf-from", "hf-to", "hf-min-qty", "hf-status"].forEach(id => {
        document.getElementById(id).value = "";
    });
    document.getElementById("history-filter-bar").style.display = "";
    document.getElementById("product-history-title").textContent = "";
    document.getElementById("product-history-modal").style.display = "flex";
    await renderProductHistory();
}

// Filtre kutularından generic FilterRule listesi (ürün geçmişi alanları OrderItem üzerinden)
function buildHistoryFilters() {
    const filters = [];
    const cust = document.getElementById("hf-customer").value.trim();
    const from = document.getElementById("hf-from").value;
    const to = document.getElementById("hf-to").value;
    const minQty = document.getElementById("hf-min-qty").value;
    const status = document.getElementById("hf-status").value;

    if (cust) filters.push({ field: "Order.User.Username", op: "contains", value: cust });
    if (from) filters.push({ field: "Order.CreatedAt", op: "from", value: from });
    if (to) filters.push({ field: "Order.CreatedAt", op: "to", value: to });
    if (minQty) filters.push({ field: "Quantity", op: "gte", value: minQty });
    if (status) filters.push({ field: "Status", op: "eq", value: status });
    return filters;
}

async function renderProductHistory() {
    const body = document.getElementById("product-history-body");
    body.innerHTML = "<p class='empty'>Yükleniyor…</p>";

    try {
        const filters = buildHistoryFilters();
        const h = await apiPost(`/admin/products/${currentHistoryProductId}/orders/filter`, { filters });

        document.getElementById("product-history-title").textContent = h.productName;

        if (h.items.length === 0) {
            body.innerHTML = `<p class="empty">Filtreyle eşleşen kayıt yok.</p>`;
            return;
        }

        const rows = h.items.map(i => {
            // Sipariş iptal/reddedilmişse kalem "Bekliyor" kalır ama tarihçede bunu göstermek
            // yanıltıcı olur — bu durumda siparişin genel durumu gösterilir.
            const iptalVeyaRed = i.orderStatus === 5 || i.orderStatus === 2;
            const s = STATUS[iptalVeyaRed ? i.orderStatus : i.itemStatus];
            const soluk = iptalVeyaRed || i.itemStatus === 2;
            return `<tr class="${soluk ? "row-inactive" : ""}">
                <td><a href="#" class="customer-link" data-user-id="${i.customerId}">${esc(i.customer)}</a></td>
                <td>#${i.orderId}</td>
                <td>${fmtDate(i.createdAt)}</td>
                <td>${i.quantity}</td>
                <td>${i.unitPrice} TL</td>
                <td>${i.lineTotal} TL</td>
                <td><span class="status ${s.class}">${s.text}</span></td>
            </tr>`;
        }).join("");

        body.innerHTML = `
            <div class="history-stats">
                <div class="stat-card"><span>Sipariş sayısı</span><strong>${h.totalOrders}</strong></div>
                <div class="stat-card"><span>Satılan adet</span><strong>${h.totalSoldQty}</strong></div>
                <div class="stat-card"><span>Ciro</span><strong>${h.totalRevenue} TL</strong></div>
                <div class="stat-card"><span>Kâr</span><strong class="${h.totalProfit >= 0 ? "profit-pos" : "profit-neg"}">${h.totalProfit} TL</strong></div>
            </div>
            <p class="tab-hint">Özet rakamlar yalnızca <strong>onaylanmış</strong> kalemleri sayar; reddedilenler soluk gösterilir. Filtre sonucundaki kayıtlar üzerinden hesaplanır.</p>
            <table class="admin-table">
                <thead><tr><th>Müşteri</th><th>Sipariş</th><th>Tarih</th><th>Adet</th><th>Birim</th><th>Tutar</th><th>Durum</th></tr></thead>
                <tbody>${rows}</tbody>
            </table>`;
    } catch (err) {
        body.innerHTML = `<p class="empty">Tarihçe yüklenemedi: ${esc(err.message)}</p>`;
    }
}

// Ürün geçmişi filtre dinleyicileri
let historyFilterTimer;
["hf-customer", "hf-min-qty"].forEach(id => {
    document.getElementById(id).addEventListener("input", () => {
        clearTimeout(historyFilterTimer);
        historyFilterTimer = setTimeout(renderProductHistory, 250);
    });
});
["hf-from", "hf-to", "hf-status"].forEach(id => {
    document.getElementById(id).addEventListener("change", renderProductHistory);
});
document.getElementById("hf-clear").addEventListener("click", () => {
    ["hf-customer", "hf-from", "hf-to", "hf-min-qty", "hf-status"].forEach(id => {
        document.getElementById(id).value = "";
    });
    renderProductHistory();
});

const historyModal = document.getElementById("product-history-modal");
document.getElementById("product-history-close").addEventListener("click", () => {
    historyModal.style.display = "none";
});
historyModal.addEventListener("click", (e) => {
    if (e.target === historyModal) historyModal.style.display = "none";
});

/* ===== Ürünler ===== */

let adminProducts = [];
let editingProductId = null;

async function fillCategorySelect() {
    const cats = await apiGet("/categories");

    // Ana kategoriler, hemen altında girintili çocukları — hangisinin alt kategori
    // olduğu listede net görünsün diye
    const parents = cats.filter(c => c.parentId == null);
    const options = parents.map(p => {
        const children = cats.filter(c => c.parentId === p.id)
            .map(ch => `<option value="${ch.id}">&nbsp;&nbsp;↳ ${esc(ch.name)}</option>`)
            .join("");
        return `<option value="${p.id}">${esc(p.name)}</option>` + children;
    }).join("");

    document.getElementById("p-category").innerHTML = options;
}

function priceCell(p) {
    return p.hasDiscount
        ? `<span class="price-old">${p.price} TL</span> <strong class="price-new">${p.discountedPrice} TL</strong>`
        : `${p.price} TL`;
}

// Kâr = efektif satış fiyatı (indirimliyse indirimli) - maliyet
function profitCell(p) {
    if (p.cost == null) return `<span class="muted">-</span>`;
    const eff = p.hasDiscount ? p.discountedPrice : p.price;
    const profit = eff - p.cost;
    return `<span class="${profit >= 0 ? "profit-pos" : "profit-neg"}">${profit.toFixed(2)} TL</span>`;
}

async function loadAdminProducts() {
    await fillCategorySelect();
    await fillProductFilterCategory();
    // Ürünler artık her zaman generic filtre ucundan çekilir; filtre boşsa tüm katalog gelir.
    await applyProductFilter();
}

// Filtre çubuğundaki kategori seçicisini (ana + girintili alt kategoriler) doldurur
async function fillProductFilterCategory() {
    const cats = await apiGet("/categories");
    const parents = cats.filter(c => c.parentId == null);
    const options = parents.map(p => {
        const children = cats.filter(c => c.parentId === p.id)
            .map(ch => `<option value="${ch.id}">&nbsp;&nbsp;↳ ${esc(ch.name)}</option>`).join("");
        return `<option value="${p.id}">${esc(p.name)}</option>` + children;
    }).join("");
    document.getElementById("pf-category").innerHTML =
        `<option value="">Tüm kategoriler</option>` + options;
}

// Filtre kutularından generic FilterRule listesi kurar (boş kutular kural üretmez)
function buildProductFilters() {
    const filters = [];
    const name = document.getElementById("pf-name").value.trim();
    const minP = document.getElementById("pf-min-price").value;
    const maxP = document.getElementById("pf-max-price").value;
    const minS = document.getElementById("pf-min-stock").value;
    const cat = document.getElementById("pf-category").value;
    const active = document.getElementById("pf-active").value;

    if (name) filters.push({ field: "Name", op: "contains", value: name });
    if (minP) filters.push({ field: "Price", op: "gte", value: minP });
    if (maxP) filters.push({ field: "Price", op: "lte", value: maxP });
    if (minS) filters.push({ field: "Stock", op: "gte", value: minS });
    if (cat) filters.push({ field: "CategoryId", op: "eq", value: cat });
    if (active) filters.push({ field: "IsActive", op: "eq", value: active });
    return filters;
}

async function applyProductFilter() {
    const filters = buildProductFilters();
    // Stabil sıra için Id'ye göre sırala (aksi halde sayfalar arası sıra değişebilir)
    adminProducts = await apiPost("/products/filter", { filters, sortBy: "Id", sortDir: "asc" });
    renderProductRows(adminProducts);
}

function renderProductRows(products) {
    const container = document.getElementById("admin-products");

    if (products.length === 0) {
        container.innerHTML = "<p class='empty'>Filtreyle eşleşen ürün yok.</p>";
        return;
    }

    const rows = products.map(p => {
        // Geçmiş her ürün için görünür (pasif ürünün de satış tarihçesi anlamlıdır)
        const history = `<button class="btn-history" data-id="${p.id}" title="Sipariş tarihçesi">📜 Geçmiş</button>`;
        const actions = p.isActive
            ? `<button class="btn-edit" data-id="${p.id}">Düzenle</button>
               ${history}
               <button class="btn-delete" data-id="${p.id}" ${p.stock > 0 ? `title="Stokta ${p.stock} adet var — önce stoğu 0 yapmalısın" disabled` : ""}>Pasifleştir</button>`
            : `${history}
               <button class="btn-activate" data-id="${p.id}">Aktifleştir</button>`;
        return `<tr class="${p.isActive ? "" : "row-inactive"}">
            <td>${p.imageUrl ? `<img src="${esc(p.imageUrl)}" alt="">` : "-"}</td>
            <td>${esc(p.name)}${p.isActive ? "" : ' <span class="badge-inactive">pasif</span>'}</td>
            <td>${priceCell(p)}</td>
            <td>${p.cost != null ? p.cost + " TL" : "-"}</td>
            <td>${profitCell(p)}</td>
            <td>
                <input type="number" class="stock-input" min="0" value="${p.stock}">
                <button class="btn-stock" data-id="${p.id}" title="Stoku kaydet">✓</button>
            </td>
            <td>${p.category ? (p.category.parentName ? esc(p.category.parentName) + " › " : "") + esc(p.category.name) : "-"}</td>
            <td>${actions}</td>
        </tr>`;
    }).join("");

    container.innerHTML = `<table class="admin-table">
        <thead><tr><th></th><th>Ürün</th><th>Fiyat</th><th>Maliyet</th><th>Kâr</th><th>Stok</th><th>Kategori</th><th>İşlem</th></tr></thead>
        <tbody>${rows}</tbody></table>`;
}

// Ürün filtresi: metin/sayı kutuları yazma durunca (250 ms), seçimler anında uygulanır
let productFilterTimer;
["pf-name", "pf-min-price", "pf-max-price", "pf-min-stock"].forEach(id => {
    document.getElementById(id).addEventListener("input", () => {
        clearTimeout(productFilterTimer);
        productFilterTimer = setTimeout(applyProductFilter, 250);
    });
});
["pf-category", "pf-active"].forEach(id => {
    document.getElementById(id).addEventListener("change", applyProductFilter);
});
document.getElementById("pf-clear").addEventListener("click", () => {
    ["pf-name", "pf-min-price", "pf-max-price", "pf-min-stock", "pf-category", "pf-active"]
        .forEach(id => { document.getElementById(id).value = ""; });
    applyProductFilter();
});

document.getElementById("product-form").addEventListener("submit", async (e) => {
    e.preventDefault();

    // İndirim bilgisi bu formda yok; güncellemede mevcut indirim kaybolmasın diye korunur
    const orig = editingProductId ? adminProducts.find(x => x.id === editingProductId) : null;

    const dto = {
        name: document.getElementById("p-name").value,
        description: document.getElementById("p-desc").value,
        price: Number(document.getElementById("p-price").value),
        cost: document.getElementById("p-cost").value ? Number(document.getElementById("p-cost").value) : null,
        stock: Number(document.getElementById("p-stock").value),
        imageUrl: document.getElementById("p-image").value || null,
        imageUrls: document.getElementById("p-images").value
            .split("\n").map(s => s.trim()).filter(Boolean),
        categoryId: Number(document.getElementById("p-category").value),
        discountPrice: orig?.discountPrice ?? null,
        discountStart: orig?.discountStart ?? null,
        discountEnd: orig?.discountEnd ?? null
    };

    try {
        if (editingProductId) {
            await apiPut(`/products/${editingProductId}`, dto);
            showMessage("Ürün güncellendi.");
        } else {
            await apiPost("/products", dto);
            showMessage("Ürün eklendi.");
        }
        resetProductForm();
        loadAdminProducts();
    } catch (err) {
        showMessage(err.message, false);
    }
});

function resetProductForm() {
    editingProductId = null;
    document.getElementById("product-form").reset();
    document.getElementById("p-images").value = "";
    document.getElementById("p-submit").textContent = "Ürün Ekle";
    document.getElementById("p-cancel").style.display = "none";
    document.getElementById("product-form").classList.remove("editing");
}

document.getElementById("p-cancel").addEventListener("click", resetProductForm);

document.getElementById("admin-products").addEventListener("click", async (e) => {
    const editBtn = e.target.closest(".btn-edit");
    const deleteBtn = e.target.closest(".btn-delete");
    const stockBtn = e.target.closest(".btn-stock");
    const activateBtn = e.target.closest(".btn-activate");
    const historyBtn = e.target.closest(".btn-history");

    if (historyBtn) {
        openProductHistory(Number(historyBtn.dataset.id));
        return;
    }

    if (editBtn) {
        const p = adminProducts.find(x => x.id === Number(editBtn.dataset.id));
        editingProductId = p.id;
        document.getElementById("p-name").value = p.name;
        document.getElementById("p-desc").value = p.description ?? "";
        document.getElementById("p-price").value = p.price;
        document.getElementById("p-cost").value = p.cost ?? "";
        document.getElementById("p-stock").value = p.stock;
        document.getElementById("p-image").value = p.imageUrl ?? "";
        document.getElementById("p-images").value = (p.imageUrls ?? []).join("\n");
        document.getElementById("p-category").value = p.categoryId;
        document.getElementById("p-submit").textContent = "Güncelle";
        document.getElementById("p-cancel").style.display = "inline-block";

        // Formu görünür yap ve vurgula (sayfa değil, form hedef alınır)
        const form = document.getElementById("product-form");
        form.classList.add("editing");
        form.scrollIntoView({ behavior: "smooth", block: "center" });
        document.getElementById("p-name").focus();
    }

    if (deleteBtn) {
        try {
            await apiDelete(`/products/${deleteBtn.dataset.id}`);
            showMessage("Ürün pasife alındı; istersen tablodan tekrar aktifleştirebilirsin.");
            loadAdminProducts();
        } catch (err) {
            showMessage(err.message, false);
        }
    }

    if (stockBtn) {
        const input = stockBtn.closest("td").querySelector(".stock-input");
        try {
            const r = await apiPut(`/products/${stockBtn.dataset.id}/stock`, { stock: Number(input.value) });
            showMessage(r.message);
        } catch (err) {
            showMessage(err.message, false);
        }
    }

    if (activateBtn) {
        try {
            const r = await apiPut(`/products/${activateBtn.dataset.id}/activate`, {});
            showMessage(r.message);
            loadAdminProducts();
        } catch (err) {
            showMessage(err.message, false);
        }
    }
});

/* ===== İndirimler ===== */

// Kategori indirimi / toplu indirim araçları arasındaki alt sekme geçişi
document.querySelectorAll(".subtab-btn").forEach(btn => {
    btn.addEventListener("click", () => {
        document.querySelectorAll(".subtab-btn").forEach(b => b.classList.remove("active"));
        document.querySelectorAll(".subtab-panel").forEach(p => p.classList.remove("active"));
        btn.classList.add("active");
        document.getElementById(`disc-subtab-${btn.dataset.subtab}`).classList.add("active");
    });
});

// datetime-local input "2026-07-15T10:00" ister; API'den "2026-07-15T10:00:00" gelir
// Tarihler backend'de/veritabanında UTC tutulur, admin ise yerel saatle çalışır.
// Bu iki fonksiyon sınırdaki çeviriyi yapar — ham string kesmek (d.slice(0,16))
// UTC'yi yerel sanmak demekti ve saatleri 3 saat kaydırıyordu.

// UTC ISO ("2026-07-20T10:48:00Z") -> datetime-local input değeri (yerel saat, "2026-07-20T13:48")
const toInputDate = (d) => {
    if (!d) return "";
    const dt = new Date(d);
    // getTimezoneOffset dakika cinsinden ve UTC'ye göre TERS işaretlidir; çıkarınca yerel saat kalır
    return new Date(dt.getTime() - dt.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
};

// datetime-local input değeri (yerel saat) -> UTC ISO. Boşsa null.
const toUtcIso = (v) => v ? new Date(v).toISOString() : null;

async function loadAdminDiscounts() {
    adminProducts = await apiGet("/products?includeInactive=true");

    // Kategori-yüzde indirim aracının seçeneklerini doldur
    const cats = await apiGet("/categories");
    document.getElementById("cd-category").innerHTML =
        cats.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join("");

    const container = document.getElementById("admin-discounts");
    const active = adminProducts.filter(p => p.isActive);

    if (active.length === 0) {
        container.innerHTML = "<p class='empty'>Aktif ürün yok.</p>";
        return;
    }

    const rows = active.map(p => {
        const pct = p.hasDiscount ? Math.round((1 - p.discountedPrice / p.price) * 100) : null;
        return `<tr data-name="${esc(p.name.toLowerCase())}" data-price="${p.price}">
        <td><input type="checkbox" class="disc-check" value="${p.id}"></td>
        <td>${esc(p.name)}</td>
        <td>${p.price} TL</td>
        <td><input type="number" class="disc-percent" min="1" max="99" placeholder="%"></td>
        <td><input type="number" class="disc-price" min="0" step="0.01" placeholder="İndirimli fiyat" value="${p.discountPrice ?? ""}"></td>
        <td><input type="datetime-local" class="disc-start" value="${toInputDate(p.discountStart)}"></td>
        <td><input type="datetime-local" class="disc-end" value="${toInputDate(p.discountEnd)}"></td>
        <td>${p.hasDiscount ? `<span class="status status-approved">-%${pct}</span>` : "-"}</td>
        <td>
            <button class="btn-edit btn-disc-save" data-id="${p.id}">Kaydet</button>
            <button class="btn-delete btn-disc-clear" data-id="${p.id}" title="İndirimi kaldır">🗑</button>
        </td>
    </tr>`;
    }).join("");

    container.innerHTML = `<table class="admin-table">
        <thead><tr><th></th><th>Ürün</th><th>Fiyat</th><th>%</th><th>İndirimli Fiyat</th><th>Başlangıç</th><th>Bitiş</th><th>Durum</th><th>İşlem</th></tr></thead>
        <tbody>${rows}</tbody></table>`;

    // Arama kutusundaki metne göre satırları filtrele
    const q = document.getElementById("disc-search").value.trim().toLowerCase();
    if (q) filterDiscountRows(q);
}

document.getElementById("admin-discounts").addEventListener("click", async (e) => {
    const saveBtn = e.target.closest(".btn-disc-save");
    const clearBtn = e.target.closest(".btn-disc-clear");
    if (!saveBtn && !clearBtn) return;

    const btn = saveBtn ?? clearBtn;
    let dto = { discountPrice: null, discountStart: null, discountEnd: null };

    if (saveBtn) {
        const tr = btn.closest("tr");
        const price = tr.querySelector(".disc-price").value;
        if (!price) {
            showMessage("İndirimli fiyat gir (kaldırmak için 🗑 kullan).", false);
            return;
        }
        dto = {
            discountPrice: Number(price),
            discountStart: toUtcIso(tr.querySelector(".disc-start").value),
            discountEnd: toUtcIso(tr.querySelector(".disc-end").value)
        };
    }

    try {
        const r = await apiPut(`/products/${btn.dataset.id}/discount`, dto);
        showMessage(r.message);
        loadAdminDiscounts();
    } catch (err) {
        showMessage(err.message, false);
    }
});

// Kategori bazlı yüzde indirim: seçili kategorideki tüm aktif ürünlere uygular
document.getElementById("cd-apply").addEventListener("click", async () => {
    const categoryId = document.getElementById("cd-category").value;
    const percent = Number(document.getElementById("cd-percent").value);
    if (!categoryId || !percent) {
        showMessage("Kategori ve indirim yüzdesi gir.", false);
        return;
    }
    const dto = {
        percent,
        discountStart: toUtcIso(document.getElementById("cd-start").value),
        discountEnd: toUtcIso(document.getElementById("cd-end").value)
    };
    try {
        const r = await apiPut(`/products/category/${categoryId}/discount`, dto);
        showMessage(r.message);
        loadAdminDiscounts();
    } catch (err) {
        showMessage(err.message, false);
    }
});

// Bir satıra yüzde girilince o ürünün indirimli fiyatını otomatik hesapla
document.getElementById("admin-discounts").addEventListener("input", (e) => {
    const pctInput = e.target.closest(".disc-percent");
    if (!pctInput) return;
    const tr = pctInput.closest("tr");
    const price = Number(tr.dataset.price);
    const pct = Number(pctInput.value);
    if (pct > 0 && pct < 100) {
        tr.querySelector(".disc-price").value = (price * (100 - pct) / 100).toFixed(2);
    }
});

// İndirim tablosunda ürün arama filtresi
function filterDiscountRows(q) {
    document.querySelectorAll("#admin-discounts tr[data-name]").forEach(tr => {
        tr.style.display = tr.dataset.name.includes(q) ? "" : "none";
    });
}
document.getElementById("disc-search").addEventListener("input", (e) => {
    filterDiscountRows(e.target.value.trim().toLowerCase());
});

// "Görünenleri seç": arama sonucu görünen satırların kutularını işaretle/kaldır
document.getElementById("disc-select-all").addEventListener("change", (e) => {
    document.querySelectorAll("#admin-discounts tr[data-name]").forEach(tr => {
        if (tr.style.display !== "none") tr.querySelector(".disc-check").checked = e.target.checked;
    });
});

// Seçili ürünlere topluca indirim uygula (clear=true ise kaldır)
async function applyBulkDiscount(clear) {
    const ids = [...document.querySelectorAll("#admin-discounts .disc-check:checked")].map(c => Number(c.value));
    if (ids.length === 0) { showMessage("Önce ürün seç.", false); return; }

    const percent = clear ? null : Number(document.getElementById("bulk-percent").value);
    if (!clear && !percent) { showMessage("İndirim yüzdesi gir.", false); return; }

    const dto = {
        ids,
        percent,
        discountStart: toUtcIso(document.getElementById("bulk-start").value),
        discountEnd: toUtcIso(document.getElementById("bulk-end").value)
    };
    try {
        const r = await apiPut("/products/discount/bulk", dto);
        showMessage(r.message);
        document.getElementById("disc-select-all").checked = false;
        loadAdminDiscounts();
    } catch (err) {
        showMessage(err.message, false);
    }
}
document.getElementById("bulk-apply").addEventListener("click", () => applyBulkDiscount(false));
document.getElementById("bulk-clear").addEventListener("click", () => applyBulkDiscount(true));

/* ===== Kategoriler ===== */

let adminCategories = [];

async function loadAdminCategories() {
    const cats = await apiGet("/categories");
    adminCategories = cats;   // sıra/yeniden adlandırma için gereken mevcut değerler

    // Ana kategoriler (üstü olmayanlar) hem ağaçta kök, hem "üst kategori" seçeneği olur
    const parents = cats.filter(c => c.parentId == null);
    document.getElementById("category-parent").innerHTML =
        `<option value="">Ana kategori (üst yok)</option>` +
        parents.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join("");

    // Bir kategoriyi başka kategorinin altına taşıma seçicisi. Menü iki seviyeli olduğu için
    // yalnızca ana kategoriler üst olabilir; kendisi ve alt kategorisi olanlar listeden çıkarılır.
    const moveSelect = (c) => {
        const hasChildren = cats.some(x => x.parentId === c.id);
        if (hasChildren) {
            // Alt kategorisi olan bir kategori taşınamaz (backend de bunu reddeder)
            return `<span class="cat-move-note" title="Alt kategorisi olan kategori taşınamaz">ana kategori</span>`;
        }
        const options = parents
            .filter(p => p.id !== c.id)
            .map(p => `<option value="${p.id}" ${c.parentId === p.id ? "selected" : ""}>${esc(p.name)}</option>`)
            .join("");
        return `<select class="cat-parent-select" data-id="${c.id}" title="Üst kategorisini değiştir">
            <option value="" ${c.parentId == null ? "selected" : ""}>— ana kategori —</option>
            ${options}
        </select>`;
    };

    const row = (c, sub) => `<li class="${sub ? "subcat" : ""}">
        <input type="number" class="sort-input cat-sort-input" data-id="${c.id}" value="${c.sortOrder}" title="Menüdeki sırası">
        <span class="cat-name">${sub ? "↳ " : ""}${esc(c.name)}</span>
        ${moveSelect(c)}
        <span>
            <button class="btn-edit btn-rename" data-id="${c.id}" data-name="${esc(c.name)}">Yeniden Adlandır</button>
            <button class="btn-delete" data-id="${c.id}">Sil</button>
        </span>
    </li>`;

    // Her ana kategorinin altına çocukları yaz
    document.getElementById("admin-categories").innerHTML = parents.map(p =>
        row(p, false) + cats.filter(c => c.parentId === p.id).map(ch => row(ch, true)).join("")
    ).join("");
}

document.getElementById("category-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    try {
        const parentVal = document.getElementById("category-parent").value;
        await apiPost("/categories", {
            name: document.getElementById("category-name").value,
            parentId: parentVal ? Number(parentVal) : null
        });
        showMessage("Kategori eklendi.");
        document.getElementById("category-form").reset();
        loadAdminCategories();
    } catch (err) {
        showMessage(err.message, false);
    }
});

document.getElementById("admin-categories").addEventListener("click", async (e) => {
    const renameBtn = e.target.closest(".btn-rename");
    const deleteBtn = e.target.closest(".btn-delete");

    if (renameBtn) {
        const newName = prompt("Kategorinin yeni adı:", renameBtn.dataset.name);
        if (!newName || newName === renameBtn.dataset.name) return;
        // Mevcut sıra ve üst kategori korunsun diye tam dto gönderilir
        const c = adminCategories.find(x => x.id === Number(renameBtn.dataset.id));
        try {
            await apiPut(`/categories/${renameBtn.dataset.id}`, {
                name: newName,
                parentId: c?.parentId ?? null,
                sortOrder: c?.sortOrder ?? 0
            });
            showMessage("Kategori güncellendi.");
            loadAdminCategories();
        } catch (err) {
            showMessage(err.message, false);
        }
    }

    if (deleteBtn) {
        try {
            await apiDelete(`/categories/${deleteBtn.dataset.id}`);
            showMessage("Kategori silindi.");
            loadAdminCategories();
        } catch (err) {
            showMessage("Silinemedi — bu kategoride ürünler var.", false);
        }
    }
});

// Kategori sırasını (SortOrder) değiştir — ad ve üst kategori korunur
document.getElementById("admin-categories").addEventListener("change", async (e) => {
    const sortInput = e.target.closest(".cat-sort-input");
    const parentSelect = e.target.closest(".cat-parent-select");

    // Üst kategori değişikliği: ad ve sıra korunur, sadece parentId değişir
    if (parentSelect) {
        const c = adminCategories.find(x => x.id === Number(parentSelect.dataset.id));
        if (!c) return;
        try {
            await apiPut(`/categories/${parentSelect.dataset.id}`, {
                name: c.name,
                parentId: parentSelect.value ? Number(parentSelect.value) : null,
                sortOrder: c.sortOrder
            });
            showMessage("Kategorinin üstü değiştirildi.");
        } catch (err) {
            showMessage(err.message, false);
        }
        loadAdminCategories();   // Başarılıysa ağaç yeniden çizilir, hatalıysa eski hâline döner
        return;
    }

    if (!sortInput) return;

    const c = adminCategories.find(x => x.id === Number(sortInput.dataset.id));
    if (!c) return;
    try {
        await apiPut(`/categories/${sortInput.dataset.id}`, {
            name: c.name,
            parentId: c.parentId ?? null,
            sortOrder: Number(sortInput.value)
        });
        showMessage("Kategori sırası güncellendi.");
        loadAdminCategories();
    } catch (err) {
        showMessage(err.message, false);
    }
});

/* ===== Kullanıcılar ===== */

let adminUsers = [];   // müşteri modalı, siparişi olmayan kullanıcı için buradan beslenir

async function loadAdminUsers() {
    const users = await apiGet("/admin/users");
    adminUsers = users;
    const me = localStorage.getItem("username");

    const rows = users.map(u => {
        const isMe = u.username === me;
        const roleSelect = `<select class="user-role" data-id="${u.id}" ${isMe ? "disabled" : ""}>
            <option value="Customer" ${u.role === "Customer" ? "selected" : ""}>Customer</option>
            <option value="Admin" ${u.role === "Admin" ? "selected" : ""}>Admin</option>
        </select>`;
        return `<tr class="${u.isBlocked ? "row-inactive" : ""}">
            <td>${u.id}</td>
            <td>
                <input type="number" class="sort-input" data-id="${u.id}" value="${u.sortOrder}" title="Listedeki sırası">
            </td>
            <td>
                <button class="customer-link" data-user-id="${u.id}" title="Müşteri bilgilerini gör">${esc(u.username)}</button>${isMe ? " (sen)" : ""}${u.isBlocked ? ' <span class="badge-inactive">bloklu</span>' : ""}
            </td>
            <td>
                <input type="email" class="contact-input ${u.email ? "" : "input-missing"}" data-id="${u.id}" data-field="email"
                       value="${esc(u.email ?? "")}" placeholder="⚠️ yok"
                       title="${u.email ? "E-posta — değiştirip Enter'a bas" : "E-postası yok: sipariş bildirimi alamıyor. Buraya yazıp Enter'a basabilirsin."}">
            </td>
            <td><input type="tel" class="contact-input" data-id="${u.id}" data-field="phone" value="${esc(u.phone ?? "")}" placeholder="-" title="Telefon — değiştirip Enter'a bas"></td>
            <td><input type="text" class="contact-input contact-address" data-id="${u.id}" data-field="address" value="${esc(u.address ?? "")}" placeholder="-" title="Adres — değiştirip Enter'a bas"></td>
            <td>${roleSelect}</td>
            <td>${fmtDate(u.createdAt)}</td>
            <td>
                <button class="btn-edit btn-passwd" data-id="${u.id}">Şifre Belirle</button>
                ${isMe ? "" : `<button class="${u.isBlocked ? "btn-activate" : "btn-delete"} btn-block" data-id="${u.id}" data-blocked="${u.isBlocked}">${u.isBlocked ? "Blok Kaldır" : "Blokla"}</button>`}
                ${isMe ? "" : `<button class="btn-delete" data-id="${u.id}">Sil</button>`}
            </td>
        </tr>`;
    }).join("");

    document.getElementById("admin-users").innerHTML = `<table class="admin-table">
        <thead><tr><th>Id</th><th>Sıra</th><th>Kullanıcı</th><th>E-posta</th><th>Telefon</th><th>Adres</th><th>Rol</th><th>Kayıt Tarihi</th><th>İşlem</th></tr></thead>
        <tbody>${rows}</tbody></table>`;
}

document.getElementById("user-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    try {
        const r = await apiPost("/admin/users", {
            username: document.getElementById("u-name").value,
            email: document.getElementById("u-email").value || null,
            phone: document.getElementById("u-phone").value || null,
            address: document.getElementById("u-address").value || null,
            password: document.getElementById("u-pass").value,
            role: document.getElementById("u-role").value
        });
        showMessage(r.message);
        document.getElementById("user-form").reset();
        loadAdminUsers();
    } catch (err) {
        showMessage(err.message, false);
    }
});

// Rol değişikliği: seçim kutusundan anında kaydedilir
document.getElementById("admin-users").addEventListener("change", async (e) => {
    const sel = e.target.closest(".user-role");
    const sortInput = e.target.closest(".sort-input");
    const contactInput = e.target.closest(".contact-input");

    // E-posta/telefon/adres: sadece değişen alan gönderilir, diğerleri null kalır ve
    // backend'de dokunulmaz. Boş string göndermek alanı temizler (backend null'a çevirir).
    if (contactInput) {
        const alanAdlari = { email: "E-posta", phone: "Telefon", address: "Adres" };
        try {
            await apiPut(`/admin/users/${contactInput.dataset.id}`, {
                [contactInput.dataset.field]: contactInput.value.trim()
            });
            showMessage(`${alanAdlari[contactInput.dataset.field]} güncellendi.`);
            // E-posta değişince "yok" uyarısının kalkması için tabloyu tazele
            if (contactInput.dataset.field === "email") loadAdminUsers();
        } catch (err) {
            showMessage(err.message, false);
            loadAdminUsers();   // Hata varsa tabloyu sunucudaki gerçek değere geri döndür
        }
        return;
    }

    if (sel) {
        try {
            const r = await apiPut(`/admin/users/${sel.dataset.id}`, { role: sel.value });
            showMessage(r.message);
        } catch (err) {
            showMessage(err.message, false);
            loadAdminUsers();
        }
        return;
    }

    if (sortInput) {
        try {
            const r = await apiPut(`/admin/users/${sortInput.dataset.id}`, { sortOrder: Number(sortInput.value) });
            showMessage(r.message);
            loadAdminUsers();
        } catch (err) {
            showMessage(err.message, false);
        }
    }
});

document.getElementById("admin-users").addEventListener("click", async (e) => {
    const passBtn = e.target.closest(".btn-passwd");
    const deleteBtn = e.target.closest(".btn-delete");
    const blockBtn = e.target.closest(".btn-block");

    const customerLink = e.target.closest(".customer-link");
    if (customerLink) {
        const id = Number(customerLink.dataset.userId);
        await openCustomerModal(id, adminUsers.find(u => u.id === id));
        return;
    }

    if (blockBtn) {
        const nowBlocked = blockBtn.dataset.blocked === "true";
        if (!nowBlocked && !confirm("Bu kullanıcı bloklansın mı? Bloklu kullanıcı giriş yapamaz.")) return;
        try {
            const r = await apiPut(`/admin/users/${blockBtn.dataset.id}`, { isBlocked: !nowBlocked });
            showMessage(r.message);
            loadAdminUsers();
        } catch (err) {
            showMessage(err.message, false);
        }
    }

    if (passBtn) {
        const pass = prompt("Yeni şifre:");
        if (!pass) return;
        try {
            const r = await apiPut(`/admin/users/${passBtn.dataset.id}`, { password: pass });
            showMessage(r.message);
        } catch (err) {
            showMessage(err.message, false);
        }
    }

    if (deleteBtn) {
        try {
            await apiDelete(`/admin/users/${deleteBtn.dataset.id}`);
            showMessage("Kullanıcı silindi.");
            loadAdminUsers();
        } catch (err) {
            showMessage(err.message, false);
        }
    }
});

/* ===== Loglar ===== */

// HTTP durum koduna göre renk sınıfı (2xx yeşil, 4xx sarı, 5xx kırmızı).
// "Seviye" sütunu kaldırıldı: önem zaten durum kodundan (renkli rozet) okunuyordu,
// ayrı bir seviye kolonu aynı bilgiyi ikinci kez göstermekten ibaretti.
function statusClass(code) {
    if (code == null) return "";
    if (code >= 500) return "log-error";
    if (code >= 400) return "log-warning";
    return "log-info";
}

// Durum kodunu düz Türkçeye çevirir: teknik olmayan bir okuyucu "işlem başarılı mı
// yoksa reddedildi mi" sorusunu koda bakmadan cevaplayabilsin.
function sonucMetni(code) {
    if (code == null) return "";
    if (code >= 500) return `başarısız — sunucu hatası (${code})`;
    if (code >= 400) return `reddedildi (${code})`;
    if (code >= 200 && code < 300) return `başarılı (${code})`;
    return `(${code})`;
}

let adminLogs = [];

async function loadAdminLogs() {
    adminLogs = await apiGet("/admin/logs");
    renderAdminLogs();
}

function renderAdminLogs() {
    const q = (document.getElementById("log-search")?.value ?? "").trim().toLowerCase();

    const logs = adminLogs.filter(l => {
        if (q) {
            const hay = `${l.action} ${l.details ?? ""} ${l.username ?? ""} ${l.ipAddress ?? ""} ${l.path ?? ""} ${l.exception ?? ""}`.toLowerCase();
            if (!hay.includes(q)) return false;
        }
        return true;
    });

    if (logs.length === 0) {
        document.getElementById("admin-logs").innerHTML = "<p class='empty'>Kayıt bulunamadı.</p>";
        return;
    }

    // Her kaydın ne olduğu doğrudan satırdaki "Açıklama" sütununda düz Türkçe okunur:
    // kim ne yaptı, hangi kaynak üzerinde ve hangi sonuçla. Ürün işlemleri "hangi ürün"
    // bilgisiyle backend'de ayrıntılı loglanır; hata kayıtlarında istisna mesajı da eklenir.
    const rows = logs.map(l => {
        const statusBadge = l.statusCode != null
            ? `<span class="log-badge ${statusClass(l.statusCode)}">${l.statusCode}</span>`
            : "-";
        return `<tr class="log-row">
            <td>${l.id}</td>
            <td>${esc(l.username ?? (l.userId ?? "Ziyaretçi"))}</td>
            <td>${esc(l.action)}</td>
            <td class="log-aciklama">${logAciklama(l)}</td>
            <td>${statusBadge}</td>
            <td>${esc(l.ipAddress ?? "-")}</td>
            <td>${l.durationMs != null ? l.durationMs + " ms" : "-"}</td>
            <td>${fmtDate(l.timestamp)}</td>
        </tr>`;
    }).join("");

    document.getElementById("admin-logs").innerHTML = `<table class="admin-table logs-table">
        <thead><tr><th>Id</th><th>Kullanıcı</th><th>İşlem</th><th>Açıklama</th><th>Durum</th><th>IP</th><th>Süre</th><th>Zaman</th></tr></thead>
        <tbody>${rows}</tbody></table>`;
}

// Bir logun "ne olduğunu" dışarıdan bakan biri anlayacak şekilde anlatır.
// - İş kodunun yazdığı açıklama (örn. "“Akıllı Saat” (#12) ürünü eklendi.") zaten okunur,
//   olduğu gibi gösterilir.
// - Yalnızca "METHOD /yol" içeren genel kayıtta ise ham yol yerine düz Türkçe bir cümle
//   kurulur: hangi kaynağa ne yapıldığı + sonucun başarılı mı reddedildi mi olduğu.
// - Hata varsa istisna mesajı ayrı satırda kırmızı eklenir.
function logAciklama(l) {
    const detay = l.details ?? "";
    const genelKayit = /^(GET|POST|PUT|DELETE|PATCH)\s/i.test(detay);

    let html;
    if (detay && !genelKayit) {
        html = esc(detay);
    } else {
        // Genel middleware kaydı: İşlem sütununda zaten okunur ad var; burada teknik
        // ayrıntıyı (yol) ve sonucu net bir cümleyle tamamlıyoruz.
        const yol = ((l.httpMethod ? l.httpMethod + " " : "") + (l.path ?? "")).trim();
        const sonuc = sonucMetni(l.statusCode);
        const parcalar = [];
        if (yol) parcalar.push(`<span class="log-path">${esc(yol)}</span>`);
        if (sonuc) parcalar.push(`İşlem ${esc(sonuc)}`);
        html = parcalar.length ? parcalar.join(" — ") : "-";
    }

    if (l.exception) {
        html += `<div class="log-exc">${esc(l.exception)}</div>`;
    }
    return html;
}

// Serbest metin araması (istemci tarafı — kayıtlar zaten yüklü)
let logSearchTimer;
document.getElementById("log-search")?.addEventListener("input", () => {
    clearTimeout(logSearchTimer);
    logSearchTimer = setTimeout(renderAdminLogs, 200);
});

/* ===== Analiz ===== */

// Seçilen filtreler (tarih aralığı + kategori + durum), sekme değişse/yenilense de korunur
let analyticsFrom = "";
let analyticsTo = "";
let analyticsCategory = "";
let analyticsStatus = "";
let analyticsCustomer = "";
let analyticsCity = "";
let analyticsMinTotal = "";
let analyticsMaxTotal = "";
let analyticsCategoryFilled = false;

// Analiz filtre çubuğundaki kategori seçicisini bir kez doldurur
async function fillAnalyticsCategory() {
    if (analyticsCategoryFilled) return;
    const cats = await apiGet("/categories");
    const parents = cats.filter(c => c.parentId == null);
    const options = parents.map(p => {
        const children = cats.filter(c => c.parentId === p.id)
            .map(ch => `<option value="${ch.id}">&nbsp;&nbsp;↳ ${esc(ch.name)}</option>`).join("");
        return `<option value="${p.id}">${esc(p.name)}</option>` + children;
    }).join("");
    document.getElementById("an-category").innerHTML =
        `<option value="">Tüm kategoriler</option>` + options;
    analyticsCategoryFilled = true;
}

// --- Basit yatay bar grafiği (kategori cirosu, durum dağılımı için) ---
function barChart(items, fmt) {
    if (!items || items.length === 0) return "<p class='empty'>Veri yok.</p>";
    const max = Math.max(...items.map(i => i.value), 1);
    return `<div class="bar-chart">` + items.map(i => `
        <div class="bar-row">
            <span class="bar-label" title="${esc(String(i.label))}">${esc(String(i.label))}</span>
            <span class="bar-track"><span class="bar-fill" style="width:${(i.value / max * 100).toFixed(1)}%"></span></span>
            <span class="bar-value">${fmt(i.value)}</span>
        </div>`).join("") + `</div>`;
}

// --- Günlük gelir/kâr trend grafiği (saf SVG çizgi + alan) ---
function trendChart(trend) {
    if (!trend || trend.length === 0) return "<p class='empty'>Bu aralıkta satış verisi yok.</p>";
    const W = 760, H = 240, padL = 10, padR = 10, padT = 14, padB = 28;
    const n = trend.length;
    const maxRev = Math.max(...trend.map(t => Math.max(t.revenue, t.profit)), 1);
    const X = (i) => padL + (n === 1 ? (W - padL - padR) / 2 : i * (W - padL - padR) / (n - 1));
    const Y = (v) => padT + (1 - v / maxRev) * (H - padT - padB);

    const revPts = trend.map((t, i) => `${X(i).toFixed(1)},${Y(t.revenue).toFixed(1)}`).join(" ");
    const profPts = trend.map((t, i) => `${X(i).toFixed(1)},${Y(t.profit).toFixed(1)}`).join(" ");
    const areaPts = `${X(0).toFixed(1)},${Y(0).toFixed(1)} ${revPts} ${X(n - 1).toFixed(1)},${Y(0).toFixed(1)}`;

    // Noktalar üzerine gün+değer ipucu (title)
    const dots = trend.map((t, i) =>
        `<circle cx="${X(i).toFixed(1)}" cy="${Y(t.revenue).toFixed(1)}" r="3" class="trend-dot">
            <title>${fmtDate(t.date).split(" ")[0]} • Gelir ${money(t.revenue)} • Kâr ${money(t.profit)} • ${t.orders} sipariş</title>
        </circle>`).join("");

    // x ekseninde ilk / orta / son tarih
    const labelIdx = n === 1 ? [0] : [0, Math.floor((n - 1) / 2), n - 1];
    const xLabels = [...new Set(labelIdx)].map(i =>
        `<text x="${X(i).toFixed(1)}" y="${H - 8}" class="trend-xlabel" text-anchor="${i === 0 ? "start" : i === n - 1 ? "end" : "middle"}">${fmtDate(trend[i].date).split(" ")[0]}</text>`).join("");

    return `
        <svg viewBox="0 0 ${W} ${H}" class="trend-chart" preserveAspectRatio="none" role="img" aria-label="Günlük gelir ve kâr trendi">
            <polygon points="${areaPts}" class="trend-area"></polygon>
            <polyline points="${revPts}" class="trend-line-rev" fill="none"></polyline>
            <polyline points="${profPts}" class="trend-line-prof" fill="none"></polyline>
            ${dots}
            ${xLabels}
        </svg>
        <div class="chart-legend">
            <span><i class="lg-rev"></i> Gelir</span>
            <span><i class="lg-prof"></i> Kâr</span>
        </div>`;
}

// Kategorik grafikler için ortak renk paleti (donut dilimleri, sütunlar vb.)
const CHART_COLORS = ["#6366f1", "#10b981", "#f59e0b", "#ef4444", "#3b82f6", "#8b5cf6", "#ec4899", "#14b8a6", "#f97316", "#64748b"];

// --- Donut (halka) grafiği: pay dağılımları için (sipariş durumu, kategori payı) ---
function donutChart(items, fmt) {
    const data = (items || []).filter(i => i.value > 0);
    if (data.length === 0) return "<p class='empty'>Veri yok.</p>";
    const total = data.reduce((s, i) => s + i.value, 0);
    const R = 70, r = 42, cx = 90, cy = 90;
    let segs;

    if (data.length === 1) {
        // Tek dilim: yay (arc) tam çember çizemez; kalın konturlu bir halka ile gösterilir
        segs = `<circle cx="${cx}" cy="${cy}" r="${(R + r) / 2}" fill="none" stroke="${CHART_COLORS[0]}" stroke-width="${R - r}">
            <title>${esc(String(data[0].label))}: ${fmt(data[0].value)} (%100)</title></circle>`;
    } else {
        let acc = 0;
        segs = data.map((i, idx) => {
            const frac = i.value / total;
            const a0 = acc * 2 * Math.PI, a1 = (acc + frac) * 2 * Math.PI; acc += frac;
            const large = frac > 0.5 ? 1 : 0;
            const p = (rad, ang) => `${(cx + rad * Math.sin(ang)).toFixed(2)} ${(cy - rad * Math.cos(ang)).toFixed(2)}`;
            const color = CHART_COLORS[idx % CHART_COLORS.length];
            return `<path d="M ${p(R, a0)} A ${R} ${R} 0 ${large} 1 ${p(R, a1)} L ${p(r, a1)} A ${r} ${r} 0 ${large} 0 ${p(r, a0)} Z" fill="${color}">
                <title>${esc(String(i.label))}: ${fmt(i.value)} (%${(frac * 100).toFixed(1)})</title></path>`;
        }).join("");
    }

    const legend = data.map((i, idx) =>
        `<span><i style="background:${CHART_COLORS[idx % CHART_COLORS.length]}"></i> ${esc(String(i.label))} — ${fmt(i.value)}</span>`).join("");

    return `<div class="donut-wrap">
        <svg viewBox="0 0 180 180" class="donut-chart" role="img">
            ${segs}
            <text x="90" y="86" class="donut-total-l">Toplam</text>
            <text x="90" y="106" class="donut-total-v">${fmt(total)}</text>
        </svg>
        <div class="donut-legend">${legend}</div>
    </div>`;
}

// --- Dikey sütun grafiği: her kategori için ciro + kâr ikili çubuk (aylık kırılım) ---
function columnChart(items, fmt) {
    const data = items || [];
    if (data.length === 0) return "<p class='empty'>Veri yok.</p>";
    const W = Math.max(320, data.length * 60), H = 200, padB = 34, padT = 12, padL = 10, padR = 10;
    const max = Math.max(...data.flatMap(d => [d.revenue, d.profit]), 1);
    const bw = (W - padL - padR) / data.length;
    const y = (v) => padT + (1 - v / max) * (H - padT - padB);
    const base = H - padB;

    const cols = data.map((d, i) => {
        const x = padL + i * bw;
        const w = bw * 0.30;
        return `
            <rect x="${(x + bw * 0.16).toFixed(1)}" y="${y(d.revenue).toFixed(1)}" width="${w.toFixed(1)}" height="${(base - y(d.revenue)).toFixed(1)}" class="col-rev" rx="2">
                <title>${esc(d.label)} • Ciro ${fmt(d.revenue)}</title></rect>
            <rect x="${(x + bw * 0.52).toFixed(1)}" y="${y(d.profit).toFixed(1)}" width="${w.toFixed(1)}" height="${(base - y(d.profit)).toFixed(1)}" class="col-prof" rx="2">
                <title>${esc(d.label)} • Kâr ${fmt(d.profit)}</title></rect>
            <text x="${(x + bw / 2).toFixed(1)}" y="${H - padB + 15}" class="col-xlabel" text-anchor="middle">${esc(d.label)}</text>`;
    }).join("");

    return `<div class="col-scroll"><svg viewBox="0 0 ${W} ${H}" class="column-chart" role="img">
            <line x1="${padL}" y1="${base}" x2="${W - padR}" y2="${base}" class="col-axis"></line>
            ${cols}
        </svg></div>
        <div class="chart-legend"><span><i class="lg-rev"></i> Ciro</span><span><i class="lg-prof"></i> Kâr</span></div>`;
}

async function loadAnalytics() {
    await fillAnalyticsCategory();

    document.getElementById("an-from").value = analyticsFrom;
    document.getElementById("an-to").value = analyticsTo;
    document.getElementById("an-category").value = analyticsCategory;
    document.getElementById("an-status").value = analyticsStatus;
    document.getElementById("an-customer").value = analyticsCustomer;
    document.getElementById("an-city").value = analyticsCity;
    document.getElementById("an-min-total").value = analyticsMinTotal;
    document.getElementById("an-max-total").value = analyticsMaxTotal;

    const params = new URLSearchParams();
    if (analyticsFrom) params.set("from", analyticsFrom);
    if (analyticsTo) params.set("to", analyticsTo);
    if (analyticsCategory) params.set("categoryId", analyticsCategory);
    if (analyticsStatus) params.set("status", analyticsStatus);
    if (analyticsCustomer) params.set("customer", analyticsCustomer);
    if (analyticsCity) params.set("city", analyticsCity);
    if (analyticsMinTotal) params.set("minTotal", analyticsMinTotal);
    if (analyticsMaxTotal) params.set("maxTotal", analyticsMaxTotal);
    const qs = params.toString();

    const a = await apiGet(`/admin/analytics${qs ? "?" + qs : ""}`);

    const saleRows = (a.productSales ?? []).map(s =>
        `<tr><td>${esc(s.product)}</td><td>${s.qtySold}</td><td>${money(s.revenue)}</td><td>${money(s.profit)}</td></tr>`).join("");

    const r = a.range;
    const rangeSaleRows = r ? (r.productSales ?? []).map(s =>
        `<tr><td>${esc(s.product)}</td><td>${s.qtySold}</td><td>${money(s.revenue)}</td><td>${money(s.profit)}</td></tr>`).join("") : "";

    const rangeHtml = r ? `
        <h3 class="analytics-h">Seçili Aralık (${fmtDate(r.from).split(" ")[0]} — ${fmtDate(r.to).split(" ")[0]})</h3>
        <div class="stats-grid">
            <div class="stat-card"><span>Sipariş</span><strong>${r.orderCount}</strong></div>
            <div class="stat-card"><span>Gelir</span><strong>${money(r.revenue)}</strong></div>
            <div class="stat-card"><span>Maliyet</span><strong>${money(r.cost)}</strong></div>
            <div class="stat-card profit"><span>Kâr</span><strong>${money(r.profit)}</strong></div>
        </div>
        ${rangeSaleRows ? `<table class="admin-table"><thead><tr><th>Ürün</th><th>Satılan</th><th>Ciro</th><th>Kâr</th></tr></thead><tbody>${rangeSaleRows}</tbody></table>`
            : "<p class='empty'>Bu aralıkta satış yok.</p>"}
    ` : "";

    // Grafik verileri
    const catChart = barChart((a.revenueByCategory ?? []).map(c => ({ label: c.category, value: c.revenue })), money);
    // Sipariş durum dağılımı artık DONUT (pay) grafiği — bar yerine daha görsel
    const statusChart = donutChart([
        { label: "Bekliyor", value: a.pendingOrders },
        { label: "Onaylandı", value: a.approvedOrders },
        { label: "Kargoda", value: a.shippedOrders },
        { label: "Teslim", value: a.deliveredOrders },
        { label: "Reddedilen", value: a.rejectedOrders },
        { label: "İptal", value: a.cancelledOrders ?? 0 }
    ], (v) => `${v}`);
    // Aylık ciro & kâr: DİKEY SÜTUN grafiği (monthlyBreakdown desc gelir; kronolojik için ters çevrilir)
    const monthlyColChart = columnChart(
        [...(a.monthlyBreakdown ?? [])].reverse().map(m => ({ label: m.label, revenue: m.revenue, profit: m.profit })),
        money);
    // Kategori ciro payı: DONUT
    const catDonut = donutChart((a.revenueByCategory ?? []).map(c => ({ label: c.category, value: c.revenue })), money);
    // En çok harcayan müşteriler: yatay BAR grafiği (tablonun görsel karşılığı)
    const topCustChart = barChart((a.topCustomers ?? []).map(c => ({ label: c.customer, value: c.spent })), money);

    // En çok harcayan müşteriler tablosu
    const topRows = (a.topCustomers ?? []).map(c =>
        `<tr><td><button class="customer-link" data-user-id="${c.userId}">${esc(c.customer)}</button></td><td>${c.orders}</td><td>${money(c.spent)}</td></tr>`).join("");

    // Kategori kırılımı (adet / ciro / kâr)
    const catBreakRows = (a.categoryBreakdown ?? []).map(c =>
        `<tr><td>${esc(c.category)}</td><td>${sayi(c.qtySold)}</td><td>${money(c.revenue)}</td><td class="${c.profit >= 0 ? "profit-pos" : "profit-neg"}">${money(c.profit)}</td></tr>`).join("");

    // En kârlı ürünler
    const profitProdRows = (a.topProfitProducts ?? []).map(p =>
        `<tr><td>${esc(p.product)}</td><td>${sayi(p.qtySold)}</td><td>${money(p.revenue)}</td><td class="${p.profit >= 0 ? "profit-pos" : "profit-neg"}">${money(p.profit)}</td></tr>`).join("");

    // Aylık kırılım (son 12 ay)
    const monthlyRows = (a.monthlyBreakdown ?? []).map(m =>
        `<tr><td>${esc(m.label)}</td><td>${sayi(m.orders)}</td><td>${money(m.revenue)}</td><td class="${m.profit >= 0 ? "profit-pos" : "profit-neg"}">${money(m.profit)}</td></tr>`).join("");

    // Kupon kullanımı
    const couponRows = (a.couponUsage ?? []).map(c =>
        `<tr><td><strong>${esc(c.code)}</strong></td><td>${sayi(c.count)}</td><td>${money(c.totalDiscount)}</td></tr>`).join("");

    // Düşük stok listesi
    const lowStockRows = (a.lowStockList ?? []).map(p =>
        `<tr><td>${esc(p.name)}</td><td>${esc(p.category)}</td><td class="${p.stock === 0 ? "profit-neg" : ""}">${p.stock === 0 ? "Tükendi" : sayi(p.stock)}</td></tr>`).join("");

    // Filtreli siparişler (detay tablo) — yalnızca bir filtre uygulandıysa gelir
    const fo = a.filteredOrders;
    const foRows = fo ? fo.map(o => {
        const s = STATUS[o.status];
        return `<tr>
            <td>#${o.id}</td>
            <td>${fmtDate(o.createdAt)}</td>
            <td><button class="customer-link" data-user-id="${o.customerId}">${esc(o.customer)}</button></td>
            <td>${esc(o.city ?? "-")}</td>
            <td>${o.itemCount}</td>
            <td>${money(o.total)}</td>
            <td><span class="status ${s.class}">${s.text}</span></td>
        </tr>`;
    }).join("") : "";

    document.getElementById("admin-analytics").innerHTML = `
        <h3 class="analytics-h">Günlük Gelir & Kâr Trendi</h3>
        <div class="chart-card">${trendChart(a.trend)}</div>

        ${fo ? `<h3 class="analytics-h">Filtreli Siparişler ${a.filteredOrders.length >= 100 ? "(ilk 100)" : `(${a.filteredOrders.length})`}</h3>
        ${foRows ? `<table class="admin-table"><thead><tr><th>No</th><th>Tarih</th><th>Müşteri</th><th>Şehir</th><th>Ürün</th><th>Tutar</th><th>Durum</th></tr></thead><tbody>${foRows}</tbody></table>`
            : "<p class='empty'>Seçilen filtrelere uyan sipariş yok.</p>"}` : ""}

        ${rangeHtml}
        <h3 class="analytics-h">Satış & Kâr</h3>
        <div class="stats-grid">
            <div class="stat-card"><span>Toplam Gelir</span><strong>${money(a.totalRevenue)}</strong></div>
            <div class="stat-card"><span>Toplam Maliyet</span><strong>${money(a.totalCost)}</strong></div>
            <div class="stat-card profit"><span>Toplam Kâr</span><strong>${money(a.totalProfit)}</strong><span class="muted">Marj: %${a.profitMargin ?? 0}</span></div>
            <div class="stat-card"><span>Ort. Sipariş Tutarı</span><strong>${money(a.averageOrderValue)}</strong></div>
            <div class="stat-card"><span>Satılan Adet</span><strong>${sayi(a.totalUnitsSold)}</strong><span class="muted">${sayi(a.distinctProductsSold)} farklı ürün</span></div>
            <div class="stat-card"><span>En Çok Satan</span><strong>${esc(a.bestSellingProduct ?? "-")}${a.bestSellingProductQuantity ? ` (${a.bestSellingProductQuantity} adet)` : ""}</strong></div>
        </div>

        <h3 class="analytics-h">Dönemsel Gelir & Kâr</h3>
        <div class="stats-grid">
            <div class="stat-card"><span>Bugün</span><strong>${money(a.daily.revenue)}</strong><span class="muted">Kâr: ${money(a.daily.profit)}</span></div>
            <div class="stat-card"><span>Bu Hafta</span><strong>${money(a.weekly.revenue)}</strong><span class="muted">Kâr: ${money(a.weekly.profit)}</span></div>
            <div class="stat-card"><span>Bu Ay</span><strong>${money(a.monthly.revenue)}</strong><span class="muted">Kâr: ${money(a.monthly.profit)}</span></div>
            <div class="stat-card"><span>Bu Yıl</span><strong>${money(a.yearly.revenue)}</strong><span class="muted">Kâr: ${money(a.yearly.profit)}</span></div>
        </div>

        <h3 class="analytics-h">Siparişler</h3>
        <div class="stats-grid">
            <div class="stat-card"><span>Toplam</span><strong>${a.totalOrders}</strong></div>
            <div class="stat-card"><span>Bekleyen</span><strong>${a.pendingOrders}</strong></div>
            <div class="stat-card"><span>Onaylanan</span><strong>${a.approvedOrders}</strong></div>
            <div class="stat-card"><span>Kargoda</span><strong>${a.shippedOrders}</strong></div>
            <div class="stat-card"><span>Teslim Edildi</span><strong>${a.deliveredOrders}</strong></div>
            <div class="stat-card"><span>Reddedilen</span><strong>${a.rejectedOrders}</strong></div>
            <div class="stat-card"><span>İptal Edilen</span><strong>${a.cancelledOrders ?? 0}</strong></div>
        </div>

        <h3 class="analytics-h">Envanter & Müşteri</h3>
        <div class="stats-grid">
            <div class="stat-card"><span>Aktif Ürün</span><strong>${a.activeProducts}</strong></div>
            <div class="stat-card"><span>Düşük Stok (≤5)</span><strong>${a.lowStockProducts}</strong></div>
            <div class="stat-card"><span>Tükenen</span><strong>${a.outOfStockProducts}</strong></div>
            <div class="stat-card"><span>Envanter Maliyeti</span><strong>${money(a.inventoryCost)}</strong><span class="muted">Perakende: ${money(a.inventoryRetail)}</span></div>
            <div class="stat-card"><span>Müşteri Sayısı</span><strong>${a.customerCount}</strong></div>
            <div class="stat-card"><span>Bu Ay Yeni Müşteri</span><strong>${a.newCustomersThisMonth ?? 0}</strong></div>
            <div class="stat-card"><span>Sadık Müşteri</span><strong>${a.repeatCustomers ?? 0}</strong><span class="muted">1+ siparişli</span></div>
        </div>

        <h3 class="analytics-h">Aylık Ciro & Kâr (son 12 ay)</h3>
        <div class="chart-card">${monthlyColChart}</div>

        <div class="chart-grid">
            <div class="chart-card">
                <h3 class="analytics-h">Kategoriye Göre Ciro</h3>
                ${catChart}
            </div>
            <div class="chart-card">
                <h3 class="analytics-h">Sipariş Durum Dağılımı</h3>
                ${statusChart}
            </div>
            <div class="chart-card">
                <h3 class="analytics-h">Kategori Ciro Payı</h3>
                ${catDonut}
            </div>
            <div class="chart-card">
                <h3 class="analytics-h">En Çok Harcayan Müşteriler</h3>
                ${topCustChart}
            </div>
        </div>

        ${monthlyRows ? `<h3 class="analytics-h">Aylık Kırılım (son 12 ay)</h3>
        <table class="admin-table"><thead><tr><th>Ay</th><th>Sipariş</th><th>Ciro</th><th>Kâr</th></tr></thead><tbody>${monthlyRows}</tbody></table>` : ""}

        ${catBreakRows ? `<h3 class="analytics-h">Kategori Kırılımı (adet / ciro / kâr)</h3>
        <table class="admin-table"><thead><tr><th>Kategori</th><th>Satılan</th><th>Ciro</th><th>Kâr</th></tr></thead><tbody>${catBreakRows}</tbody></table>` : ""}

        ${topRows ? `<h3 class="analytics-h">En Çok Harcayan Müşteriler</h3>
        <table class="admin-table"><thead><tr><th>Müşteri</th><th>Sipariş</th><th>Toplam Harcama</th></tr></thead><tbody>${topRows}</tbody></table>` : ""}

        ${profitProdRows ? `<h3 class="analytics-h">En Kârlı Ürünler</h3>
        <table class="admin-table"><thead><tr><th>Ürün</th><th>Satılan</th><th>Ciro</th><th>Kâr</th></tr></thead><tbody>${profitProdRows}</tbody></table>` : ""}

        ${saleRows ? `<h3 class="analytics-h">Ürün Bazında Satış (kaç satıldı / ciro / kâr)</h3>
        <table class="admin-table"><thead><tr><th>Ürün</th><th>Satılan</th><th>Ciro</th><th>Kâr</th></tr></thead><tbody>${saleRows}</tbody></table>` : ""}

        ${couponRows ? `<h3 class="analytics-h">Kupon Kullanımı</h3>
        <table class="admin-table"><thead><tr><th>Kupon</th><th>Kullanım</th><th>Toplam İndirim</th></tr></thead><tbody>${couponRows}</tbody></table>` : ""}

        ${lowStockRows ? `<h3 class="analytics-h">Düşük Stok Uyarısı (≤5)</h3>
        <table class="admin-table"><thead><tr><th>Ürün</th><th>Kategori</th><th>Stok</th></tr></thead><tbody>${lowStockRows}</tbody></table>` : ""}
    `;
}

// Analiz sekmesindeki müşteri linkleri de modalı açsın
document.getElementById("admin-analytics").addEventListener("click", (e) => {
    const link = e.target.closest(".customer-link");
    if (link) openCustomerModal(Number(link.dataset.userId));
});

function readAnalyticsInputs() {
    analyticsFrom = document.getElementById("an-from").value;
    analyticsTo = document.getElementById("an-to").value;
    analyticsCategory = document.getElementById("an-category").value;
    analyticsStatus = document.getElementById("an-status").value;
    analyticsCustomer = document.getElementById("an-customer").value.trim();
    analyticsCity = document.getElementById("an-city").value.trim();
    analyticsMinTotal = document.getElementById("an-min-total").value;
    analyticsMaxTotal = document.getElementById("an-max-total").value;
}

document.getElementById("an-apply").addEventListener("click", () => {
    readAnalyticsInputs();
    loadAnalytics();
});

// Kategori/durum seçilince anında uygula
document.getElementById("an-category").addEventListener("change", () => {
    analyticsCategory = document.getElementById("an-category").value;
    loadAnalytics();
});
document.getElementById("an-status").addEventListener("change", () => {
    analyticsStatus = document.getElementById("an-status").value;
    loadAnalytics();
});

// Müşteri/şehir/tutar alanlarında yazarken 500 ms sonra otomatik uygula (Enter'da anında).
let analyticsInputTimer;
["an-customer", "an-city", "an-min-total", "an-max-total"].forEach(id => {
    const el = document.getElementById(id);
    el.addEventListener("input", () => {
        clearTimeout(analyticsInputTimer);
        analyticsInputTimer = setTimeout(() => { readAnalyticsInputs(); loadAnalytics(); }, 500);
    });
    el.addEventListener("keydown", (e) => {
        if (e.key === "Enter") { clearTimeout(analyticsInputTimer); readAnalyticsInputs(); loadAnalytics(); }
    });
});

document.getElementById("an-clear").addEventListener("click", () => {
    analyticsFrom = "";
    analyticsTo = "";
    analyticsCategory = "";
    analyticsStatus = "";
    analyticsCustomer = "";
    analyticsCity = "";
    analyticsMinTotal = "";
    analyticsMaxTotal = "";
    loadAnalytics();
});

/* ===== Yeni sipariş bildirimi ===== */

// Panel açıkken yeni sipariş gelirse admin'in haberi olsun diye periyodik yoklama.
// Tüm sipariş listesini değil, sadece {sayı, son sipariş id} döndüren hafif bir uç nokta çekilir.
// En son görülen sipariş id'si localStorage'da tutulur — sayfa yenilenince aynı sipariş
// için tekrar bildirim çıkmaz.
const POLL_MS = 20000;
let lastSeenOrderId = Number(localStorage.getItem("lastSeenOrderId") ?? 0);

async function pollNewOrders() {
    let data;
    try {
        data = await apiGet("/admin/orders/pending-count");
    } catch {
        return;   // Backend kapalı/ağ hatası: sessizce geç, bir sonraki turda tekrar denenir
    }

    // Bekleyen sipariş sayısı rozeti
    const badge = document.getElementById("pending-badge");
    badge.textContent = data.count;
    badge.style.display = data.count > 0 ? "inline-flex" : "none";

    // İlk açılış: mevcut siparişler "yeni" sayılmasın, sessizce işaretle
    if (lastSeenOrderId === 0) {
        setLastSeen(data.latestOrderId);
        return;
    }

    if (data.latestOrderId > lastSeenOrderId) {
        const kaçTane = data.latestOrderId - lastSeenOrderId;
        showToast(kaçTane === 1
            ? `🔔 Yeni sipariş geldi! #${data.latestOrderId}`
            : `🔔 ${kaçTane} yeni sipariş geldi!`, "info");

        setLastSeen(data.latestOrderId);

        // Siparişler sekmesi açıksa liste kendini tazelesin
        if (document.getElementById("tab-orders").classList.contains("active")) {
            loadAdminOrders();
        }
    }
}

function setLastSeen(id) {
    lastSeenOrderId = id;
    localStorage.setItem("lastSeenOrderId", String(id));
}

/* ===== Açılış ===== */

setupNav();
updateCartCount();
loadAdminOrders();
pollNewOrders();
setInterval(pollNewOrders, POLL_MS);

/* ===== Kuponlar ===== */

// Düzenlenen kuponun id'si; null ise form "yeni kupon" modundadır.
let editingCouponId = null;

// Son çekilen kupon listesi. Düzenle/Aç butonları formu bundan doldurur;
// tek bir kupon için sunucuya tekrar gitmeye gerek yok.
let adminCoupons = [];

// datetime-local kutusu YEREL saat ister, API UTC konuşur.
// Doğrudan bağlansaydı kupon "3 saat sonra başlar" gibi görünürdü.
const utcToLocalInput = (iso) => {
    if (!iso) return "";
    const d = new Date(iso);
    const pad = (n) => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};
const localInputToUtc = (value) => value ? new Date(value).toISOString() : null;

const couponOzet = (c) => c.type === 0 ? `%${c.value}` : `${c.value} TL`;

// Kuponun O ANKİ durumu: kapalıysa/süresi geçtiyse/limiti dolduysa kullanılamaz.
// Admin'in "kupon neden geçmiyor?" sorusunu tabloya bakarak cevaplayabilmesi için.
function couponDurum(c) {
    const now = new Date();
    if (!c.isActive) return { text: "Kapalı", class: "status-rejected" };
    if (c.startsAt && new Date(c.startsAt) > now) return { text: "Başlamadı", class: "status-pending" };
    if (c.endsAt && new Date(c.endsAt) < now) return { text: "Süresi doldu", class: "status-rejected" };
    if (c.maxUses != null && c.usedCount >= c.maxUses) return { text: "Limit doldu", class: "status-rejected" };
    return { text: "Aktif", class: "status-approved" };
}

// Kupon formundaki "kimlere tanımlansın" seçicisi. Kullanıcılar sekmesi açılmamış olabilir,
// bu yüzden liste gerektiğinde çekilir (bkz. ensureAdminOrders — aynı gerekçe).
//
// Seçim, <select multiple> yerine onay kutulu bir açılır liste: select multiple'da birden
// çok kişi ancak Ctrl basılı tutularak seçilebiliyor ve yanlışlıkla tek tıklama önceki
// seçimlerin hepsini siliyor.
async function couponUserSecimiDoldur() {
    if (adminUsers.length === 0) {
        try {
            adminUsers = await apiGet("/admin/users");
        } catch {
            return;   // liste çekilemezse form "Herkese açık" ile çalışmaya devam eder
        }
    }

    const liste = document.getElementById("c-users-list");
    const secili = new Set(couponSeciliKullanicilar());   // yeniden doldurulurken seçim kaybolmasın

    liste.innerHTML = adminUsers.map(u => `
        <label class="user-picker-item">
            <input type="checkbox" value="${u.id}" ${secili.has(u.id) ? "checked" : ""}>
            <span>${esc(u.username)}</span>
        </label>`).join("");

    couponSeciciEtiketiniTazele();
}

function couponSeciliKullanicilar() {
    return [...document.querySelectorAll("#c-users-list input:checked")].map(i => Number(i.value));
}

// Düğme metni, liste kapalıyken kimin seçili olduğunu gösterir
function couponSeciciEtiketiniTazele() {
    const secili = couponSeciliKullanicilar();
    const btn = document.getElementById("c-users-toggle");

    if (secili.length === 0) {
        btn.textContent = "Herkese açık";
    } else if (secili.length <= 2) {
        btn.textContent = secili.map(id => adminUsers.find(u => u.id === id)?.username ?? id).join(", ");
    } else {
        btn.textContent = `${secili.length} müşteri seçili`;
    }

    btn.classList.toggle("has-selection", secili.length > 0);
}

function couponSeciciAyarla(userIds = []) {
    const set = new Set(userIds);
    document.querySelectorAll("#c-users-list input").forEach(i => { i.checked = set.has(Number(i.value)); });
    couponSeciciEtiketiniTazele();
}

document.getElementById("c-users-toggle").addEventListener("click", (e) => {
    e.stopPropagation();
    const liste = document.getElementById("c-users-list");
    liste.hidden = !liste.hidden;
    e.currentTarget.setAttribute("aria-expanded", String(!liste.hidden));
});

document.getElementById("c-users-list").addEventListener("change", couponSeciciEtiketiniTazele);

// Dışarı tıklayınca kapanır (üye menüsüyle aynı davranış)
document.addEventListener("click", (e) => {
    const liste = document.getElementById("c-users-list");
    if (!liste.hidden && !e.target.closest("#c-users")) {
        liste.hidden = true;
        document.getElementById("c-users-toggle").setAttribute("aria-expanded", "false");
    }
});

async function loadAdminCoupons() {
    const kutu = document.getElementById("admin-coupons");

    await couponUserSecimiDoldur();

    let coupons;
    try {
        coupons = await apiGet("/coupons");
    } catch {
        kutu.innerHTML = "<p class='empty'>Kuponlar yüklenemedi.</p>";
        return;
    }

    adminCoupons = coupons;

    if (coupons.length === 0) {
        kutu.innerHTML = "<p class='empty'>Henüz kupon yok. Yukarıdaki formdan ekleyebilirsin.</p>";
        return;
    }

    const satirlar = coupons.map(c => {
        const d = couponDurum(c);
        const kullanim = c.maxUses != null ? `${c.usedCount}/${c.maxUses}` : `${c.usedCount}/∞`;
        const tarih = (c.startsAt || c.endsAt)
            ? `${c.startsAt ? fmtDate(c.startsAt) : "—"}<br>${c.endsAt ? fmtDate(c.endsAt) : "—"}`
            : "Süresiz";
        // Uzun listeler satırı şişirmesin: ilk ikisi yazılır, kalanı "+N" olarak özetlenir
        // ve tamamı tooltip'te durur.
        const isimler = c.usernames ?? [];
        const sahip = isimler.length > 0
            ? `<span class="coupon-owner" title="${esc(isimler.join(", "))}">`
              + esc(isimler.slice(0, 2).join(", "))
              + (isimler.length > 2 ? ` +${isimler.length - 2}` : "")
              + `</span>`
            : `<span class="muted">Herkese açık</span>`;
        return `
            <tr>
                <td><strong>${esc(c.code)}</strong></td>
                <td>${couponOzet(c)}</td>
                <td>${sahip}</td>
                <td>${c.minOrderTotal != null ? c.minOrderTotal + " TL" : "—"}</td>
                <td>${kullanim}</td>
                <td>${c.perUserLimit != null ? c.perUserLimit : "∞"}</td>
                <td class="coupon-dates">${tarih}</td>
                <td><span class="status ${d.class}">${d.text}</span></td>
                <td class="coupon-actions">
                    <button type="button" class="link-btn" data-edit-coupon="${c.id}">Düzenle</button>
                    ${c.isActive
                        ? `<button type="button" class="link-btn danger" data-close-coupon="${c.id}">Kapat</button>`
                        : `<button type="button" class="link-btn" data-open-coupon="${c.id}">Aç</button>`}
                </td>
            </tr>`;
    }).join("");

    kutu.innerHTML = `
        <table class="admin-table">
            <thead>
                <tr>
                    <th>Kod</th><th>İndirim</th><th>Kime</th><th>Min. sepet</th>
                    <th>Kullanım</th><th>Kişi başı</th><th>Başlangıç / Bitiş</th>
                    <th>Durum</th><th></th>
                </tr>
            </thead>
            <tbody>${satirlar}</tbody>
        </table>`;
}

function couponFormDoldur(c) {
    editingCouponId = c.id;
    document.getElementById("c-code").value = c.code;
    document.getElementById("c-type").value = String(c.type);
    document.getElementById("c-value").value = c.value;
    couponSeciciAyarla(c.userIds ?? []);
    document.getElementById("c-min").value = c.minOrderTotal ?? "";
    document.getElementById("c-maxuses").value = c.maxUses ?? "";
    document.getElementById("c-peruser").value = c.perUserLimit ?? "";
    document.getElementById("c-start").value = utcToLocalInput(c.startsAt);
    document.getElementById("c-end").value = utcToLocalInput(c.endsAt);
    document.querySelector("#coupon-form button[type=submit]").textContent = "Kuponu Güncelle";
    document.getElementById("coupon-cancel").style.display = "inline-block";
    document.getElementById("c-code").focus();
}

function couponFormSifirla() {
    editingCouponId = null;
    document.getElementById("coupon-form").reset();
    // form.reset() onay kutularını HTML'deki hallerine döndürür; seçici JS ile kurulduğu
    // için hepsini elle temizleyip düğme etiketini tazelemek gerekiyor.
    couponSeciciAyarla([]);
    document.querySelector("#coupon-form button[type=submit]").textContent = "Kupon Ekle";
    document.getElementById("coupon-cancel").style.display = "none";
}

document.getElementById("coupon-cancel").addEventListener("click", couponFormSifirla);

document.getElementById("coupon-form").addEventListener("submit", async (e) => {
    e.preventDefault();

    const bosIseNull = (id) => {
        const v = document.getElementById(id).value;
        return v === "" ? null : Number(v);
    };

    const dto = {
        code: document.getElementById("c-code").value.trim(),
        type: Number(document.getElementById("c-type").value),
        value: Number(document.getElementById("c-value").value),
        userIds: couponSeciliKullanicilar(),
        minOrderTotal: bosIseNull("c-min"),
        maxUses: bosIseNull("c-maxuses"),
        perUserLimit: bosIseNull("c-peruser"),
        startsAt: localInputToUtc(document.getElementById("c-start").value),
        endsAt: localInputToUtc(document.getElementById("c-end").value),
        // Düzenlemede kuponun açık/kapalı durumu korunur; aç/kapa ayrı butonların işi
        isActive: editingCouponId
            ? (adminCoupons.find(c => c.id === editingCouponId)?.isActive ?? true)
            : true
    };

    try {
        const r = editingCouponId
            ? await apiPut(`/coupons/${editingCouponId}`, dto)
            : await apiPost("/coupons", dto);
        showMessage(r.message);
        couponFormSifirla();
        await loadAdminCoupons();
    } catch (err) {
        showMessage(err.message, false);
    }
});

document.getElementById("admin-coupons").addEventListener("click", async (e) => {
    const edit = e.target.closest("[data-edit-coupon]");
    if (edit) {
        const c = adminCoupons.find(x => x.id === Number(edit.dataset.editCoupon));
        if (c) couponFormDoldur(c);
        return;
    }

    const close = e.target.closest("[data-close-coupon]");
    if (close) {
        if (!confirm("Kupon kapatılsın mı? (Geçmiş siparişler etkilenmez)")) return;
        try {
            const r = await apiDelete(`/coupons/${close.dataset.closeCoupon}`);
            showMessage(r.message);
            await loadAdminCoupons();
        } catch (err) {
            showMessage(err.message, false);
        }
        return;
    }

    // Kapalı kuponu yeniden açmak: ayrı bir uç yerine mevcut kuponu isActive=true ile günceller
    const open = e.target.closest("[data-open-coupon]");
    if (open) {
        const c = adminCoupons.find(x => x.id === Number(open.dataset.openCoupon));
        if (!c) return;
        try {
            const r = await apiPut(`/coupons/${c.id}`, {
                code: c.code, type: c.type, value: c.value, userIds: c.userIds,
                minOrderTotal: c.minOrderTotal, maxUses: c.maxUses, perUserLimit: c.perUserLimit,
                startsAt: c.startsAt, endsAt: c.endsAt, isActive: true
            });
            showMessage(r.message);
            await loadAdminCoupons();
        } catch (err) {
            showMessage(err.message, false);
        }
    }
});

/* =====================================================================
   Analiz — Glassmorphism gösterge paneli (alt-sekmeler)
   "Böl ve Yönet": her alt-sekmenin işi yalnızca o sekme aktifken çalışır.
   ===================================================================== */

document.querySelectorAll(".dash-tab").forEach(btn => {
    btn.addEventListener("click", () => {
        const which = btn.dataset.dash;
        document.querySelectorAll(".dash-tab").forEach(b => b.classList.remove("active"));
        document.querySelectorAll(".dash-panel").forEach(p => p.classList.remove("active"));
        btn.classList.add("active");
        document.getElementById(`dash-${which}`).classList.add("active");

        // Yalnızca ilgili sekme aktifken kaynak tüketilir.
        if (which === "live") startLiveFeed();
        else stopLiveFeed();

        if (which === "map") buildRegionMap();   // ilk açılışta bir kez kurulur
    });
});

/* ===== Alt-sekme 1: Canlı Akış (GERÇEK siparişler) =====
   Eskiden Math.random ile sahte sipariş üretiliyordu. Artık gerçek veri:
   ilk açılışta son siparişler yüklenir, sonra periyodik olarak yalnızca YENİ
   siparişler (id > son görülen) çekilip akışın en üstüne animasyonla eklenir. */

let liveTimer = null;
let livePaused = false;
let liveCount = 0;          // Bu oturumda AKIŞ AÇIKKEN gelen yeni sipariş sayısı
let liveSum = 0;            // ...ve onların toplam cirosu
let liveLastId = 0;         // Akışta görülen en yüksek sipariş no
let liveSeeded = false;     // İlk (geçmiş) yükleme yapıldı mı
const LIVE_POLL_MS = 5000;

async function startLiveFeed() {
    // Yalnızca Analiz + Canlı Akış aktifken çalışmalı; çift kurulmayı da engelle.
    const analyticsActive = document.getElementById("tab-analytics")?.classList.contains("active");
    const liveActive = document.getElementById("dash-live")?.classList.contains("active");
    if (!analyticsActive || !liveActive || liveTimer || livePaused) return;

    // İlk açılış: son siparişleri "geçmiş" olarak göster (oturum sayaçlarına dahil etme).
    if (!liveSeeded) {
        liveSeeded = true;
        try {
            const gecmis = await apiGet("/admin/orders/recent?take=15");
            const feed = document.getElementById("live-feed");
            const empty = feed.querySelector(".live-empty");
            if (empty && gecmis.length) empty.remove();
            // Eskiden yeniye sıralı gelir; prepend ile en yeni en üstte olur.
            gecmis.forEach(o => { renderLiveRow(o, false); liveLastId = Math.max(liveLastId, o.id); });
            if (!gecmis.length) {
                feed.querySelector(".live-empty").textContent = "Henüz sipariş yok — yeni sipariş geldikçe burada görünecek.";
            }
        } catch { /* backend kapalıysa sessizce geç; poll tekrar dener */ }
    }

    // Periyodik: yalnızca yeni siparişleri çek.
    liveTimer = setInterval(pollLiveFeed, LIVE_POLL_MS);
}

function stopLiveFeed() {
    if (liveTimer) { clearInterval(liveTimer); liveTimer = null; }
}

async function pollLiveFeed() {
    if (livePaused) return;
    let yeniler;
    try {
        yeniler = await apiGet(`/admin/orders/recent?afterId=${liveLastId}&take=20`);
    } catch {
        return;   // ağ/backend hatası: bir sonraki turda tekrar
    }
    // Eskiden yeniye sıralı; sırayla eklenince en yeni en üste biner.
    yeniler.forEach(o => {
        renderLiveRow(o, true);
        liveLastId = Math.max(liveLastId, o.id);
        liveCount++;
        liveSum += o.total;
    });
    if (yeniler.length) {
        document.getElementById("live-count").textContent = liveCount;
        document.getElementById("live-sum").textContent = `${liveSum.toLocaleString("tr-TR")} TL`;
    }
}

// Bir sipariş satırını akışa ekler. isNew=true ise animasyon + toast (gerçek yeni sipariş).
function renderLiveRow(o, isNew) {
    const feed = document.getElementById("live-feed");
    const empty = feed.querySelector(".live-empty");
    if (empty) empty.remove();

    const t = new Date(o.createdAt);
    const saat = t.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
    const gun = t.toLocaleDateString("tr-TR", { day: "2-digit", month: "2-digit" });
    const s = STATUS[o.status];
    const yer = o.district ? `${o.city} / ${o.district}` : o.city;
    const row = document.createElement("div");
    row.className = "live-row" + (isNew ? " enter" : "");
    row.innerHTML = `
        <span class="live-badge">#${o.id}</span>
        <div class="live-row-main">
            <strong>${esc(o.customer)}</strong> — ${o.itemCount} ürün
            <span class="status ${s.class}" style="margin-left:6px">${s.text}</span>
            <div class="live-row-sub">📍 ${esc(yer)} · ${gun} ${saat}</div>
        </div>
        <span class="live-amount">${o.total.toLocaleString("tr-TR")} TL</span>`;
    feed.prepend(row);
    if (isNew) {
        requestAnimationFrame(() => row.classList.remove("enter"));
        liveToast(o);
    }
    // Liste sınırsız büyümesin: en fazla son 30 kayıt tutulur.
    while (feed.children.length > 30) feed.lastElementChild.remove();
}

// Sağ ÜSTTE beliren "Yeni Sipariş Alındı" bildirimi (site.js'in sağ-alt toast'ından ayrı).
function liveToast(o) {
    let box = document.getElementById("live-toast-box");
    if (!box) {
        box = document.createElement("div");
        box.id = "live-toast-box";
        document.body.appendChild(box);
    }
    const t = document.createElement("div");
    t.className = "live-toast";
    t.innerHTML = `<span class="lt-ico">🛒</span>
        <div><strong>Yeni Sipariş Alındı!</strong>
        <div class="lt-sub">${esc(o.city)} · ${o.total.toLocaleString("tr-TR")} TL</div></div>`;
    box.appendChild(t);
    requestAnimationFrame(() => t.classList.add("show"));
    setTimeout(() => {
        t.classList.remove("show");
        setTimeout(() => t.remove(), 350);
    }, 3200);
}

document.getElementById("live-toggle")?.addEventListener("click", () => {
    const btn = document.getElementById("live-toggle");
    livePaused = !livePaused;
    if (livePaused) {
        stopLiveFeed();
        btn.textContent = "▶ Devam Et";
        btn.classList.add("is-paused");
    } else {
        btn.textContent = "⏸ Duraklat";
        btn.classList.remove("is-paused");
        startLiveFeed();
    }
});

/* ===== Alt-sekme 2: Bölgesel Harita (GERÇEK veri: il → 7 bölge) =====
   Eskiden bölgelere sabit demo sayılar gömülüydü. Artık /admin/orders/region-stats'ten
   gelen gerçek il sipariş/ciro verisi 7 coğrafi bölgeye toplanır; renk yoğunluğu gerçek
   sipariş sayısına göre koyulaşır, tooltip gerçek sipariş/ciro/pay/il sayısını gösterir. */

let regionListenersReady = false;

// 7 coğrafi bölge — yalnızca yerleşim (ülkeyi kabaca yansıtan döşemeler). Sayılar canlı doldurulur.
const REGIONS = [
    { key: "marmara",   ad: "Marmara",           x: 40,  y: 40,  w: 200, h: 120 },
    { key: "karadeniz", ad: "Karadeniz",         x: 250, y: 20,  w: 470, h: 90  },
    { key: "ege",       ad: "Ege",               x: 40,  y: 170, w: 170, h: 150 },
    { key: "icAnadolu", ad: "İç Anadolu",        x: 250, y: 120, w: 300, h: 150 },
    { key: "akdeniz",   ad: "Akdeniz",           x: 220, y: 280, w: 330, h: 100 },
    { key: "doguAnd",   ad: "Doğu Anadolu",      x: 560, y: 120, w: 200, h: 150 },
    { key: "gunAnd",    ad: "Güneydoğu Anadolu", x: 560, y: 280, w: 200, h: 100 }
];

// 81 il → bölge. Anahtarlar normalize (Türkçe küçük harf) tutulur; sık kullanılan birkaç
// kısaltma/eş ad da eklenir ki serbest metinle girilen iller de eşleşsin.
const IL_BOLGE_HAM = {
    marmara: ["İstanbul", "Edirne", "Kırklareli", "Tekirdağ", "Çanakkale", "Balıkesir", "Bursa", "Yalova", "Kocaeli", "Sakarya", "Bilecik"],
    ege: ["İzmir", "Manisa", "Aydın", "Denizli", "Muğla", "Uşak", "Kütahya", "Afyonkarahisar", "Afyon"],
    akdeniz: ["Antalya", "Isparta", "Burdur", "Mersin", "İçel", "Adana", "Osmaniye", "Hatay", "Kahramanmaraş", "Maraş"],
    icAnadolu: ["Ankara", "Konya", "Kayseri", "Eskişehir", "Sivas", "Yozgat", "Çankırı", "Kırıkkale", "Kırşehir", "Nevşehir", "Niğde", "Aksaray", "Karaman"],
    karadeniz: ["Zonguldak", "Karabük", "Bartın", "Kastamonu", "Çorum", "Sinop", "Samsun", "Amasya", "Tokat", "Ordu", "Giresun", "Trabzon", "Rize", "Artvin", "Gümüşhane", "Bayburt", "Bolu", "Düzce"],
    doguAnd: ["Erzurum", "Erzincan", "Ağrı", "Kars", "Ardahan", "Iğdır", "Van", "Muş", "Bitlis", "Bingöl", "Tunceli", "Elazığ", "Malatya", "Hakkari", "Hakkâri"],
    gunAnd: ["Gaziantep", "Antep", "Şanlıurfa", "Urfa", "Diyarbakır", "Mardin", "Batman", "Şırnak", "Siirt", "Adıyaman", "Kilis"]
};
// Türkçe "I sorunu": veride il adı bazen düz I ile ("Istanbul") bazen "İstanbul" girilir.
// Eşleştirmenin kaçırmaması için TÜM i-varyantlarını (İ/I/ı) tek "i"ye indirip küçültürüz.
// Aynı normalizasyon hem harita anahtarlarına hem gelen veriye uygulandığı için tutarlıdır.
const normIl = (s) => (s || "")
    .replace(/İ/g, "i").replace(/I/g, "i").replace(/ı/g, "i")
    .toLowerCase().trim();
const IL_BOLGE = {};
for (const [bolge, iller] of Object.entries(IL_BOLGE_HAM)) {
    iller.forEach(il => { IL_BOLGE[normIl(il)] = bolge; });
}

function regionColor(orders, max) {
    // Ferah/aydınlık tema: açık pastel maviden doygun mora doğru. Veri yoksa nötr açık.
    if (max <= 0) return "hsl(250 30% 92%)";
    const t = Math.min(1, orders / max);
    const light = 92 - t * 42;           // %92 → %50 açıklık
    return `hsl(250 70% ${light}%)`;
}

// Gerçek il verisini çekip 7 bölgeye toplar; her seferinde (harita sekmesi açıldığında) tazelenir.
async function buildRegionMap() {
    const host = document.getElementById("region-map");
    const tip = document.getElementById("map-tooltip");

    // Bölge birikeç kutuları (her açılışta sıfırdan).
    const acc = Object.fromEntries(REGIONS.map(r => [r.key, { orders: 0, revenue: 0, cities: 0, topCity: null, topCityOrders: 0 }]));
    let unmatchedOrders = 0, unmatchedRevenue = 0;
    const unmatchedCities = [];

    try {
        const iller = await apiGet("/admin/orders/region-stats");
        iller.forEach(p => {
            const key = IL_BOLGE[normIl(p.city)];
            if (!key) {
                unmatchedOrders += p.orderCount;
                unmatchedRevenue += p.totalRevenue;
                if (p.orderCount > 0) unmatchedCities.push(p.city);
                return;
            }
            const a = acc[key];
            a.orders += p.orderCount;
            a.revenue += p.totalRevenue;
            a.cities += 1;
            if (p.orderCount > a.topCityOrders) { a.topCityOrders = p.orderCount; a.topCity = p.city; }
        });
    } catch {
        host.innerHTML = "<p class='empty' style='padding:24px'>Bölge verisi yüklenemedi.</p>";
        return;
    }

    // REGIONS + canlı veriyi birleştir (tooltip ve renk için).
    const data = REGIONS.map(r => ({ ...r, ...acc[r.key] }));
    const byKey = Object.fromEntries(data.map(r => [r.key, r]));
    const maxOrders = Math.max(...data.map(r => r.orders), 0);
    const toplamSiparis = data.reduce((s, r) => s + r.orders, 0);

    const tiles = data.map(r => `
        <g class="region-tile" data-key="${r.key}" tabindex="0">
            <rect x="${r.x}" y="${r.y}" width="${r.w}" height="${r.h}" rx="16"
                  fill="${regionColor(r.orders, maxOrders)}"></rect>
            <text x="${r.x + r.w / 2}" y="${r.y + r.h / 2 - 8}" text-anchor="middle"
                  dominant-baseline="middle" class="region-label">${esc(r.ad)}</text>
            <text x="${r.x + r.w / 2}" y="${r.y + r.h / 2 + 12}" text-anchor="middle"
                  dominant-baseline="middle" class="region-sublabel">${r.orders.toLocaleString("tr-TR")} sipariş</text>
        </g>`).join("");

    const note = unmatchedOrders > 0
        ? `<p class="region-note">${unmatchedOrders.toLocaleString("tr-TR")} sipariş bir bölgeye eşleşmedi${unmatchedCities.length ? ` (${esc([...new Set(unmatchedCities)].slice(0, 5).join(", "))}${unmatchedCities.length > 5 ? "…" : ""})` : ""}.</p>`
        : "";

    host.innerHTML = `<svg viewBox="0 0 800 400" class="region-svg" role="img"
        aria-label="Bölgelere göre sipariş yoğunluğu haritası">${tiles}</svg>${note}`;

    const showTip = (key, clientX, clientY) => {
        const r = byKey[key];
        if (!r) return;
        const pay = toplamSiparis > 0 ? (r.orders / toplamSiparis * 100).toFixed(1) : "0.0";
        tip.innerHTML = `<strong>${esc(r.ad)}</strong>
            <div class="mt-row"><span>Sipariş</span><b>${r.orders.toLocaleString("tr-TR")}</b></div>
            <div class="mt-row"><span>Ciro</span><b>${r.revenue.toLocaleString("tr-TR")} TL</b></div>
            <div class="mt-row"><span>Pay</span><b>%${pay}</b></div>
            <div class="mt-row"><span>İl sayısı</span><b>${r.cities}</b></div>
            ${r.topCity ? `<div class="mt-row"><span>En çok</span><b>${esc(r.topCity)}</b></div>` : ""}`;
        tip.hidden = false;
        const box = host.getBoundingClientRect();
        let left = clientX - box.left + 14;
        let top = clientY - box.top + 14;
        // Sağ/alt kenardan taşarsa içeri al
        if (left + tip.offsetWidth > box.width) left = box.width - tip.offsetWidth - 8;
        if (top + tip.offsetHeight > box.height) top = box.height - tip.offsetHeight - 8;
        tip.style.left = `${Math.max(4, left)}px`;
        tip.style.top = `${Math.max(4, top)}px`;
    };

    // Dinleyiciler host üzerinde bir kez kurulur (innerHTML değişse de host aynı kaldığı için sürer).
    if (!regionListenersReady) {
        regionListenersReady = true;
        host.addEventListener("mousemove", (e) => {
            const g = e.target.closest(".region-tile");
            if (g) showTipRef.fn(g.dataset.key, e.clientX, e.clientY);
            else tip.hidden = true;
        });
        host.addEventListener("mouseleave", () => { tip.hidden = true; });
        host.addEventListener("focusin", (e) => {
            const g = e.target.closest(".region-tile");
            if (!g) return;
            const rect = g.getBoundingClientRect();
            showTipRef.fn(g.dataset.key, rect.left + rect.width / 2, rect.top + rect.height / 2);
        });
        host.addEventListener("focusout", () => { tip.hidden = true; });
    }
    // showTip her yüklemede yeni veriyle kapanır; dinleyiciler güncel sürümü buradan çağırır.
    showTipRef.fn = showTip;
}
// Dinleyiciler kapanışı bir kez bağlanır; güncel showTip'i bu referans üzerinden çağırır.
const showTipRef = { fn: () => {} };
