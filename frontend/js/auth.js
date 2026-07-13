document.getElementById("login-form").addEventListener("submit",async (e) => {           // Bu form gönderilmeye çalışıldığında şu fonksiyonu çalıltır.
    e.preventDefault();  // Tarayıcının varsayılan davranışını iptal eder
    const message = document.getElementById("message");

    try {
        const result = await apiPost("/auth/login" , {
            username: document.getElementById("username").value,
            password: document.getElementById("password").value
        });

        localStorage.setItem("token", result.token);  // localStrage -> Tarayıcının küçük deposu
        localStorage.setItem("username", result.username);
        localStorage.setItem("role", result.role);
         
        window.location.href = "index.html";

    }catch (err) {
        message.textContent = err.message;
    }
});