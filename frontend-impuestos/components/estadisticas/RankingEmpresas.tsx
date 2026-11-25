import { formatoEntero } from "@/lib/estadisticas/formato";
import type { DashboardImpuestos } from "@/lib/estadisticas/tipos";
import { ListaBarras } from "./ListaBarras";

/**
 * «Compañías con más liquidaciones» (solo Super Admin en la vista global, D-DASH; spec-visual §3.1): ListaBarras con
 * `onElegir`; cada fila es un botón «Ver {Compañía}» que fija ?empresa= con `elegir(id)` de useEmpresaSeleccionada.
 */
export function RankingEmpresas({ ranking, onElegir }: { ranking: NonNullable<DashboardImpuestos["ranking"]>; onElegir: (id: string) => void }) {
  return (
    <ListaBarras
      nivel={2}
      titulo="Compañías con más liquidaciones"
      subtitulo="Por liquidaciones creadas en el periodo"
      items={ranking.map((r) => ({ clave: r.empresa.id, nombre: r.empresa.nombre, valor: r.total, detalle: `${formatoEntero(r.total)} liquidaciones` }))}
      onElegir={onElegir}
      etiquetaAccion={(i) => `Ver ${i.nombre}`}
    />
  );
}
