// Giriş ekranında geri tuşu her zaman karşılama (tanıtım) sayfasına götürür.
// Kullanıcı buraya nereden gelmiş olursa olsun (çıkış yaptıktan sonra, oturumu
// dolduğu için yönlendirilerek ya da doğrudan adresi yazarak) geri basınca
// korumalı bir sayfaya düşüp tekrar login'e atılmasın; index.html açılsın.
// Bunun için ekstra bir geçmiş kaydı bırakılır, geri basıldığında popstate
// yakalanır ve karşılama sayfasına gidilir.
history.pushState(null, "", location.href);
window.addEventListener("popstate", () => {
    window.location.replace("index.html");
});

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
         
        window.location.href = "shop.html";

    }catch (err) {
        message.textContent = err.message;
    }
});