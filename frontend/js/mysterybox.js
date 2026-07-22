// Gizemli Hediye Kutuları — istemci tarafı.
//
// Önemli: Ödül BURADA belirlenmez. Kullanıcı bir kutuya tıklar, sunucu (POST /mysterybox/open)
// hangi ödülün kazanıldığını ve kaçırılan 2 alternatifi döner. İstemci yalnızca bu sonucu
// sahneler — kod tarafında ödül seçmek/etkilemek mümkün değil (backend korumalı).

if (!requireAuth()) throw new Error("Oturum gerekli");

const stage = document.getElementById("mystery-stage");
const resultBox = document.getElementById("mystery-result");

let secimYapildi = false;   // seçim yapıldıktan sonra diğer kutulara tıklanamaz

// Bugünün kutusu açıldığında (ya da zaten açılmış olduğu görüldüğünde) işaretlenir:
// nav'daki 🎁 bağlantısının dikkat çekici efekti bu işarete göre susturulur (site.js).
function hediyeAcildiIsaretle() {
    localStorage.setItem("giftOpenedDate", new Date().toDateString());
}

// Sayfa açılışı: bugün oynandı mı? Oynandıysa kutular yerine "yarın gel" gösterilir.
async function init() {
    try {
        const durum = await apiGet("/mysterybox");
        if (durum.canOpen) {
            renderBoxes();
        } else {
            hediyeAcildiIsaretle();   // bugünkü kutu zaten açılmış
            renderCooldown(durum);
        }
    } catch (err) {
        stage.innerHTML = `<p class="empty">Hediye kutuları yüklenemedi: ${esc(err.message)}</p>`;
    }
}

// Kapalı üç kutuyu basar.
function renderBoxes() {
    secimYapildi = false;
    resultBox.hidden = true;
    resultBox.innerHTML = "";
    stage.classList.remove("locked");
    stage.innerHTML = "";

    for (let i = 0; i < 3; i++) {
        const box = document.createElement("button");
        box.type = "button";
        box.className = "gift-box";
        box.dataset.index = i;
        box.setAttribute("aria-label", `${i + 1}. hediye kutusunu aç`);
        box.innerHTML = `
            <div class="gift-3d">
                <div class="gift-lid"><span class="gift-bow">🎀</span></div>
                <div class="gift-base">
                    <span class="gift-ribbon-v"></span>
                    <span class="gift-q">?</span>
                </div>
                <div class="gift-reveal" aria-hidden="true"></div>
            </div>`;
        box.addEventListener("click", () => openBox(box));
        stage.appendChild(box);
    }
}

// Bir kutuya tıklanınca: sunucudan sonucu al, tıklanan kutuyu aç, diğer ikisini soldur.
async function openBox(box) {
    if (secimYapildi) return;
    secimYapildi = true;
    stage.classList.add("locked");           // diğer kutular artık tıklanamaz
    box.classList.add("is-opening");

    let sonuc;
    try {
        sonuc = await apiPost("/mysterybox/open", {});
    } catch (err) {
        // Hata olursa kilidi aç, kullanıcı tekrar deneyebilsin
        secimYapildi = false;
        stage.classList.remove("locked");
        box.classList.remove("is-opening");
        showToast(err.message, "warn");
        return;
    }

    // Kutu açıldı: nav'daki efekt bugün için sussun
    hediyeAcildiIsaretle();

    // Sunucu "bugün zaten oynadın" derse kutuları bırak, cooldown ekranına geç
    if (sonuc.alreadyPlayed) {
        renderCooldown({ nextAvailableAt: sonuc.nextAvailableAt, lastPrize: sonuc.won });
        return;
    }

    const digerler = [...stage.querySelectorAll(".gift-box")].filter(b => b !== box);

    // Tıklanan kutu: açılır ve kazanılan ödülü gösterir.
    revealPrize(box, sonuc.won, true);
    box.classList.add("is-opened", "is-winner");

    // Diğer iki kutu: yarı saydam olur ve kaçırılan ödülleri gösterir (tıklanamaz).
    digerler.forEach((b, i) => {
        const kacirilan = sonuc.missed[i];
        setTimeout(() => {
            revealPrize(b, kacirilan, false);
            b.classList.add("is-opened", "is-missed");
        }, 260 + i * 160);   // sırayla, tatlı bir gecikmeyle
    });

    // Kazanç özeti + kupon kodu (kopyalanabilir) alttan belirir.
    setTimeout(() => showResult(sonuc.won), 700);
}

