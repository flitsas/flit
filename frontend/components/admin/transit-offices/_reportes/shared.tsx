"use client";

// Piezas visuales propias del panel de Reportes del organismo: las tarjetas, las barras y las
// tablitas de los tableros.
//
// Lo que comparten las dos consolas de consultas —el panel, el botón primario, el aviso de error y
// el estilo de campo— vive en `@/components/consultas/ui` y se reexporta aquí para no reescribir
// medio módulo. Una pieza con dos definiciones acaba con dos aspectos, y son el mismo producto.

import type { ReactNode } from "react";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";

export {
  CSV_EXPORT_VISIBLE,
  Empty,
  ErrorNotice,
  FIELD_CLS,
  PrimaryButton,
  Section,
} from "@/components/consultas/ui";

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
    <div className={CARDLIST_SCROLL}>
      <table className={`min-w-[34rem] ${CARDLIST_TABLE}`}>
        <thead>
          <tr className={CARDLIST_HEAD_ROW}>
            {headers.map((h) => (
              <th key={h} className={CARDLIST_TH}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} className={CARDLIST_ROW}>
              {row.cells.map((cell, i) => (
                <td
                  key={headers[i]}
                  className={`${CARDLIST_CELL} ${i === 0 ? "" : "tabular-nums"}`}
                >
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
