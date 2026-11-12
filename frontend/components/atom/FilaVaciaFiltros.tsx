"use client";

import { TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { TABLA_MOVIL_FILA_ANCHA_CLS } from "@/components/atom/table-styles";

/**
 * Fila única de `<tbody>` para el vacío POR FILTROS (Feature #13996): la tabla sigue montada con su
 * `<thead>` (embudos visibles) y esta fila ocupa todas las columnas con el aviso y «Limpiar filtros».
 */
export function FilaVaciaFiltros({
  columnas,
  onLimpiar,
  mensaje,
}: {
  columnas: number;
  onLimpiar: () => void;
  mensaje?: string;
}) {
  return (
    <tr className={TABLA_MOVIL_FILA_ANCHA_CLS}>
      <td
        colSpan={columnas}
        className={`rounded-xl border border-border bg-card dark:border-border dark:bg-card ${TABLA_MOVIL_FILA_ANCHA_CLS}`}
      >
        <TablaFiltrosVacio onLimpiar={onLimpiar} mensaje={mensaje} />
      </td>
    </tr>
  );
}
