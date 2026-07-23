
/*api.js projenin frontend'deki en önemli dosyası.backend'le konuşmanın bütün teknik ayrıntısını tek yerde toplar.*/ 


// Geliştirmede frontend Live Server'dan (ör. 5500) servis edilir, API ayrı portta (5113) çalışır.
// Canlıda ise frontend'i API'nin kendisi sunar (aynı origin) — bu yüzden göreli "/api" yeterli
// ve doğrudur; "localhost" ziyaretçinin kendi bilgisayarını işaret ettiği için canlıda asla çalışmazdı.
const API_URL =
    (location.hostname === "localhost" || location.hostname === "127.0.0.1")
        ? "http://localhost:5113/api"
        : "/api";

function authHeaders() {
    const token = localStorage.getItem("token");
    return token ? { "Authorization": `Bearer ${token}` } : {};
}

async function apiPost(path, body) {
    const response = await fetch(`${API_URL}${path}`, {
        method: "POST",
        headers: { "Content-Type": "application/json", ...authHeaders() },
        body: JSON.stringify(body)
    });
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error(data?.message ?? `İstek başarısız: ${response.status}`);
    return data;
}

async function apiGet(path) {
    const response = await fetch(`${API_URL}${path}`, { headers: authHeaders() });
    if (!response.ok) throw new Error(`İstek başarısız: ${response.status}`);
    return response.json();
}


async function apiPut(path, body) {
    const response = await fetch (`${API_URL}${path}`, {
        method: "PUT",
        headers: {"Content-Type": "application/json", ...authHeaders() },
        body: JSON.stringify(body)
    });
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error(data?.message ?? `İstek başarısız: ${response.status}`);
    return data;
}

async function apiDelete(path) {
    const response = await fetch (`${API_URL}${path}`, {
        method: "DELETE",
        headers: authHeaders()
    });
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error(data?.message ?? `İstek başarısız: ${response.status}`);
    return data;
}