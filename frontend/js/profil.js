// Geçerli (süresi dolmamış) oturum yoksa login'e yönlendirir
if (!requireAuth()) throw new Error("Oturum gerekli");

// Kullanıcı adı ve rol sunucudan gelir; localStorage kurcalanabildiği için ona güvenilmez.
async function loadProfile() {
    const form = document.getElementById("profile-form");

    try {
        const p = await apiGet("/profile");

        document.getElementById("username").value = p.username;
        document.getElementById("email").value = p.email ?? "";
        document.getElementById("fullname").value = p.fullName ?? "";
        document.getElementById("phone").value = p.phone ?? "";
        document.getElementById("city").value = p.city ?? "";
        document.getElementById("district").value = p.district ?? "";
        document.getElementById("address").value = p.address ?? "";

        // E-postası olmayan kullanıcı bildirim alamıyor — sebebini görsün
        document.getElementById("email-warning").style.display = p.email ? "none" : "block";

        document.getElementById("profile-meta").textContent =
            `${p.role === "Admin" ? "Yönetici" : "Müşteri"} hesabı • Üyelik: ${new Date(p.createdAt).toLocaleDateString("tr-TR")}`;
    } catch {
        form.style.display = "none";
        showMessage("Bilgilerin yüklenemedi. Sayfayı yenilemeyi dene.", false);
    }
}

function showMessage(text, ok = true) {
    const el = document.getElementById("message");
    el.textContent = text;
    el.className = `profile-message ${ok ? "msg-ok" : "msg-error"}`;
}

document.getElementById("profile-form").addEventListener("submit", async (e) => {
    e.preventDefault();

    const btn = document.getElementById("save-btn");
    btn.disabled = true;
    const eskiMetin = btn.textContent;
    btn.textContent = "Kaydediliyor…";

    try {
        const r = await apiPut("/profile", {
            email: document.getElementById("email").value.trim(),
            fullName: document.getElementById("fullname").value.trim(),
            phone: document.getElementById("phone").value.trim(),
            city: document.getElementById("city").value.trim(),
            district: document.getElementById("district").value.trim(),
            address: document.getElementById("address").value.trim()
        });
        showMessage(r.message);
        // Kaydedilen değerlerle (sunucudaki hâliyle) tazele: uyarı da güncellensin
        await loadProfile();
    } catch (err) {
        // Sunucunun mesajı gösterilir ("Bu e-posta adresi başka bir hesapta kayıtlı." gibi)
        showMessage(err.message, false);
    } finally {
        btn.disabled = false;
        btn.textContent = eskiMetin;
    }
});

setupNav();
updateCartCount();
loadProfile();
