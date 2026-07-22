// Ongima müşteri asistanı — sağ altta beliren sohbet balonu.
//
// Tek <script> etiketiyle taşınabilir olsun diye kendi stilini ve DOM'unu kendisi kurar.
// Yalnızca GİRİŞ YAPMIŞ müşteriye görünür (bot müşterinin kendi verisiyle çalışır).
// Backend'e POST /chatbot ile tüm sohbet geçmişini gönderir; kimlik JWT'den okunur.
(function () {
    "use strict";

    // site.js'teki escapeHtml varsa onu kullan; yoksa (o dosya yüklenmemişse) yerel bir kopya.
    const escapeHtmlLocal = (v) => v == null ? "" : String(v)
        .replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;").replaceAll("'", "&#39;");
    const escFn = (typeof esc === "function") ? esc : escapeHtmlLocal;

    // Giriş kontrolü: authInfo (site.js) varsa onu kullan, yoksa token varlığına bak.
    function girisVarMi() {
        if (typeof authInfo === "function") return !!authInfo();
        return !!localStorage.getItem("token");
    }

    // Sohbet geçmişi: [{role:"user"|"assistant", content:"..."}]. Backend'e olduğu gibi gider.
    const gecmis = [];
    let panelAcik = false;
    let bekliyor = false;

    function stilEkle() {
        if (document.getElementById("cbot-style")) return;
        const style = document.createElement("style");
        style.id = "cbot-style";
        style.textContent = `
        /* Buton aktifken sayfanın altına güvenli boşluk bırakılır: sabit (fixed) buton
           sağ altta yüzdüğü için, bu boşluk olmadan en alttaki ürünlerin/sayfalamanın
           üstüne biner. Yalnızca buton gerçekten basıldığında (giriş yapılmışsa) eklenir. */
        body.cbot-active{padding-bottom:104px}
        /* Geniş "pill" buton: yeşil nokta + 💬 + "Canlı Destek" yazısı. Küçük daire fark
           edilmiyordu; etiketli buton daha davetkâr. Ürünlerle çakışmaması, sağdaki içerik
           boşluğuyla (style.css'teki .cbot-active main padding-right) sağlanır. */
        #cbot-btn{position:fixed;right:24px;bottom:24px;height:62px;padding:0 28px 0 20px;border:none;
            border-radius:31px;background:linear-gradient(135deg,#7c3aed,#ec4899 55%,#f59e0b);
            background-size:220% 220%;color:#fff;font-size:17px;font-weight:700;cursor:pointer;
            z-index:9998;display:inline-flex;align-items:center;gap:11px;white-space:nowrap;
            box-shadow:0 10px 28px rgba(124,58,237,.5);transition:transform .15s, box-shadow .15s;
            animation:cbotPulse 2.2s ease-out infinite, cbotGradient 6s ease infinite}
        #cbot-btn:hover{transform:translateY(-3px) scale(1.03);box-shadow:0 16px 38px rgba(236,72,153,.6)}
        #cbot-btn .cbot-ico{font-size:25px;line-height:1;animation:cbotWiggle 2.8s ease-in-out infinite}
        /* "Çevrimiçi" yeşil nokta: yazının solunda, canlı desteğin açık olduğunu sezdirir */
        #cbot-btn .cbot-dot{width:12px;height:12px;border-radius:50%;background:#22c55e;flex:0 0 auto;
            box-shadow:0 0 0 3px rgba(34,197,94,.35);animation:cbotBlink 1.6s ease-in-out infinite}
        #cbot-btn .cbot-txt{line-height:1}
        @keyframes cbotPulse{
            0%{box-shadow:0 10px 28px rgba(124,58,237,.5),0 0 0 0 rgba(236,72,153,.55)}
            70%{box-shadow:0 10px 28px rgba(124,58,237,.5),0 0 0 20px rgba(236,72,153,0)}
            100%{box-shadow:0 10px 28px rgba(124,58,237,.5),0 0 0 0 rgba(236,72,153,0)}}
        @keyframes cbotGradient{0%{background-position:0% 50%}50%{background-position:100% 50%}100%{background-position:0% 50%}}
        @keyframes cbotWiggle{0%,86%,100%{transform:rotate(0)}89%{transform:rotate(-14deg)}92%{transform:rotate(12deg)}95%{transform:rotate(-8deg)}98%{transform:rotate(4deg)}}
        @keyframes cbotBlink{0%,100%{opacity:1}50%{opacity:.35}}
        @media (prefers-reduced-motion:reduce){#cbot-btn,#cbot-btn .cbot-ico,#cbot-btn .cbot-dot{animation:none}}
        /* Geniş pill yalnızca GENİŞ ekranlarda (≥1250px) sığar: orada sabit buton, ürün
           ızgarasının sağındaki boşlukta durur. Daha dar ekranlarda etiketli pill ürünlerin
           üstüne binmeden sığmaz; bu yüzden ikon-yalnız kompakt daireye düşürülür. */
        @media (max-width:1249px){#cbot-btn{width:56px;height:56px;padding:0;
                border-radius:50%;justify-content:center;gap:0}
            #cbot-btn .cbot-txt{display:none}
            #cbot-btn .cbot-ico{font-size:26px}
            #cbot-btn .cbot-dot{position:absolute;top:5px;right:5px;border:2px solid #fff}}
        /* En küçük ekranlarda daire köşeye biraz daha yaklaşır ve alt boşluk küçülür. */
        @media (max-width:480px){#cbot-btn{right:16px;bottom:16px;width:54px;height:54px}
            #cbot-btn .cbot-ico{font-size:25px}
            body.cbot-active{padding-bottom:80px}}
        #cbot-panel{position:fixed;right:20px;bottom:88px;width:340px;max-width:calc(100vw - 40px);
            height:460px;max-height:calc(100vh - 120px);background:#fff;border-radius:14px;
            box-shadow:0 8px 30px rgba(0,0,0,.28);z-index:9999;display:none;flex-direction:column;overflow:hidden}
        #cbot-panel.open{display:flex}
        #cbot-head{background:#5b3cc4;color:#fff;padding:12px 14px;font-weight:600;display:flex;
            align-items:center;justify-content:space-between}
        #cbot-head small{display:block;font-weight:400;opacity:.85;font-size:11px;margin-top:2px}
        #cbot-close{background:none;border:none;color:#fff;font-size:20px;cursor:pointer;line-height:1}
        #cbot-msgs{flex:1;overflow-y:auto;padding:12px;background:#f6f5fb;display:flex;flex-direction:column;gap:8px}
        .cbot-row{display:flex}
        .cbot-row.user{justify-content:flex-end}
        .cbot-bubble{max-width:80%;padding:8px 12px;border-radius:14px;font-size:14px;line-height:1.4;
            white-space:pre-wrap;word-wrap:break-word}
        .cbot-row.user .cbot-bubble{background:#5b3cc4;color:#fff;border-bottom-right-radius:4px}
        .cbot-row.bot .cbot-bubble{background:#fff;color:#222;border:1px solid #e3e0f0;border-bottom-left-radius:4px}
        .cbot-typing{font-size:13px;color:#777;font-style:italic;padding:2px 4px}
        #cbot-form{display:flex;gap:6px;padding:10px;border-top:1px solid #eee;background:#fff}
        #cbot-input{flex:1;border:1px solid #ccc;border-radius:20px;padding:9px 14px;font-size:14px;outline:none}
        #cbot-input:focus{border-color:#5b3cc4}
        #cbot-send{border:none;background:#5b3cc4;color:#fff;border-radius:20px;padding:0 16px;
            cursor:pointer;font-size:14px}
        #cbot-send:disabled{opacity:.5;cursor:default}`;
        document.head.appendChild(style);
    }

    function arayuzKur() {
        stilEkle();
        // Sabit buton sağ altta ürünlerin üstüne binmesin diye sayfaya alt boşluk aç.
        document.body.classList.add("cbot-active");

        const btn = document.createElement("button");
        btn.id = "cbot-btn";
        btn.type = "button";
        btn.setAttribute("aria-label", "Canlı Destek asistanını aç");
        btn.title = "Canlı Destek";
        btn.innerHTML = `<span class="cbot-dot" aria-hidden="true"></span><span class="cbot-ico" aria-hidden="true">💬</span><span class="cbot-txt">Canlı Destek</span>`;
        btn.addEventListener("click", panelDegistir);
        document.body.appendChild(btn);

        const panel = document.createElement("div");
        panel.id = "cbot-panel";
        panel.innerHTML = `
            <div id="cbot-head">
                <div>Ongima Asistan<small>Siparişleriniz, kuponlarınız ve ürünler için</small></div>
                <button id="cbot-close" type="button" aria-label="Kapat">×</button>
            </div>
            <div id="cbot-msgs" aria-live="polite"></div>
            <form id="cbot-form" autocomplete="off">
                <input id="cbot-input" type="text" placeholder="Bir şey sorun..." maxlength="500" aria-label="Mesajınız">
                <button id="cbot-send" type="submit">Gönder</button>
            </form>`;
        document.body.appendChild(panel);

        document.getElementById("cbot-close").addEventListener("click", panelDegistir);
        document.getElementById("cbot-form").addEventListener("submit", gonder);

        // İlk açılış karşılaması (yalnızca ekranda; modele geçmiş olarak GÖNDERİLMEZ).
        botMesajiEkle("Merhaba! 👋 Siparişleriniz, kuponlarınız, sepetiniz veya ürünler hakkında sorabilirsiniz.");
    }

    function panelDegistir() {
        panelAcik = !panelAcik;
        document.getElementById("cbot-panel").classList.toggle("open", panelAcik);
        if (panelAcik) document.getElementById("cbot-input").focus();
    }

    function satirEkle(rol, metin) {
        const msgs = document.getElementById("cbot-msgs");
        const row = document.createElement("div");
        row.className = "cbot-row " + (rol === "user" ? "user" : "bot");
        row.innerHTML = `<div class="cbot-bubble">${escFn(metin)}</div>`;
        msgs.appendChild(row);
        msgs.scrollTop = msgs.scrollHeight;
    }

    function botMesajiEkle(metin) { satirEkle("bot", metin); }

    async function gonder(e) {
        e.preventDefault();
        if (bekliyor) return;

        const input = document.getElementById("cbot-input");
        const metin = input.value.trim();
        if (!metin) return;

        input.value = "";
        satirEkle("user", metin);
        gecmis.push({ role: "user", content: metin });

        bekliyor = true;
        const sendBtn = document.getElementById("cbot-send");
        sendBtn.disabled = true;

        // "yazıyor..." göstergesi
        const msgs = document.getElementById("cbot-msgs");
        const typing = document.createElement("div");
        typing.className = "cbot-typing";
        typing.textContent = "yazıyor...";
        msgs.appendChild(typing);
        msgs.scrollTop = msgs.scrollHeight;

        try {
            const data = await apiPost("/chatbot", { messages: gecmis });
            const cevap = data?.reply ?? "Bir sorun oluştu, tekrar dener misiniz?";
            gecmis.push({ role: "assistant", content: cevap });
            typing.remove();
            botMesajiEkle(cevap);
        } catch (err) {
            typing.remove();
            botMesajiEkle("Bağlantı hatası: " + (err?.message ?? "asistan yanıt vermedi."));
        } finally {
            bekliyor = false;
            sendBtn.disabled = false;
            input.focus();
        }
    }

    function baslat() {
        if (!girisVarMi()) return;   // Yalnızca giriş yapmış müşteriye
        arayuzKur();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", baslat);
    } else {
        baslat();
    }
})();
