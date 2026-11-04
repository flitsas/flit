"use client";

import { useCallback, useEffect, useSyncExternalStore } from "react";
import { Moon, Sun } from "lucide-react";
import {
  DARK_MODE_ENABLED,
  THEME_COMING_SOON_MESSAGE,
  THEME_STORAGE_KEY,
} from "./theme-script";

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
  useEffect(() => {
    if (!DARK_MODE_ENABLED) {
      // HU #14159: forzar claro e ignorar / limpiar preferencia oscura previa.
      setTheme(false);
      return;
    }
    // La app que no pone THEME_INIT_SCRIPT (Trámites) aplica la preferencia al montar la barra.
    if (storedPreference() !== isDark()) document.documentElement.classList.toggle("dark", storedPreference());
  }, []);
  const toggle = useCallback(() => {
    if (!DARK_MODE_ENABLED) return;
    setTheme(!isDark());
  }, []);
  return { dark: DARK_MODE_ENABLED ? dark : false, toggle };
}

/** El interruptor de siempre de Trámites (sol / luna). */
export function ThemeToggle() {
  const { dark, toggle } = useSuiteTheme();

  if (!DARK_MODE_ENABLED) {
    // `title` en un wrapper: en Chrome/Firefox el tooltip no aparece en <button disabled>.
    return (
      <span title={THEME_COMING_SOON_MESSAGE} className="inline-flex">
        <button
          type="button"
          disabled
          aria-disabled="true"
          aria-label={THEME_COMING_SOON_MESSAGE}
          aria-pressed={false}
          className="flex cursor-not-allowed items-center gap-1 rounded-full px-1 py-1 opacity-60 transition"
          style={{ background: "var(--color-flit-tech)", color: "var(--color-flit-primary)" }}
        >
          <span className="grid h-7 w-7 place-items-center rounded-full bg-white">
            <Sun className="h-3.5 w-3.5" aria-hidden="true" />
          </span>
          <span className="grid h-7 w-7 place-items-center rounded-full">
            <Moon className="h-3.5 w-3.5" aria-hidden="true" />
          </span>
        </button>
      </span>
    );
  }

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
