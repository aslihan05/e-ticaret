if (!localStorage.getItem("token")) {
    window.location.href = "login.html";
}

const STATUS = {
    0: { text: "Bekliyor", class: "status-pending" },
    1: { text: "Onaylandı", class: "status-approved" },
    2: { text: "Reddedildi", class: "status-rejected" },
    3: { text: "Kargoda", class: "status-shipped" },
    4: { text: "Teslim Edildi", class: "status-delivered" }
};

async function loadOrders() {
    const orders = await apiGet("/orders");
    const container = document.getElementById("orders-list");
    container.innerHTML = "";

    if (orders.length === 0) {
        container.innerHTML = "<p class='empty'>Henüz siparişin yok. <a href='index.html'>Alışverişe başla →</a></p>";
        return;
    }

    for (const o of orders) {
        const s = STATUS[o.status];
        const date = new Date(o.createdAt).toLocaleString("tr-TR");
        // Reddedilen kalemler üstü çizili gösterilir ve toplama katılmaz
        const itemsHtml = o.orderItems.map(i => {
            const rejected = i.status === 2;
            return `<li class="${rejected ? "item-rejected" : ""}">${i.product.name} × ${i.quantity} — ${i.unitPrice * i.quantity} TL${rejected ? " (reddedildi)" : ""}</li>`;
        }).join("");
        const total = o.orderItems.filter(i => i.status !== 2)
            .reduce((sum, i) => sum + i.unitPrice * i.quantity, 0);

        const card = document.createElement("div");
        card.className = "order-card";
        card.innerHTML = `
            <div class="order-header">
                <div>
                    <strong>Sipariş #${o.id}</strong>
                    <span class="order-date">${date}</span>
                </div>
                <span class="status ${s.class}">${s.text}</span>
            </div>
            <ul class="order-items">${itemsHtml}</ul>
            <div class="order-total">Toplam: <strong>${total} TL</strong></div>`;
        container.appendChild(card);
    }
}

setupNav();
updateCartCount();
loadOrders();
