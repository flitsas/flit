// Tema claro/oscuro de la suite: la preferencia se guarda por navegador en `flit-theme` y se aplica con la clase
// `dark` de <html>. Sin "use client": un layout del servidor puede poner este script en <head> para que la página
// no destelle en claro antes de hidratar (lo usa el hub; Trámites lo aplica al montar su barra).
export const THEME_STORAGE_KEY = "flit-theme";

export const THEME_INIT_SCRIPT = `try{if(localStorage.getItem("${THEME_STORAGE_KEY}")==="dark")document.documentElement.classList.add("dark")}catch(e){}`;
