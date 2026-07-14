if (!localStorage.getItem("token")) {
    window.location.href = "login.html";
}

async function loadCart() {
    const items = await apiGet("/cart");
    console.log("Sepet Verisi:", items);
    const container = document.getElementById("cart-items");
    const summary = document.querySelector(".cart-summary");
    container.innerHTML = "";

    if (items.length === 0) {
        container.innerHTML = "<p class='empty'>Sepetin boş. <a href='index.html'>Alışverişe başla →</a></p>";
        summary.style.display = "none";
        return;
    }
    summary.style.display = "flex";

    // Sepette indirimli fiyat gösterilir; asıl fiyatı sipariş anında backend belirler
    const unitPrice = (prod) => {
        const now = new Date();
        const active = prod.discountPrice != null && prod.discountPrice < prod.price
            && (!prod.discountStart || new Date(prod.discountStart) <= now)
            && (!prod.discountEnd || new Date(prod.discountEnd) >= now);
        return active ? prod.discountPrice : prod.price;
    };

    let total = 0;
    for (const item of items) {
        const price = unitPrice(item.product);
        total += price * item.quantity;
        const priceLabel = price < item.product.price
            ? `<span class="price-old">${item.product.price} TL</span> <span class="price-new">${price} TL</span>`
            : `${price} TL`;
        const row = document.createElement("div");
        row.className = "cart-item";
        row.innerHTML = `
            <img src="${item.product.imageUrl}" alt="${item.product.name}">
            <div class="cart-item-info">
                <h3>${item.product.name}</h3>
                <span>${priceLabel}</span>
            </div>
           <div class="qty-controls">
                <button class="qty-btn" data-id="${item.id}" data-qty="${item.quantity - 1}">−</button>
                <span>${item.quantity}</span>
                <button class="qty-btn" 
                        data-id="${item.id}" 
                        data-qty="${item.quantity + 1}"
                        ${item.quantity >= item.product.stock ? "disabled title='Stok sınırına ulaştınız'" : ""}>+</button>
            </div>
            <strong>${price * item.quantity} TL</strong>
            <button class="remove-btn" data-id="${item.id}">🗑</button>`;
        container.appendChild(row);
    }
    document.getElementById("cart-total").textContent = `${total} TL`;
}

document.getElementById("cart-items").addEventListener("click", async (e) => {
    const qtyBtn = e.target.closest(".qty-btn");
    const removeBtn = e.target.closest(".remove-btn");

    try {
        if (qtyBtn) {
            const qty = Number(qtyBtn.dataset.qty);
            if (qty < 1) {
                await apiDelete(`/cart/${qtyBtn.dataset.id}`);
            } else {
                await apiPut(`/cart/${qtyBtn.dataset.id}`, { quantity: qty });
            }
        } else if (removeBtn) {
            await apiDelete(`/cart/${removeBtn.dataset.id}`);
        } else {
            return;
        }
        await loadCart();
        await updateCartCount();
    } catch (err) {
        alert(err.message);
    }
});

document.getElementById("checkout-btn").addEventListener("click", async () => {
    const message = document.getElementById("message");
    try {
        await apiPost("/orders", {});
        message.style.color = "green";
        message.textContent = "Siparişin alındı! Admin onayından sonra hazırlanacak. 🎉";
        await loadCart();
        await updateCartCount();
    } catch (err) {
        message.style.color = "red";
        message.textContent = err.message;
    }
});

setupNav();
updateCartCount();
loadCart();
