// Tema claro/oscuro de la suite: la preferencia se guarda por navegador en `flit-theme` y se aplica con la clase
// `dark` de <html>. Sin "use client": un layout del servidor puede poner este script en <head> para que la página
// no destelle en claro antes de hidratar (lo usa el hub; Trámites lo aplica al montar su barra).
//
// HU #14159: Dark Mode temporalmente deshabilitado — forzar claro y limpiar preferencia oscura previa.
export const THEME_STORAGE_KEY = "flit-theme";

/** `false` mientras el modo oscuro no esté listo para producción (HU #14159). */
export const DARK_MODE_ENABLED = false;

export const THEME_COMING_SOON_MESSAGE = "Modo oscuro — próximamente disponible";

export const THEME_INIT_SCRIPT = DARK_MODE_ENABLED
  ? `try{if(localStorage.getItem("${THEME_STORAGE_KEY}")==="dark")document.documentElement.classList.add("dark")}catch(e){}`
  : `try{document.documentElement.classList.remove("dark");if(localStorage.getItem("${THEME_STORAGE_KEY}")==="dark")localStorage.setItem("${THEME_STORAGE_KEY}","light")}catch(e){}`;
