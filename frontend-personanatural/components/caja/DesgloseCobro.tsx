import { Card } from "@/components/tramite/piezas";
import type { TramiteParaCobrar } from "@/lib/caja/tipos";
import { formatearPesos } from "@/lib/formato";

/** Desglose y total del trámite tal como lo calcula el servidor. Solo lectura: la caja no edita el valor. */
export function DesgloseCobro({ tramite, totalCop }: { tramite: TramiteParaCobrar; totalCop: number }) {
  return (
    <Card title={tramite.tipoTramite.nombre} id="desglose-caja">
      <p className="-mt-2 mb-4 text-sm text-muted-foreground">
        <span className="font-semibold">{tramite.codigo}</span> · {tramite.organismo.nombre}
      </p>
      <div className="w-full overflow-x-auto rounded-[9px] border border-border">
        <table className="w-full text-sm">
          <caption className="sr-only">Desglose del cobro</caption>
          <thead className="bg-primary text-primary-foreground">
            <tr>
              <th scope="col" className="p-4 text-left font-bold">
                Concepto
              </th>
              <th scope="col" className="p-4 text-right font-bold">
                Valor
              </th>
            </tr>
          </thead>
          <tbody className="bg-card">
            {tramite.lineas.map((l, i) => (
              <tr key={`${l.concepto}-${i}`}>
                <td className="border-t border-border p-4">{l.concepto}</td>
                <td className="border-t border-border p-4 text-right tabular-nums">{formatearPesos(l.valorCop)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot className="bg-card">
            <tr>
              <th scope="row" className="border-t border-border p-4 text-left text-lg font-bold">
                Total a cobrar
              </th>
              <td className="border-t border-border p-4 text-right text-lg font-bold tabular-nums text-primary" data-testid="total-caja">
                {formatearPesos(totalCop)}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </Card>
  );
}
