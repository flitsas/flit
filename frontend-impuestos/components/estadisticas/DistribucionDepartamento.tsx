import { formatoMonto } from "@/lib/estadisticas/formato";
import type { DashboardImpuestos } from "@/lib/estadisticas/tipos";
import { SUBTITULO_CLS } from "./Figura";
import { ListaBarras } from "./ListaBarras";

/** `Dashboard.porDepartamento[].x-flit-etiquetas` (impuestos.v1.yaml 1.1.0, delta 10). */
const ETIQUETA_TOTAL = "Liquidaciones";
const ETIQUETA_MONTO = "Monto liquidado";

type Departamentos = NonNullable<DashboardImpuestos["porDepartamento"]>;

/**
 * «Por departamento» (spec-visual §3.1) con la forma de ListaBarras: nombre, cantidad y monto. El monto null dice «Sin
 * dato» (nunca $ 0); si el back no lo trae (anterior a la 1.1.0) no se pinta la línea ni la columna.
 */
export function DistribucionDepartamento({ items }: { items: Departamentos }) {
  const conMonto = items.some((d) => d.monto !== undefined);
  return (
    <>
      <ListaBarras
        nivel={2}
        titulo="Por departamento"
        subtitulo="Liquidaciones según el departamento de la secretaría"
        encabezadoCategoria="Departamento"
        encabezadoValor={ETIQUETA_TOTAL}
        encabezadosExtra={conMonto ? [ETIQUETA_MONTO] : []}
        items={items.map((d) => {
          const monto = conMonto ? formatoMonto(d.monto) : null;
          return { clave: d.codigo, nombre: d.nombre, valor: d.total, detalle: monto ? `${ETIQUETA_MONTO}: ${monto}` : undefined, extra: monto ? [monto] : undefined };
        })}
      />
      {items.length === 0 ? <p className={`m-0 ${SUBTITULO_CLS}`}>No hay liquidaciones con departamento en este periodo.</p> : null}
    </>
  );
}
