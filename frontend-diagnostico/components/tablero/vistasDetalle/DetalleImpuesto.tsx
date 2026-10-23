import { CheckCircle2, Clock } from "lucide-react";
import { formatearPesos, type ResultadoImpuestoDetalle } from "@/lib/tablero/detalle";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, Etiqueta, FILA_TABLA_CLS, TARJETA_DETALLE_CLS, TEXTO_SECUNDARIO_CLS } from "./comun";

/**
 * Impuesto vehicular departamental (HU #13866, F09-C9/C10; contrato de HU #13864, Q1 = a): «Total adeudado» destacado en
 * pesos enteros con el mismo formato del avalúo y una fila por vigencia (Vigencia / Valor / Estado «Pagada» o «Pendiente»).
 * Sin vigencias, «No registra vigencias». Solo lee `monto_total` y `anio`/`valor`/`pagada` de cada vigencia: aunque llegara
 * un proveedor, una transacción o el departamento consultado, no hay dónde pintarlos (R-20).
 */
export function DetalleImpuesto({ resultado }: { resultado: ResultadoImpuestoDetalle }) {
  return (
    <div className="flex flex-col gap-3">
      <div className={TARJETA_DETALLE_CLS}>
        <div className="flex flex-col gap-0.5">
          <span className={TEXTO_SECUNDARIO_CLS}>Total adeudado</span>
          <span className="text-xl font-bold text-flit-primary dark:text-white">{formatearPesos(resultado.monto_total)}</span>
        </div>
      </div>

      {resultado.vigencias.length === 0 ? (
        <p className={`${TARJETA_DETALLE_CLS} text-sm text-[var(--nav-texto)] dark:text-slate-300`}>No registra vigencias</p>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10">
          <table aria-label="Vigencias del impuesto" className="w-full min-w-[360px] border-collapse">
            <thead>
              <tr>
                {["Vigencia", "Valor", "Estado"].map((c) => (
                  <th key={c} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {resultado.vigencias.map(({ anio, valor, pagada }, i) => (
                <tr key={`${anio}-${i}`} className={FILA_TABLA_CLS}>
                  <td className={CELDA_TABLA_CLS}>{anio}</td>
                  <td className={`${CELDA_TABLA_CLS} whitespace-nowrap`}>{formatearPesos(valor)}</td>
                  <td className={CELDA_TABLA_CLS}>
                    {pagada ? (
                      <Etiqueta tono="success">
                        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                        Pagada
                      </Etiqueta>
                    ) : (
                      <Etiqueta tono="warning">
                        <Clock className="h-3.5 w-3.5" aria-hidden="true" />
                        Pendiente
                      </Etiqueta>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
