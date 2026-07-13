
/*api.js projenin frontend'deki en önemli dosyası.backend'le konuşmanın bütün teknik ayrıntısını tek yerde toplar.*/ 


const API_URL = "http://localhost:5113/api"

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
    if (!response.ok) throw new Error(`İstek başarısız: ${response.status}`);
}