"use client";

import { esRangoVacio, sinRango, type DateRange } from "./range";

interface DateRangeFilterProps {
  value: DateRange;
  onChange: (next: DateRange) => void;
  /** Deshabilita los inputs mientras se recargan las métricas. */
  disabled?: boolean;
  /**
   * BUG #12588 — ofrece «Todo el periodo» para volver a la vista sin acotar. Solo lo activa el
   * consumidor cuyo endpoint admite rango vacío (el overview analítico); los paneles de reportes lo
   * dejan apagado porque los suyos exigen `from`/`to`.
   */
  permiteSinRango?: boolean;
}

const inputClass =
  "h-10 rounded-[10px] border bg-white px-3 text-xs font-medium text-[#162744] outline-none focus:border-[#557EFF] disabled:opacity-60 dark:bg-[#0B0F14] dark:text-white";

/**
 * Filtro de rango de fechas del dashboard analítico (HU #10247, AC2). Dos inputs
 * nativos `type=date` con label asociado; al cambiar cualquiera notifica al padre
 * para refrescar los gráficos vía API sin recargar la página.
 *
 * BUG #12588 — con `permiteSinRango` el vacío es un estado legítimo («todo el periodo») y no un
 * formulario a medio llenar: el AC2 de la HU #10247 fijaba el mes en curso por defecto, y eso hacía
 * que el total del dashboard hablara de un periodo mientras el usuario lo leía como el universo.
 */
export function DateRangeFilter({ value, onChange, disabled, permiteSinRango }: DateRangeFilterProps) {
  const vacio = esRangoVacio(value);
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
          value={value.to}
          min={value.from || undefined}
          disabled={disabled}
          onChange={(e) => onChange({ ...value, to: e.target.value })}
        />
      </div>
      {permiteSinRango && (
        <div className="flex flex-col gap-1">
          {/* Sin esto, volver a «todo» obligaría a vaciar los dos inputs a mano: el control nativo
              `type=date` no siempre ofrece un borrado visible. */}
          <button
            type="button"
            className="h-10 rounded-[10px] border px-3 text-xs font-medium text-[#162744] transition hover:bg-[#557EFF]/10 disabled:opacity-60 dark:text-white"
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
