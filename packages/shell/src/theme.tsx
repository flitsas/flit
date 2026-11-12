"use client";

import { useCallback, useEffect, useSyncExternalStore } from "react";
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

/** Interruptor claro/oscuro del Manual UI Kit 2026 (`.tema-kit`): el dibujo del paquete de íconos (sol en claro, luna en
 * oscuro) como fondo de 60 × 32 px; `aria-pressed` dice si el oscuro está puesto. Cada app sirve /assets/kit/. */
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
          className="suite-tema"
        />
      </span>
    );
  }

  return (
    <button type="button" onClick={toggle} aria-label="Cambiar tema" aria-pressed={dark} className="suite-tema" />
  );
}
