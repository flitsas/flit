// Tema claro/oscuro de la suite: la preferencia se guarda por navegador en `flit-theme` y se aplica con la clase
// `dark` de <html>. Sin "use client": un layout del servidor puede poner este script en <head> para que la página
// no destelle en claro antes de hidratar (lo usa el hub; Trámites lo aplica al montar su barra).
//
// HU #14159 lo dejó apagado mientras no estuviera listo; el rediseño del UI kit (HU #14478) lo enciende con los tokens
// oscuros del kit en toda la suite.
export const THEME_STORAGE_KEY = "flit-theme";

/** Interruptor general del modo oscuro (apagado en HU #14159, encendido en HU #14478). */
export const DARK_MODE_ENABLED = true;

export const THEME_COMING_SOON_MESSAGE = "Modo oscuro — próximamente disponible";

export const THEME_INIT_SCRIPT = DARK_MODE_ENABLED
  ? `try{if(localStorage.getItem("${THEME_STORAGE_KEY}")==="dark")document.documentElement.classList.add("dark")}catch(e){}`
  : `try{document.documentElement.classList.remove("dark");if(localStorage.getItem("${THEME_STORAGE_KEY}")==="dark")localStorage.setItem("${THEME_STORAGE_KEY}","light")}catch(e){}`;
