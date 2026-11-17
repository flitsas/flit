"use client";

import { INTERRUPTOR_CLS, INTERRUPTOR_PERILLA_CLS } from "@flit/ui/Interruptor";

/** Interruptor accesible (role=switch) con su etiqueta y una ayuda opcional. */
export function Interruptor({
  activo,
  onCambio,
  etiqueta,
  ayuda,
  deshabilitado,
}: {
  activo: boolean;
  onCambio: (v: boolean) => void;
  etiqueta: string;
  ayuda?: string;
  deshabilitado?: boolean;
}) {
  return (
    <div className={`flex items-start gap-3 ${deshabilitado ? "opacity-60" : ""}`}>
      <button
        type="button"
        role="switch"
        aria-checked={activo}
        aria-label={etiqueta}
        disabled={deshabilitado}
        onClick={() => onCambio(!activo)}
        className={`${INTERRUPTOR_CLS} mt-0.5`}
      >
        <span className={INTERRUPTOR_PERILLA_CLS} />
      </button>
      <span className="flex flex-col">
        <span className="text-xs font-semibold text-flit-primary">{etiqueta}</span>
        {ayuda && <span className="text-[11px] text-muted-foreground">{ayuda}</span>}
      </span>
    </div>
  );
}
