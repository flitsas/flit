"use client";

// Piezas visuales compartidas por las tres pestañas de Reportes del organismo.
//
// Estaban embebidas en `OtReportsConsole`; al partir la consola en pestañas dejaron de pertenecer a
// ninguna en particular. Se extraen sin cambiar su comportamiento: el panel operativo debe seguir
// viéndose exactamente igual que antes del corte.

import type { ReactNode } from "react";

/**
 * Si se ofrece la descarga en CSV, además del Excel.
 *
 * Está OCULTA, no eliminada: el generador de CSV, sus pruebas y el camino de descarga siguen
 * enteros, y volver a ofrecerla es poner esto en `true`. Se apagó porque el Excel es donde estos
 * informes acaban de verdad —fechas como fechas y números sumables—, y dos botones de descarga
 * obligan a elegir entre formatos a quien solo quería el archivo.
 *
 * El tipo es `boolean` y no el literal `false` a propósito: así apagarla no vuelve muerto, a ojos
 * del compilador, todo el código que cuelga de ella.
 */
export const CSV_EXPORT_VISIBLE: boolean = false;

export function Section({
  title,
  testId,
  hint,
  actions,
  children,
}: {
  title: string;
  testId: string;
  /** Una línea que explica qué mide la sección. Sin ella, el título tiene que cargar solo con todo. */
  hint?: string;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section
      className="flex flex-col gap-3 rounded-xl border border-[#DFE5ED] p-4 dark:border-white/10"
      data-testid={testId}
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold">{title}</h3>
          {hint && <p className="mt-0.5 text-[11px] text-[#6B7280] dark:text-white/50">{hint}</p>}
        </div>
        {actions}
      </div>
      {children}
    </section>
  );
}

export function SubTitle({ children }: { children: ReactNode }) {
  return (
    <p className="mt-1 text-[11px] font-semibold uppercase tracking-wide text-[#6B7280] dark:text-white/50">
      {children}
    </p>
  );
}

export function Tile({
  value,
  label,
  hint,
  accent,
  onClick,
}: {
  value: number | string;
  label: string;
  hint?: string;
  /** Color del valor. Se usa para atar la tarjeta a su serie en la gráfica de al lado. */
  accent?: string;
  onClick?: () => void;
}) {
  const content = (
    <>
      <p className="text-xl font-semibold tabular-nums" style={accent ? { color: accent } : undefined}>
        {value}
      </p>
      <p className="text-[11px] text-[#6B7280] dark:text-white/50">{label}</p>
      {hint && <p className="mt-0.5 text-[10px] text-[#9AA5B4] dark:text-white/35">{hint}</p>}
    </>
  );

  if (!onClick) {
    return (
      <div className="rounded-xl border border-[#DFE5ED] px-3 py-2 dark:border-white/10">{content}</div>
    );
  }

  return (
    <button
      type="button"
      onClick={onClick}
      className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-left transition hover:border-[#557EFF] hover:shadow-sm dark:border-white/10 dark:hover:border-[#557EFF]"
    >
      {content}
    </button>
  );
}

export function Bucket({
  value,
  label,
  hot,
  onClick,
}: {
  value: number;
  label: string;
  hot?: boolean;
  onClick?: () => void;
}) {
  // El ámbar es una alarma, no una etiqueta del tramo: en cero no hay nada que atender y un
  // bloque resaltado sin contenido enseña a ignorar el resaltado cuando sí importa.
  const alerta = Boolean(hot) && value > 0;
  const baseCls = alerta
    ? "border-amber-300 bg-amber-50 dark:border-amber-500/40 dark:bg-amber-500/10"
    : "border-[#DFE5ED] dark:border-white/10";

  const content = (
    <>
      <p
        className={`text-lg font-semibold tabular-nums ${alerta ? "text-amber-800 dark:text-amber-300" : ""}`}
      >
        {value}
      </p>
      <p
        className={`text-[11px] ${alerta ? "text-amber-700 dark:text-amber-400" : "text-[#6B7280] dark:text-white/50"}`}
      >
        {label}
      </p>
    </>
  );

  if (!onClick) {
    return <div className={`rounded-xl border px-3 py-2 text-center ${baseCls}`}>{content}</div>;
  }

  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-xl border px-3 py-2 text-center transition hover:shadow-sm ${baseCls}`}
    >
      {content}
    </button>
  );
}

export function Bar({
  label,
  value,
  total,
  suffix,
  color,
  onClick,
}: {
  label: string;
  value: number;
  total: number;
  suffix?: string;
  color?: string;
  onClick?: () => void;
}) {
  const pct = total === 0 ? 0 : Math.min(100, Math.round((value / total) * 100));
  const fill = color ?? "linear-gradient(135deg,#557EFF,#00DBD5)";

  const content = (
    <>
      <span className="truncate" title={label}>
        {label}
      </span>
      <span className="h-2 overflow-hidden rounded bg-[#EEF1F5] dark:bg-white/10">
        <span className="block h-full rounded" style={{ width: `${pct}%`, background: fill }} />
      </span>
      <span className="tabular-nums font-semibold">{suffix ?? value}</span>
    </>
  );

  if (!onClick) {
    return (
      <div className="grid grid-cols-[minmax(8rem,14rem)_1fr_auto] items-center gap-3 text-xs">
        {content}
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={onClick}
      className="grid grid-cols-[minmax(8rem,14rem)_1fr_auto] items-center gap-3 rounded-lg text-left text-xs transition hover:bg-[#F5F7FA] dark:hover:bg-white/5"
    >
      {content}
    </button>
  );
}

export function Table({
  headers,
  rows,
}: {
  headers: string[];
  rows: { key: string; cells: string[] }[];
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[34rem] text-xs">
        <thead>
          <tr className="border-b border-[#DFE5ED] text-left text-[11px] uppercase tracking-wide text-[#6B7280] dark:border-white/10 dark:text-white/50">
            {headers.map((h) => (
              <th key={h} className="py-2 pr-3 font-semibold">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} className="border-b border-[#EEF1F5] dark:border-white/5">
              {row.cells.map((cell, i) => (
                <td key={headers[i]} className={`py-2 pr-3 ${i === 0 ? "" : "tabular-nums"}`}>
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="text-xs text-[#6B7280] dark:text-white/50">{children}</p>;
}

export function ErrorNotice({ message }: { message: string }) {
  return (
    <p
      role="alert"
      className="rounded-xl bg-red-50 px-4 py-3 text-xs text-red-700 dark:bg-red-500/10 dark:text-red-300"
    >
      {message}
    </p>
  );
}

/** Botón primario del módulo: el degradado azul→cian es la acción principal en toda la consola OT. */
export function PrimaryButton({
  children,
  onClick,
  disabled,
  type = "button",
}: {
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  type?: "button" | "submit";
}) {
  return (
    <button
      type={type === "submit" ? "submit" : "button"}
      onClick={onClick}
      disabled={disabled}
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white transition disabled:opacity-60"
      style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
    >
      {children}
    </button>
  );
}

export const FIELD_CLS =
  "rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF] disabled:opacity-60";
