"use client";

import { useCallback, useEffect, useSyncExternalStore } from "react";
import { Moon, Sun } from "lucide-react";
import { THEME_STORAGE_KEY } from "./theme-script";

// Tema claro/oscuro de la suite, el mismo en todos los productos: la fuente de verdad es la clase `dark` de <html>
// (la leen Tailwind y los tokens de @flit/ui) y la preferencia se recuerda en localStorage.

function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange);
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
  return () => observer.disconnect();
}

const isDark = () => document.documentElement.classList.contains("dark");

function storedPreference(): boolean {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY) === "dark";
  } catch {
    return false;
  }
}

function setTheme(dark: boolean): void {
  document.documentElement.classList.toggle("dark", dark);
  try {
    localStorage.setItem(THEME_STORAGE_KEY, dark ? "dark" : "light");
  } catch {
    // Sin almacenamiento (modo privado estricto): el tema dura lo que dure la página.
  }
}

export function useSuiteTheme(): { dark: boolean; toggle: () => void } {
  const dark = useSyncExternalStore(subscribe, isDark, () => false);
  // La app que no pone THEME_INIT_SCRIPT (Trámites) aplica la preferencia al montar la barra, como siempre.
  useEffect(() => {
    if (storedPreference() !== isDark()) document.documentElement.classList.toggle("dark", storedPreference());
  }, []);
  const toggle = useCallback(() => setTheme(!isDark()), []);
  return { dark, toggle };
}

/** El interruptor de siempre de Trámites (sol / luna). */
export function ThemeToggle() {
  const { dark, toggle } = useSuiteTheme();
  return (
    <button
      type="button"
      onClick={toggle}
      aria-label="Cambiar tema"
      aria-pressed={dark}
      className="flex items-center gap-1 rounded-full px-1 py-1 transition"
      style={{ background: "var(--color-flit-tech)", color: "var(--color-flit-primary)" }}
    >
      <span className={`grid h-7 w-7 place-items-center rounded-full ${dark ? "" : "bg-white"}`}>
        <Sun className="h-3.5 w-3.5" aria-hidden="true" />
      </span>
      <span className={`grid h-7 w-7 place-items-center rounded-full ${dark ? "bg-white" : ""}`}>
        <Moon className="h-3.5 w-3.5" aria-hidden="true" />
      </span>
    </button>
  );
}
