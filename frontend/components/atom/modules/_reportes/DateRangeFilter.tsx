"use client";

import type { DateRange } from "./range";

interface DateRangeFilterProps {
  value: DateRange;
  onChange: (next: DateRange) => void;
  /** Deshabilita los inputs mientras se recargan las métricas. */
  disabled?: boolean;
}

const inputClass =
  "h-10 rounded-[10px] border bg-white px-3 text-xs font-medium text-[#162744] outline-none focus:border-[#557EFF] disabled:opacity-60 dark:bg-[#0B0F14] dark:text-white";

/**
 * Filtro de rango de fechas del dashboard analítico (HU #10247, AC2). Dos inputs
 * nativos `type=date` con label asociado; al cambiar cualquiera notifica al padre
 * para refrescar los gráficos vía API sin recargar la página.
 */
export function DateRangeFilter({ value, onChange, disabled }: DateRangeFilterProps) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1">
        <label htmlFor="reportes-fecha-desde" className="text-[10px] font-semibold uppercase opacity-60">
          Desde
        </label>
        <input
          id="reportes-fecha-desde"
          type="date"
          className={inputClass}
          style={{ borderColor: "#DFE5ED" }}
          value={value.from}
          max={value.to || undefined}
          disabled={disabled}
          onChange={(e) => onChange({ ...value, from: e.target.value })}
        />
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor="reportes-fecha-hasta" className="text-[10px] font-semibold uppercase opacity-60">
          Hasta
        </label>
        <input
          id="reportes-fecha-hasta"
          type="date"
          className={inputClass}
          style={{ borderColor: "#DFE5ED" }}
          value={value.to}
          min={value.from || undefined}
          disabled={disabled}
          onChange={(e) => onChange({ ...value, to: e.target.value })}
        />
      </div>
    </div>
  );
}
