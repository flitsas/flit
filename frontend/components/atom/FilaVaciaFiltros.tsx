"use client";

import { TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";

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
    <tr>
      <td
        colSpan={columnas}
        className="rounded-xl border border-[#DFE5ED] bg-white dark:border-white/10 dark:bg-[#0B0F14]"
      >
        <TablaFiltrosVacio onLimpiar={onLimpiar} mensaje={mensaje} />
      </td>
    </tr>
  );
}
