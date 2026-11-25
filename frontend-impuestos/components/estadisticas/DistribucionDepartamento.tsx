import type { DashboardImpuestos } from "@/lib/estadisticas/tipos";
import { SUBTITULO_CLS } from "./Figura";
import { ListaBarras } from "./ListaBarras";

/**
 * «Por departamento» (spec-visual §3.1) con la forma de ListaBarras: nombre y cantidad. El contrato no trae el monto por
 * departamento (`porDepartamento[]` = codigo, nombre, total): no se pinta (hueco reportado en el informe de M17).
 */
export function DistribucionDepartamento({ items }: { items: NonNullable<DashboardImpuestos["porDepartamento"]> }) {
  return (
    <>
      <ListaBarras
        nivel={2}
        titulo="Por departamento"
        subtitulo="Liquidaciones según el departamento de la secretaría"
        items={items.map((d) => ({ clave: d.codigo, nombre: d.nombre, valor: d.total }))}
      />
      {items.length === 0 ? <p className={`m-0 ${SUBTITULO_CLS}`}>No hay liquidaciones con departamento en este periodo.</p> : null}
    </>
  );
}
