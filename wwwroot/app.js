const api = async (url, options = {}) => {
  const res = await fetch(url, {
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
    ...options
  });
  if (!res.ok) throw new Error(await res.text());
  return res.headers.get("content-type")?.includes("application/json") ? res.json() : res.text();
};

const nav = () => `
<nav>
  <strong>Cero License Manager</strong>
  <a href="/index.html">Dashboard</a>
  <a href="/clientes.html">Clientes</a>
  <a href="/generar-licencia.html">Generar licencia</a>
  <a href="/licencias.html">Licencias</a>
  <a href="/llaves.html">Llaves</a>
</nav>`;

document.addEventListener("DOMContentLoaded", () => {
  document.body.insertAdjacentHTML("afterbegin", nav());
});

const fmtDate = value => value ? value.substring(0, 10) : "";
const download = url => { window.location.href = url; };
