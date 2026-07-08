"use client";

// Lista de barras horizontales simple (Reportes 2.0, HU-C): distribuciones por
// OT/tipo/módulo sin coste de SVG. Cada fila puede ser clicable (drill-down).
import { formatNumber } from "./format";

export interface BarListItem {
  key: string;
  label: string;
  /** Valor que dimensiona la barra. */
  value: number;
  /** Texto del valor a la derecha (por defecto el número formateado). */
  valueLabel?: string;
  /** Nota secundaria bajo la etiqueta (p. ej. "8 de 38"). */
  hint?: string;
}

export interface BarListProps {
  items: BarListItem[];
  color?: string;
  /** Máximo de la escala; por defecto el mayor valor de la lista. */
  max?: number;
  onSelect?: (item: BarListItem) => void;
  emptyMessage?: string;
  testId?: string;
}

export function BarList({ items, color = "#557EFF", max, onSelect, emptyMessage, testId }: BarListProps) {
  if (items.length === 0) {
    return (
      <p className="text-xs opacity-60 py-2" data-testid={testId ? `${testId}-empty` : undefined}>
        {emptyMessage ?? "Sin datos en el periodo."}
      </p>
    );
  }
  const scale = Math.max(max ?? 0, ...items.map((i) => i.value), 1);

  return (
    <ul className="flex flex-col gap-1.5" data-testid={testId}>
      {items.map((item) => {
        const width = `${Math.max(2, Math.round((item.value / scale) * 100))}%`;
        const row = (
          <>
            <div className="flex items-center justify-between gap-2 text-[11px]">
              <span className="truncate font-medium">{item.label}</span>
              <span className="font-semibold shrink-0">{item.valueLabel ?? formatNumber(item.value)}</span>
            </div>
            <div className="h-2 rounded-full bg-[#DFE5ED] dark:bg-[#1E2A3C] overflow-hidden mt-0.5">
              <div className="h-full rounded-full" style={{ width, background: color }} />
            </div>
            {item.hint && <p className="text-[10px] opacity-60 mt-0.5">{item.hint}</p>}
          </>
        );
        return (
          <li key={item.key}>
            {onSelect ? (
              <button
                type="button"
                onClick={() => onSelect(item)}
                className="w-full text-left rounded-lg px-1.5 py-1 outline-none hover:bg-[#557EFF14] focus:bg-[#557EFF14]"
                aria-label={`Ver detalle de ${item.label}`}
              >
                {row}
              </button>
            ) : (
              <div className="px-1.5 py-1">{row}</div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