// Bir kutunun içine ödülü yazar. won=true ise kupon kodu da gösterilir.
function revealPrize(box, prize, won) {
    const reveal = box.querySelector(".gift-reveal");
    const min = prize.minOrderTotal ? `<small>${prize.minOrderTotal} TL üzeri</small>` : "";
    reveal.innerHTML = `
        <span class="gp-emoji">${esc(prize.emoji || "🎁")}</span>
        <strong class="gp-label">${esc(prize.label)}</strong>
        ${won ? `<span class="gp-code">${esc(prize.code)}</span>` : `<span class="gp-missed">Kaçırdın</span>`}
        ${min}`;
}

// Alt bilgi kartı: kazanılan ödül + kupon kodu + kopyala + sepete git.
function showResult(prize) {
    const bitis = prize.expiresAt ? new Date(prize.expiresAt).toLocaleDateString("tr-TR") : null;
    resultBox.innerHTML = `
        <div class="mystery-win-card">
            <div class="win-emoji">${esc(prize.emoji || "🎁")}</div>
            <h3>Tebrikler! <span>${esc(prize.label)}</span> kazandın 🎉</h3>
            <p class="win-code-row">
                Kupon kodun:
                <button type="button" id="win-code" class="win-code" title="Kopyalamak için tıkla">${esc(prize.code)}</button>
            </p>
            ${bitis ? `<p class="win-exp">Son kullanım: ${bitis}</p>` : ""}
            <a href="cart.html" class="add-btn win-cta">Sepete Git & Kullan →</a>
        </div>`;
    resultBox.hidden = false;
    requestAnimationFrame(() => resultBox.classList.add("show"));

    document.getElementById("win-code").addEventListener("click", async (e) => {
        try {
            await navigator.clipboard.writeText(prize.code);
            const b = e.currentTarget;
            const eski = b.textContent;
            b.textContent = "Kopyalandı ✔";
            b.classList.add("copied");
            setTimeout(() => { b.textContent = eski; b.classList.remove("copied"); }, 1400);
        } catch {
            showToast("Kod kopyalanamadı, elle seçebilirsin.", "warn");
        }
    });
}

// Bugün oynanmışsa gösterilen ekran.
function renderCooldown(durum) {
    secimYapildi = true;
    stage.classList.remove("locked");
    stage.innerHTML = "";
    resultBox.hidden = true;

    const kalan = kalanSure(durum.nextAvailableAt);
    const p = durum.lastPrize;
    const odul = p
        ? `<div class="cooldown-prize"><span>${esc(p.emoji || "🎁")}</span>
             <div><strong>${esc(p.label)}</strong>
             ${p.code ? `<span class="gp-code">${esc(p.code)}</span>` : ""}</div></div>`
        : "";

    stage.innerHTML = `
        <div class="mystery-cooldown">
            <div class="cd-lock">⏳</div>
            <h3>Bugünkü kutunu zaten açtın!</h3>
            ${odul}
            <p>Yeni bir hediye kutusu için <strong>${esc(kalan)}</strong> sonra tekrar gel.</p>
            <a href="coupons.html" class="add-btn">Kuponlarıma Git</a>
        </div>`;
}

// "3 saat 12 dakika" gibi kalan süre metni.
function kalanSure(nextAvailableAt) {
    if (!nextAvailableAt) return "birazdan";
    const fark = new Date(nextAvailableAt) - new Date();
    if (fark <= 0) return "birazdan";
    const saat = Math.floor(fark / 3600000);
    const dk = Math.floor((fark % 3600000) / 60000);
    if (saat > 0) return `${saat} saat ${dk} dakika`;
    return `${dk} dakika`;
}

setupNav();
updateCartCount();
init();
