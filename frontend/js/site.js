function setupNav() {
    const nav = document.getElementById("nav");
    const username = localStorage.getItem("username");

    if (username) {
        nav.innerHTML = `
            <span>Merhaba, ${username}</span>
            <a href="#" id="logout-link">Çıkış Yap</a>`;

        document.getElementById("logout-link").addEventListener("click", (e) => {
            e.preventDefault();
            localStorage.clear();
            window.location.href = "login.html";
        });
    } else {
        nav.innerHTML = `<a href="login.html">Giriş Yap</a>`;
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

async function addToCart(productId, button) {
    if (!localStorage.getItem("token")) {
        window.location.href = "login.html";
        return;
    }

    const oldText = button.textContent;
    button.disabled = true;

    try {
        await apiPost("/cart", { productId: productId, quantity: 1 });
        button.textContent = "Eklendi ✓";
        await updateCartCount();
        setTimeout(() => {
            button.textContent = oldText;
            button.disabled = false;
        }, 1200);
    } catch (err) {
        button.textContent = oldText;
        button.disabled = false;
        alert(err.message);
    }
}
