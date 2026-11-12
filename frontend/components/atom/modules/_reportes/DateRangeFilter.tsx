"use client";

import { RangoFechas } from "@/components/atom/RangoFechas";
import type { DateRange } from "./range";

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
 * Filtro de rango de fechas compartido de Reportes (HU #10247). Delega en {@link RangoFechas}:
 * selector de periodo y, en «Rango propio», Desde/Hasta nativos que se editan por separado.
 */
export function DateRangeFilter({ value, onChange, disabled, permiteSinRango }: DateRangeFilterProps) {
  return (
    <RangoFechas
      value={value}
      onChange={onChange}
      disabled={disabled}
      permiteSinRango={permiteSinRango}
    />
  );
}
