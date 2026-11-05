"use client";

import { useId, useRef, type KeyboardEvent } from "react";
import { FileSpreadsheet, Zap } from "lucide-react";
import { AYUDA_CLS, ETIQUETA_CLS, FOCO_CLS } from "@/components/configuracion/estilos";
import type { ModoCreacion } from "./borradores";

const MODOS: ReadonlyArray<{ valor: ModoCreacion; etiqueta: string; Icono: typeof Zap }> = [
  { valor: "express", etiqueta: "Express", Icono: Zap },
  { valor: "masivo", etiqueta: "Masivo", Icono: FileSpreadsheet },
];

export interface SelectorModoProps {
  modo: ModoCreacion;
  onModo: (modo: ModoCreacion) => void;
  /** Mientras se envía algo o con la cabecera del lote creada: el selector no cambia de modo. */
  bloqueado?: boolean;
  /** Por qué está bloqueado, visible y asociado al grupo. */
  explicacion?: string | null;
}

/**
 * Selector Express | Masivo de la pantalla única (HU #14085), con el patrón WAI-ARIA «Radio Group»: un solo radio en el
 * orden de tabulación (tabindex itinerante) y las flechas mueven el foco y eligen, con vuelta; Inicio y Fin. Bloqueado,
 * los radios siguen enfocables (aria-disabled) para que la explicación se anuncie. Misma pastilla de las pestañas de la
 * configuración: el elegido en la tinta AA del azul de marca con texto blanco.
 */
export function SelectorModo({ modo, onModo, bloqueado = false, explicacion }: SelectorModoProps) {
  const id = useId();
  const refs = useRef<Array<HTMLButtonElement | null>>([]);
  const actual = Math.max(0, MODOS.findIndex((m) => m.valor === modo));
  const descripcionId = `${id}-descripcion`;

  function elegir(indice: number) {
    refs.current[indice]?.focus();
    if (!bloqueado) onModo(MODOS[indice].valor);
  }

  function alPulsar(e: KeyboardEvent<HTMLButtonElement>, indice: number) {
    const total = MODOS.length;
    const destino =
      e.key === "ArrowRight" || e.key === "ArrowDown"
        ? (indice + 1) % total
        : e.key === "ArrowLeft" || e.key === "ArrowUp"
          ? (indice - 1 + total) % total
          : e.key === "Home"
            ? 0
            : e.key === "End"
              ? total - 1
              : -1;
    if (destino < 0) return;
    e.preventDefault();
    elegir(destino);
  }

  return (
    <div className="flex flex-col gap-1.5 lg:items-end">
      <span id={`${id}-etiqueta`} className={ETIQUETA_CLS}>
        Tipo de diagnóstico
      </span>
      <div
        role="radiogroup"
        aria-labelledby={`${id}-etiqueta`}
        aria-describedby={descripcionId}
        aria-disabled={bloqueado || undefined}
        className="flex w-fit gap-1 rounded-full border border-[var(--nav-borde)] bg-[var(--nav-panel-bg)] p-1 dark:border-white/10 dark:bg-white/[0.04]"
      >
        {MODOS.map((m, i) => {
          const elegido = i === actual;
          return (
            <button
              key={m.valor}
              ref={(el) => {
                refs.current[i] = el;
              }}
              type="button"
              role="radio"
              aria-checked={elegido}
              aria-disabled={bloqueado || undefined}
              tabIndex={elegido ? 0 : -1}
              onClick={() => elegir(i)}
              onKeyDown={(e) => alPulsar(e, i)}
              className={`inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-sm font-semibold transition aria-disabled:cursor-not-allowed ${FOCO_CLS} ${
                elegido
                  ? "bg-[var(--flit-brand-ink)] text-white shadow-[var(--nav-sombra-activo)]"
                  : "text-[var(--nav-texto)] aria-disabled:opacity-60 dark:text-slate-300"
              }`}
            >
              <m.Icono className="h-4 w-4" aria-hidden="true" />
              {m.etiqueta}
            </button>
          );
        })}
      </div>
      <p id={descripcionId} className={`${AYUDA_CLS} max-w-sm lg:text-right`}>
        {explicacion ?? "Express: un vehículo por placa o VIN. Masivo: varios vehículos desde la plantilla Excel."}
      </p>
    </div>
  );
}
