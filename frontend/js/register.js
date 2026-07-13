document.getElementById("register-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    const message = document.getElementById("message");

    try {
        const result = await apiPost("/auth/register", {
            username: document.getElementById("username").value,
            password: document.getElementById("password").value
        });

        message.style.color = "green";
        message.textContent =  result.message;
    } catch (err) {
        message.style.color = "red";
        message.textContent = err.message;

    }
})