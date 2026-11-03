let csrfToken = null;
const getCsrfToken = async () => {
  if (csrfToken) return csrfToken;
  const response = await fetch("/api/auth/csrf", { cache: "no-store", credentials: "same-origin" });
  if (!response.ok) throw new Error("No se pudo preparar la solicitud.");
  csrfToken = (await response.json()).csrfToken;
  return csrfToken;
};
const api = async (url, options = {}) => {
  const method = (options.method || "GET").toUpperCase();
  const headers = new Headers(options.headers || {});
  if (!["GET", "HEAD"].includes(method)) {
    headers.set("X-CSRF-TOKEN", await getCsrfToken());
    if (options.body && !(options.body instanceof FormData)) headers.set("Content-Type", "application/json");
  }
  const res = await fetch(url, { ...options, headers, credentials: "same-origin", cache: "no-store" });
  if (res.status === 401) { window.location.assign("/login.html"); throw new Error("Sesión vencida."); }
  if (!res.ok) {
    let error = "La operación no pudo completarse.";
    try { error = (await res.json()).error || error; } catch { /* respuesta genérica */ }
    throw new Error(error);
  }
  return res.headers.get("content-type")?.includes("application/json") ? res.json() : res.text();
};

const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g, character =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[character]);
const logout = async () => {
  await api("/api/auth/logout", { method: "POST" });
  window.location.assign("/login.html");
};

const nav = () => `
<nav>
  <strong>Cero License Manager</strong>
  <a href="/index.html">Dashboard</a>
  <a href="/clientes.html">Clientes</a>
  <a href="/generar-licencia.html">Generar licencia</a>
  <a href="/licencias.html">Licencias</a>
  <a href="/llaves.html">Llaves</a>
  <button type="button" class="secondary" onclick="logout()">Cerrar sesión</button>
</nav>`;

document.addEventListener("DOMContentLoaded", () => {
  document.body.insertAdjacentHTML("afterbegin", nav());
});

const fmtDate = value => value ? value.substring(0, 10) : "";
const download = url => { window.location.href = url; };
