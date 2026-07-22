document.getElementById("register-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    const message = document.getElementById("message");

    try {
        const result = await apiPost("/auth/register", {
            username: document.getElementById("username").value,
            email: document.getElementById("email").value,
            // Opsiyonel alanlar: boşsa null gönderilir, backend de null olarak kaydeder
            phone: document.getElementById("phone").value.trim() || null,
            address: document.getElementById("address").value.trim() || null,
            password: document.getElementById("password").value
        });

        message.style.color = "green";
        message.textContent =  result.message;
    } catch (err) {
        message.style.color = "red";
        message.textContent = err.message;

    }
})