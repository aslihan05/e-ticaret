if (!localStorage.getItem("token") || localStorage.getItem("role") !== "Admin") {
    window.location.href = "login.html";
}

const STATUS = {
    0: { text: "Bekliyor", class: "status-pending" },
    1: { text: "Onaylandı", class: "status-approved" },
    2: { text: "Reddedildi", class: "status-rejected" },
    3: { text: "Kargoda", class: "status-shipped" },
    4: { text: "Teslim Edildi", class: "status-delivered" }
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

async function loadAdminOrders() {
    const orders = await apiGet("/admin/orders");
    const container = document.getElementById("admin-orders");

    if (orders.length === 0) {
        container.innerHTML = "<p class='empty'>Henüz sipariş yok.</p>";
        return;
    }

    const rows = orders.map(o => {
        const s = STATUS[o.status];
        // Reddedilen kalemler toplama katılmaz; bekleyen siparişte kalemler seçilebilir
        const total = o.orderItems.filter(i => i.status !== 2)
            .reduce((sum, i) => sum + i.unitPrice * i.quantity, 0);
        const items = o.status === 0
            ? o.orderItems.map(i =>
                `<label class="item-pick"><input type="checkbox" class="item-check" value="${i.id}" checked> ${i.product.name} ×${i.quantity}</label>`).join("")
            : o.orderItems.map(i => i.status === 2
                ? `<span class="item-rejected">${i.product.name} ×${i.quantity}</span>`
                : `${i.product.name} ×${i.quantity}`).join(", ");
        return `<tr>
            <td>#${o.id}</td>
            <td>${o.user.username}</td>
            <td>${fmtDate(o.createdAt)}</td>
            <td>${items}</td>
            <td>${total > 0 ? `${total} TL` : "-"}</td>
            <td><span class="status ${s.class}">${s.text}</span></td>
            <td>${orderActions(o)}</td>
        </tr>`;
    }).join("");

    container.innerHTML = `<table class="admin-table">
        <thead><tr><th>No</th><th>Müşteri</th><th>Tarih</th><th>Ürünler</th><th>Toplam</th><th>Durum</th><th>İşlem</th></tr></thead>
        <tbody>${rows}</tbody></table>`;
}

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


/* ===== Ürünler ===== */

let adminProducts = [];
let editingProductId = null;

async function fillCategorySelect() {
    const cats = await apiGet("/categories");
    document.getElementById("p-category").innerHTML =
        cats.map(c => `<option value="${c.id}">${c.name}</option>`).join("");
}

function priceCell(p) {
    return p.hasDiscount
        ? `<span class="price-old">${p.price} TL</span> <strong class="price-new">${p.discountedPrice} TL</strong>`
        : `${p.price} TL`;
}

async function loadAdminProducts() {
    await fillCategorySelect();
    adminProducts = await apiGet("/products?includeInactive=true");
    const container = document.getElementById("admin-products");

    const rows = adminProducts.map(p => {
        const actions = p.isActive
            ? `<button class="btn-edit" data-id="${p.id}">Düzenle</button>
               <button class="btn-delete" data-id="${p.id}">Sil</button>`
            : `<button class="btn-activate" data-id="${p.id}">Aktifleştir</button>`;
        return `<tr class="${p.isActive ? "" : "row-inactive"}">
            <td>${p.imageUrl ? `<img src="${p.imageUrl}" alt="">` : "-"}</td>
            <td>${p.name}${p.isActive ? "" : ' <span class="badge-inactive">pasif</span>'}</td>
            <td>${priceCell(p)}</td>
            <td>
                <input type="number" class="stock-input" min="0" value="${p.stock}">
                <button class="btn-stock" data-id="${p.id}" title="Stoku kaydet">✓</button>
            </td>
            <td>${p.category?.name ?? "-"}</td>
            <td>${actions}</td>
        </tr>`;
    }).join("");

    container.innerHTML = `<table class="admin-table">
        <thead><tr><th></th><th>Ürün</th><th>Fiyat</th><th>Stok</th><th>Kategori</th><th>İşlem</th></tr></thead>
        <tbody>${rows}</tbody></table>`;
}

document.getElementById("product-form").addEventListener("submit", async (e) => {
    e.preventDefault();

    // İndirim bilgisi bu formda yok; güncellemede mevcut indirim kaybolmasın diye korunur
    const orig = editingProductId ? adminProducts.find(x => x.id === editingProductId) : null;

    const dto = {
        name: document.getElementById("p-name").value,
        description: document.getElementById("p-desc").value,
        price: Number(document.getElementById("p-price").value),
        stock: Number(document.getElementById("p-stock").value),
        imageUrl: document.getElementById("p-image").value || null,
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

    if (editBtn) {
        const p = adminProducts.find(x => x.id === Number(editBtn.dataset.id));
        editingProductId = p.id;
        document.getElementById("p-name").value = p.name;
        document.getElementById("p-desc").value = p.description ?? "";
        document.getElementById("p-price").value = p.price;
        document.getElementById("p-stock").value = p.stock;
        document.getElementById("p-image").value = p.imageUrl ?? "";
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

// datetime-local input "2026-07-15T10:00" ister; API'den "2026-07-15T10:00:00" gelir
const toInputDate = (d) => d ? d.slice(0, 16) : "";

async function loadAdminDiscounts() {
    adminProducts = await apiGet("/products?includeInactive=true");
    const container = document.getElementById("admin-discounts");
    const active = adminProducts.filter(p => p.isActive);

    if (active.length === 0) {
        container.innerHTML = "<p class='empty'>Aktif ürün yok.</p>";
        return;
    }

    const rows = active.map(p => `<tr>
        <td>${p.name}</td>
        <td>${p.price} TL</td>
        <td><input type="number" class="disc-price" min="0" step="0.01" placeholder="İndirimli fiyat" value="${p.discountPrice ?? ""}"></td>
        <td><input type="datetime-local" class="disc-start" value="${toInputDate(p.discountStart)}"></td>
        <td><input type="datetime-local" class="disc-end" value="${toInputDate(p.discountEnd)}"></td>
        <td>${p.hasDiscount ? '<span class="status status-approved">İndirimde</span>' : "-"}</td>
        <td>
            <button class="btn-edit btn-disc-save" data-id="${p.id}">Kaydet</button>
            <button class="btn-delete btn-disc-clear" data-id="${p.id}" title="İndirimi kaldır">🗑</button>
        </td>
    </tr>`).join("");

    container.innerHTML = `<table class="admin-table">
        <thead><tr><th>Ürün</th><th>Fiyat</th><th>İndirimli Fiyat</th><th>Başlangıç</th><th>Bitiş</th><th>Durum</th><th>İşlem</th></tr></thead>
        <tbody>${rows}</tbody></table>`;
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
            discountStart: tr.querySelector(".disc-start").value || null,
            discountEnd: tr.querySelector(".disc-end").value || null
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

/* ===== Kategoriler ===== */

async function loadAdminCategories() {
    const cats = await apiGet("/categories");
    document.getElementById("admin-categories").innerHTML = cats.map(c =>
        `<li>${c.name}
            <span>
                <button class="btn-edit btn-rename" data-id="${c.id}" data-name="${c.name}">Yeniden Adlandır</button>
                <button class="btn-delete" data-id="${c.id}">Sil</button>
            </span>
        </li>`).join("");
}

document.getElementById("category-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    try {
        await apiPost("/categories", { name: document.getElementById("category-name").value });
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
        try {
            await apiPut(`/categories/${renameBtn.dataset.id}`, { name: newName });
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

/* ===== Kullanıcılar ===== */

async function loadAdminUsers() {
    const users = await apiGet("/admin/users");
    const me = localStorage.getItem("username");

    const rows = users.map(u => {
        const isMe = u.username === me;
        const roleSelect = `<select class="user-role" data-id="${u.id}" ${isMe ? "disabled" : ""}>
            <option value="Customer" ${u.role === "Customer" ? "selected" : ""}>Customer</option>
            <option value="Admin" ${u.role === "Admin" ? "selected" : ""}>Admin</option>
        </select>`;
        return `<tr>
            <td>${u.id}</td>
            <td>${u.username}${isMe ? " (sen)" : ""}</td>
            <td>${roleSelect}</td>
            <td>${fmtDate(u.createdAt)}</td>
            <td>
                <button class="btn-edit btn-passwd" data-id="${u.id}">Şifre Belirle</button>
                ${isMe ? "" : `<button class="btn-delete" data-id="${u.id}">Sil</button>`}
            </td>
        </tr>`;
    }).join("");

    document.getElementById("admin-users").innerHTML = `<table class="admin-table">
        <thead><tr><th>Id</th><th>Kullanıcı</th><th>Rol</th><th>Kayıt Tarihi</th><th>İşlem</th></tr></thead>
        <tbody>${rows}</tbody></table>`;
}

document.getElementById("user-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    try {
        const r = await apiPost("/admin/users", {
            username: document.getElementById("u-name").value,
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
    if (!sel) return;
    try {
        const r = await apiPut(`/admin/users/${sel.dataset.id}`, { role: sel.value });
        showMessage(r.message);
    } catch (err) {
        showMessage(err.message, false);
        loadAdminUsers();
    }
});

document.getElementById("admin-users").addEventListener("click", async (e) => {
    const passBtn = e.target.closest(".btn-passwd");
    const deleteBtn = e.target.closest(".btn-delete");

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

async function loadAdminLogs() {
    const logs = await apiGet("/admin/logs");

    const rows = logs.map(l => `<tr>
        <td>${l.id}</td>
        <td>${l.userId ?? "-"}</td>
        <td>${l.action}</td>
        <td>${l.details ?? ""}</td>
        <td>${fmtDate(l.timestamp)}</td>
    </tr>`).join("");

    document.getElementById("admin-logs").innerHTML = `<table class="admin-table">
        <thead><tr><th>Id</th><th>Kullanıcı</th><th>İşlem</th><th>Detay</th><th>Zaman</th></tr></thead>
        <tbody>${rows}</tbody></table>`;
}

/* ===== Analiz ===== */

async function loadAnalytics() {
    const a = await apiGet("/admin/analytics");
    document.getElementById("admin-analytics").innerHTML = `
        <div class="stats-grid">
            <div class="stat-card"><span>Toplam Sipariş</span><strong>${a.totalOrders}</strong></div>
            <div class="stat-card"><span>Onaylanan</span><strong>${a.approvedOrders}</strong></div>
            <div class="stat-card"><span>Bekleyen</span><strong>${a.pendingOrders}</strong></div>
            <div class="stat-card"><span>Reddedilen</span><strong>${a.rejectedOrders}</strong></div>
            <div class="stat-card"><span>Toplam Gelir</span><strong>${a.totalRevenue} TL</strong></div>
            <div class="stat-card"><span>En Çok Satan</span><strong>${a.bestSellingProduct ?? "-"}${a.bestSellingProductQuantity ? ` (${a.bestSellingProductQuantity} adet)` : ""}</strong></div>
        </div>`;
}

/* ===== Açılış ===== */

setupNav();
updateCartCount();
loadAdminOrders();
