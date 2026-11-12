"use client";

import { useEffect, useId, useRef, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { MANDATO_TIPOS } from "@/lib/plataforma/mandato-templates";

/**
 * Encabezado «Tipo de mandato» con su ayuda: qué significa cada tipo. El tipo dice QUIÉN ocupa el bloque del
 * mandatario en el contrato, y se confundía con el modelo del mandatario (persona natural / jurídica).
 * Se abre con clic o teclado y se cierra con Escape o al hacer clic fuera.
 */
export function TipoMandatoAyuda({ label = "Tipo de mandato" }: { label?: string }) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("mousedown", onClick);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("mousedown", onClick);
    };
  }, [open]);

  return (
    <span ref={ref} className="relative inline-flex items-center gap-1">
      {label}
      <button
        type="button"
        aria-label="Qué significa cada tipo de mandato"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((v) => !v)}
        className="inline-flex h-5 w-5 items-center justify-center rounded-full text-inherit hover:bg-white/10 focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
      >
        <Icono nombre="exclamacion" className="h-3.5 w-3.5" />
      </button>
      {open ? (
        <span
          id={panelId}
          role="note"
          className="absolute left-0 top-full z-30 mt-2 block w-80 max-w-[calc(100vw-2rem)] rounded-xl border border-border bg-card p-3 text-left text-[11px] font-normal normal-case leading-relaxed tracking-normal text-foreground shadow-[var(--kit-sombra)]"
        >
          <span className="mb-1.5 block text-muted-foreground">
            Dice quién ocupa el bloque del mandatario en el contrato.
          </span>
          {MANDATO_TIPOS.map((t) => (
            <span key={t.value} className="mt-1.5 block">
              <span className="font-semibold">{t.label}:</span> {t.summary}
            </span>
          ))}
        </span>
      ) : null}
    </span>
  );
}
