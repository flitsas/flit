"use client";

import { DateRangePicker } from "@/components/atom/DateRangePicker";
import { esRangoVacio, sinRango, type DateRange } from "./range";

interface DateRangeFilterProps {
  value: DateRange;
  onChange: (next: DateRange) => void;
  /** Deshabilita el control mientras se recargan las métricas. */
  disabled?: boolean;
  /**
   * BUG #12588 — ofrece «Todo el periodo» para volver a la vista sin acotar. Solo lo activa el
   * consumidor cuyo endpoint admite rango vacío (el overview analítico); los paneles de reportes lo
   * dejan apagado porque los suyos exigen `from`/`to`.
   */
  permiteSinRango?: boolean;
}

/**
 * Filtro de rango de fechas compartido (HU #10247, migrado HU #12724).
 * Envuelve {@link DateRangePicker} de un solo campo con calendario popover.
 */
export function DateRangeFilter({ value, onChange, disabled, permiteSinRango }: DateRangeFilterProps) {
  const vacio = esRangoVacio(value);
  return (
    <div className="flex flex-wrap items-end gap-3">
      <DateRangePicker value={value} onChange={onChange} disabled={disabled} label="Rango de fechas" />
      {permiteSinRango && (
        <div className="flex flex-col gap-1">
          <span className="text-[10px] font-semibold uppercase opacity-60" aria-hidden="true">
            &nbsp;
          </span>
          <button
            type="button"
            className="h-10 rounded-[10px] border border-[#DFE5ED] px-3 text-xs font-medium text-[#162744] transition hover:bg-[#557EFF]/10 disabled:opacity-60 dark:border-white/15 dark:text-white"
            disabled={disabled || vacio}
            aria-pressed={vacio}
            title={
              vacio
                ? "Ya se están mostrando todos los trámites, sin filtro de fechas"
                : "Quitar el filtro de fechas y ver todos los trámites"
            }
            onClick={() => onChange(sinRango())}
          >
            Todo el periodo
          </button>
        </div>
      )}
    </div>
  );
}
