"use client";

import type { ReactNode } from "react";
import { ClampedText, Hint, StateChip, type ChipTone } from "./ConfigUi";

// Switch accesible reutilizado en las pestañas de configuración (HU #10194).
// Implementado como checkbox visualmente oculto para conservar semántica y foco.
// Tarjeta de alto uniforme: título y switch en la misma línea, estado en TEXTO junto al switch (además del
// color), descripción a dos líneas con «Ver más» y, si aplica, una pista corta con la consecuencia.

/** Texto de estado de cada posición del switch (no cambia el valor guardado, solo cómo se lee). */
export interface ToggleSwitchState {
  on: string;
  off: string;
  onTone?: ChipTone;
  offTone?: ChipTone;
}

/** Estado por defecto: Activado / Desactivado. */
export const STATE_ACTIVADO: ToggleSwitchState = { on: "Activado", off: "Desactivado" };

export interface ToggleSwitchProps {
  id: string;
  /** Chip opcional junto al título (p. ej. estado). Queda fuera de la etiqueta: no altera el nombre accesible. */
  badge?: ReactNode;
  label: string;
  description?: string;
  /** Consecuencia o valor por defecto, como pista corta con icono. */
  hint?: string;
  /** Si se pasa, muestra junto al switch el estado en texto con icono. */
  state?: ToggleSwitchState;
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
}

export function ToggleSwitch({
  id,
  badge,
  label,
  description,
  hint,
  state,
  checked,
  disabled = false,
  onChange,
}: ToggleSwitchProps) {
  return (
    <div className="flex h-full min-h-[88px] flex-col gap-1.5 rounded-[14px] border border-[#DFE5ED] bg-white px-4 py-3 dark:border-white/10 dark:bg-[#0B0F14]">
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
          <label htmlFor={id} className="cursor-pointer py-1 text-sm font-semibold text-[#162744] dark:text-white">
            {label}
          </label>
          {badge}
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {state && (
            <StateChip
              text={checked ? state.on : state.off}
              tone={checked ? (state.onTone ?? "success") : (state.offTone ?? "neutral")}
            />
          )}
          <label
            htmlFor={id}
            className={`relative inline-flex shrink-0 items-center py-2 ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}
          >
            <input
              id={id}
              type="checkbox"
              role="switch"
              aria-checked={checked}
              checked={checked}
              disabled={disabled}
              onChange={(e) => onChange(e.target.checked)}
              className="peer sr-only"
            />
            <span
              aria-hidden="true"
              className="h-6 w-11 rounded-full transition peer-focus-visible:ring-2 peer-focus-visible:ring-[#557EFF] peer-focus-visible:ring-offset-2"
              style={{ background: checked ? "#00DBD5" : "#DFE5ED" }}
            />
            <span
              aria-hidden="true"
              className="absolute top-1/2 h-5 w-5 -translate-y-1/2 rounded-full bg-white shadow transition-all"
              style={{ left: checked ? "calc(100% - 22px)" : "2px" }}
            />
          </label>
        </div>
      </div>
      {description && <ClampedText text={description} />}
      {hint && <Hint>{hint}</Hint>}
    </div>
  );
}
