"use client";

import { useId, useState } from "react";

import { RANGE_PRESETS, type QueryRangePreset } from "@/lib/api/queries";
import {
  esRangoVacio,
  isValidOptionalRange,
  sinRango,
  toIsoDate,
  type DateRange,
} from "@/components/atom/modules/_reportes/range";

/** Valor del selector cuando el rango está vacío (solo si el consumidor admite rango vacío). */
const TODO = "todo";
type Modo = QueryRangePreset | typeof TODO;

const FIELD_CLS =
  "rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-flit-brand disabled:opacity-60";

/**
 * Resuelve un periodo relativo contra el día LOCAL (Bogotá) de `hoy`. Se arma con `toIsoDate`, que
 * lee los componentes locales: con `toISOString()` (UTC), pasadas las 7 p. m. el «hasta» ya sería
 * mañana. «Últimos N días» incluye hoy (N días en total), igual que Consultas.
 */
export function resolverPreset(preset: QueryRangePreset, hoy: Date): DateRange | null {
  const y = hoy.getFullYear();
  const m = hoy.getMonth();
  const d = hoy.getDate();
  const fin = toIsoDate(hoy);
  switch (preset) {
    case "hoy":
      return { from: fin, to: fin };
    case "ultimos_7":
      return { from: toIsoDate(new Date(y, m, d - 6)), to: fin };
    case "ultimos_30":
      return { from: toIsoDate(new Date(y, m, d - 29)), to: fin };
    case "ultimos_90":
      return { from: toIsoDate(new Date(y, m, d - 89)), to: fin };
    case "mes_actual":
      return { from: toIsoDate(new Date(y, m, 1)), to: fin };
    case "mes_anterior":
      return { from: toIsoDate(new Date(y, m - 1, 1)), to: toIsoDate(new Date(y, m, 0)) };
    case "anio_actual":
      return { from: toIsoDate(new Date(y, 0, 1)), to: fin };
    default:
      return null;
  }
}

/** Qué opción del selector describe el rango actual: el preset que coincide, o «Rango propio». */
function modoDe(value: DateRange, hoy: Date, permiteSinRango: boolean): Modo {
  if (esRangoVacio(value)) return permiteSinRango ? TODO : "personalizado";
  for (const { value: preset } of RANGE_PRESETS) {
    const r = resolverPreset(preset, hoy);
    if (r && r.from === value.from && r.to === value.to) return preset;
  }
  return "personalizado";
}

export interface RangoFechasProps {
  value: DateRange;
  onChange: (next: DateRange) => void;
  disabled?: boolean;
  /** Ofrece «Todo el periodo» (rango vacío). Solo para endpoints que admiten from/to opcionales. */
  permiteSinRango?: boolean;
  /** Día de referencia; se inyecta en tests para no depender del reloj. */
  hoy?: Date;
  idPrefix?: string;
}

/**
 * Filtro de rango de fechas único de Reportes: selector de periodo + Desde/Hasta nativos en
 * «Rango propio» (mismo patrón que Consultas). Sustituye al calendario de rango, donde con el rango
 * lleno el primer clic fijaba el FINAL y cerraba el popover, y no se podía elegir el inicio.
 *
 * Nada se cierra solo y cada extremo se edita por separado. No decide la validez del rango: el
 * consumidor valida con `isValidRange` (exige ambos) o `isValidOptionalRange` (Dashboard); aquí solo
 * se avisa si el inicio es posterior al fin.
 */
export function RangoFechas({
  value,
  onChange,
  disabled,
  permiteSinRango = false,
  hoy,
  idPrefix,
}: RangoFechasProps) {
  const autoId = useId();
  const id = idPrefix ?? autoId;
  // «Rango propio» elegido a mano: aunque el rango coincida con un preset, se queda editable.
  const [propioForzado, setPropioForzado] = useState(false);
  const referencia = hoy ?? new Date();
  const derivado = modoDe(value, referencia, permiteSinRango);
  const modo: Modo = propioForzado ? "personalizado" : derivado;
  const incoherente = !isValidOptionalRange(value);

  function elegirModo(next: Modo) {
    if (next === "personalizado") {
      setPropioForzado(true);
      return;
    }
    setPropioForzado(false);
    if (next === TODO) {
      onChange(sinRango());
      return;
    }
    const r = resolverPreset(next, referencia);
    if (r) onChange(r);
  }

  return (
    <div className="flex flex-wrap items-end gap-3" data-testid="rango-fechas">
      <label className="flex flex-col gap-1 text-xs font-semibold" htmlFor={`${id}-periodo`}>
        Periodo
        <select
          id={`${id}-periodo`}
          value={modo}
          disabled={disabled}
          onChange={(e) => elegirModo(e.target.value as Modo)}
          className={FIELD_CLS}
        >
          {permiteSinRango && <option value={TODO}>Todo el periodo</option>}
          {RANGE_PRESETS.map((preset) => (
            <option key={preset.value} value={preset.value}>
              {preset.label}
            </option>
          ))}
        </select>
      </label>

      {modo === "personalizado" && (
        <>
          <label className="flex flex-col gap-1 text-xs font-semibold" htmlFor={`${id}-desde`}>
            Desde
            <input
              id={`${id}-desde`}
              type="date"
              value={value.from}
              max={value.to || undefined}
              disabled={disabled}
              aria-invalid={incoherente ? true : undefined}
              aria-describedby={incoherente ? `${id}-error` : undefined}
              onChange={(e) => onChange({ from: e.target.value, to: value.to })}
              className={FIELD_CLS}
            />
          </label>
          <label className="flex flex-col gap-1 text-xs font-semibold" htmlFor={`${id}-hasta`}>
            Hasta
            <input
              id={`${id}-hasta`}
              type="date"
              value={value.to}
              min={value.from || undefined}
              disabled={disabled}
              aria-invalid={incoherente ? true : undefined}
              aria-describedby={incoherente ? `${id}-error` : undefined}
              onChange={(e) => onChange({ from: value.from, to: e.target.value })}
              className={FIELD_CLS}
            />
          </label>
        </>
      )}

      {incoherente && (
        <p
          id={`${id}-error`}
          role="alert"
          className="basis-full text-[11px] font-medium text-[var(--badge-warning-fg)]"
        >
          La fecha de inicio no puede ser posterior a la fecha final.
        </p>
      )}
    </div>
  );
}
