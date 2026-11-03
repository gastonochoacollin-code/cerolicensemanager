let csrfToken = null;
let setupRequired = false;
const form = document.getElementById("loginForm");
const error = document.getElementById("error");

async function loadState() {
  const response = await fetch("/api/auth/csrf", { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) throw new Error("No se pudo preparar el acceso.");
  const state = await response.json();
  csrfToken = state.csrfToken;
  setupRequired = state.setupRequired;
  document.getElementById("heading").textContent = setupRequired ? "Crear contraseña de Gaston" : "Acceso de Gaston";
  document.getElementById("description").textContent = setupRequired
    ? "Alta inicial local. Usa al menos 14 caracteres, mayúscula, minúscula, número y símbolo."
    : "Introduce la contraseña del administrador.";
  document.getElementById("confirmRow").hidden = !setupRequired;
  document.getElementById("password").autocomplete = setupRequired ? "new-password" : "current-password";
  document.getElementById("submitButton").textContent = setupRequired ? "Crear cuenta" : "Iniciar sesión";
}

form.addEventListener("submit", async event => {
  event.preventDefault();
  error.textContent = "";
  const password = document.getElementById("password").value;
  if (setupRequired && password !== document.getElementById("confirmPassword").value) {
    error.textContent = "Las contraseñas no coinciden.";
    return;
  }
  try {
    const response = await fetch(setupRequired ? "/api/auth/setup" : "/api/auth/login", {
      method: "POST",
      credentials: "same-origin",
      cache: "no-store",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrfToken },
      body: JSON.stringify({ password })
    });
    if (!response.ok) {
      error.textContent = response.status === 429 ? "Demasiados intentos. Espera unos minutos."
        : setupRequired && response.status === 409 ? "La cuenta ya fue creada. Recarga la página."
        : setupRequired && response.status === 400 ? "La contraseña no cumple los requisitos."
        : "No se pudo iniciar sesión.";
      return;
    }
    window.location.replace("/index.html");
  } catch {
    error.textContent = "No se pudo conectar con la aplicación.";
  }
});

loadState().catch(() => { error.textContent = "No se pudo preparar el acceso."; });
