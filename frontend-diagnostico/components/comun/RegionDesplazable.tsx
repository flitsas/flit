"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";

// HU #14562 · D2 (axe scrollable-region-focusable, WCAG 2.1.1) + O-4 (auditoría D1–D3): contenedor con scroll de una
// tabla. Solo cuando el contenido desborda (scrollWidth > clientWidth, o scrollHeight > clientHeight si el contenedor
// tiene alto máximo) es `region` con nombre «…, desplazable» y parada de Tab (tabIndex 0); con la tabla entera a la
// vista es un div plano, sin landmark ni parada vacía. Se vuelve a medir con ResizeObserver (cambio de ancho de la
// pantalla o del contenido). Foco visible con --di-action.
// Ronda 2 de E2E (HU #14580): único patrón para los contenedores desplazables de la ola. El `DataTable` del kit pinta su
// propio `overflow-x-auto` sin foco y `packages/ui` queda fuera del alcance U10: `RegionDesplazableTabla` le aplica las
// mismas reglas desde Diagnóstico, sin tocar el kit.
const FOCO_DESPLAZABLE = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]";
const CLASES_FOCO = FOCO_DESPLAZABLE.split(" ");

const desborda = (el: HTMLElement) => el.scrollWidth > el.clientWidth || el.scrollHeight > el.clientHeight;

/** `true` si el elemento tiene más contenido que su caja visible (ancho, o alto si lo tiene acotado); se actualiza con ResizeObserver. */
export function useDesbordeHorizontal<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [hayDesborde, setHayDesborde] = useState(false);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const medir = () => setHayDesborde(desborda(el));
    medir();
    if (typeof ResizeObserver === "undefined") return;
    const observador = new ResizeObserver(medir);
    observador.observe(el);
    // El ancho de la tabla puede cambiar sin que cambie el del contenedor.
    if (el.firstElementChild) observador.observe(el.firstElementChild);
    return () => observador.disconnect();
  }, []);

  return [ref, hayDesborde] as const;
}

/** Uso: <RegionDesplazable nombre="Personas consultadas" className="rounded-xl border"><table …/></RegionDesplazable> */
export function RegionDesplazable({ nombre, className = "", children }: { nombre: string; className?: string; children: ReactNode }) {
  const [ref, hayDesborde] = useDesbordeHorizontal<HTMLDivElement>();
  return (
    <div
      ref={ref}
      // O-4 (reauditoría): rol, nombre y tabIndex van juntos y solo con desborde; sin él es un div plano (sin landmark
      // huérfano, y un aria-label en un div sin rol es ARIA prohibido).
      {...(hayDesborde ? { role: "region", "aria-label": `${nombre}, desplazable`, tabIndex: 0 } : {})}
      className={`overflow-x-auto ${FOCO_DESPLAZABLE} ${className}`.trim()}
    >
      {children}
    </div>
  );
}

function marcar(el: HTMLElement, nombre: string) {
  el.setAttribute("role", "region");
  el.setAttribute("aria-label", `${nombre}, desplazable`);
  el.tabIndex = 0;
  el.classList.add(...CLASES_FOCO);
}

function desmarcar(el: HTMLElement) {
  el.removeAttribute("role");
  el.removeAttribute("aria-label");
  el.removeAttribute("tabindex");
  el.classList.remove(...CLASES_FOCO);
}

/**
 * Uso: <RegionDesplazableTabla nombre="Salud de las fuentes"><DataTable ariaLabel="Salud de las fuentes" … /></RegionDesplazableTabla>
 * Aplica el patrón de `RegionDesplazable` al contenedor `overflow-x-auto` que el `DataTable` del kit pinta alrededor de
 * su `<table>`: rol, nombre, tabIndex y foco solo mientras desborda. El envoltorio es `display: contents` (no altera el
 * layout) y sigue a la tabla aunque se monte después (cargando → listo) o se vuelva a montar.
 */
export function RegionDesplazableTabla({ nombre, children }: { nombre: string; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const raiz = ref.current;
    if (!raiz) return;
    let actual: HTMLElement | null = null;
    const observador = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(() => medir());

    function contenedor(): HTMLElement | null {
      const padre = raiz?.querySelector("table")?.parentElement ?? null;
      return padre?.classList.contains("overflow-x-auto") ? padre : null;
    }
    function medir() {
      if (!actual) return;
      if (desborda(actual)) marcar(actual, nombre);
      else desmarcar(actual);
    }
    function seguir() {
      const el = contenedor();
      if (el !== actual) {
        if (actual) desmarcar(actual);
        observador?.disconnect();
        actual = el;
        if (el) {
          observador?.observe(el);
          if (el.firstElementChild) observador?.observe(el.firstElementChild);
        }
      }
      medir();
    }

    seguir();
    // Solo cambios de hijos: los atributos que pone `marcar` no vuelven a disparar el observador.
    const mutaciones = new MutationObserver(seguir);
    mutaciones.observe(raiz, { childList: true, subtree: true });
    return () => {
      mutaciones.disconnect();
      observador?.disconnect();
    };
  }, [nombre]);

  return (
    <div ref={ref} className="contents">
      {children}
    </div>
  );
}
