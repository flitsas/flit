"use client";

import { useEffect, useRef, useState } from "react";
import { pestanaClass } from "@flit/ui/Pestanas";
import { contenedorScroll } from "@/components/detalle/useCercania";

export interface SeccionBarra {
  id: string;
  /** Rótulo completo y, en celular, el corto. */
  texto: string;
  corto?: string;
}

/**
 * Barra de secciones del detalle (rediseño S1, R2): fija arriba, lleva a cada sección con un clic y subraya la que se está
 * viendo (scrollspy). Es navegación dentro de la página: todo el contenido sigue a la vista y nada se esconde.
 */
export function BarraSecciones({ secciones, onIr }: { secciones: SeccionBarra[]; onIr: (id: string) => void }) {
  const [activa, setActiva] = useState(secciones[0]?.id ?? "");
  const barra = useRef<HTMLElement>(null);

  // Sección pulsada: manda hasta el siguiente gesto de la gestora. Así el scroll suave no subraya las intermedias y, en
  // pantallas altas donde la última sección no alcanza a subir, la barra no marca otra distinta a la pulsada.
  const pulsada = useRef<string | null>(null);

  useEffect(() => {
    const contenedor = contenedorScroll(barra.current);
    const alFondo = () => {
      const c = contenedor ?? document.scrollingElement;
      // Solo si hay algo que desplazar: una página que cabe entera no tiene «fondo» al que llegar.
      return !!c && c.scrollHeight > c.clientHeight && c.scrollTop + c.clientHeight >= c.scrollHeight - 1;
    };
    const ultima = secciones[secciones.length - 1]?.id;
    const visibles = new Map<string, boolean>();
    const elegir = () => {
      if (pulsada.current) return;
      // Al fondo, la última: puede que nunca cruce la franja de arriba porque no hay más página que desplazar.
      if (alFondo() && ultima) return setActiva(ultima);
      // Si no, la primera sección (en orden de la página) que cruza la franja bajo la barra.
      const primera = secciones.find((s) => visibles.get(s.id));
      if (primera) setActiva(primera.id);
    };
    const gesto = (e: Event) => {
      if (e.target instanceof Node && barra.current?.contains(e.target)) return;
      pulsada.current = null;
    };
    const eventos = ["wheel", "touchstart", "pointerdown", "keydown"] as const;
    eventos.forEach((e) => window.addEventListener(e, gesto, { capture: true, passive: true }));
    const raizScroll: EventTarget = contenedor ?? window;
    raizScroll.addEventListener("scroll", elegir, { passive: true });
    const obs =
      typeof IntersectionObserver === "undefined"
        ? null
        : new IntersectionObserver(
            (entradas) => {
              for (const e of entradas) visibles.set(e.target.id, e.isIntersecting);
              elegir();
            },
            { root: contenedor, rootMargin: "-25% 0px -65% 0px" },
          );
    for (const s of secciones) {
      const el = document.getElementById(s.id);
      if (el) obs?.observe(el);
    }
    return () => {
      obs?.disconnect();
      raizScroll.removeEventListener("scroll", elegir);
      eventos.forEach((e) => window.removeEventListener(e, gesto, { capture: true }));
    };
  }, [secciones]);

  // En celular la barra desliza dentro de sí misma: la activa se mantiene a la vista. Se mueve solo el scroll horizontal
  // de la barra (scrollIntoView podría mover también la página en vertical y provocar un salto).
  useEffect(() => {
    const pista = barra.current?.querySelector<HTMLElement>("[data-pista]");
    const boton = pista?.querySelector<HTMLElement>('[aria-current="location"]');
    if (!pista || !boton) return;
    const izquierda = boton.offsetLeft - pista.offsetLeft;
    if (izquierda < pista.scrollLeft) pista.scrollLeft = izquierda;
    else if (izquierda + boton.offsetWidth > pista.scrollLeft + pista.clientWidth) pista.scrollLeft = izquierda + boton.offsetWidth - pista.clientWidth;
  }, [activa]);

  return (
    <nav
      ref={barra}
      aria-label="Secciones del comparendo"
      className="sticky top-0 z-20 -mx-1 border-b border-[color:var(--di-divider)] bg-[var(--di-surface)] px-1"
      data-testid="detalle-barra"
    >
      <div data-pista className="kit-pestanas flex items-end gap-0 overflow-x-auto whitespace-nowrap">
        {secciones.map((s) => (
          <button
            key={s.id}
            type="button"
            aria-current={activa === s.id ? "location" : undefined}
            className={pestanaClass(activa === s.id)}
            onClick={() => {
              pulsada.current = s.id;
              setActiva(s.id);
              onIr(s.id);
            }}
          >
            {s.corto ? (
              <>
                <span className="sm:hidden">{s.corto}</span>
                <span className="hidden sm:inline">{s.texto}</span>
              </>
            ) : (
              s.texto
            )}
          </button>
        ))}
      </div>
    </nav>
  );
}
