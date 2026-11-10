import { formatearPesos } from "@/lib/formato";
import type { TramiteParaCobrar } from "@/lib/caja/tipos";

/** Desglose y total del trámite tal como lo calcula el servidor. Solo lectura: la caja no edita el valor. */
export function DesgloseCobro({ tramite, totalCop }: { tramite: TramiteParaCobrar; totalCop: number }) {
  return (
    <section aria-labelledby="titulo-desglose-caja" className="rounded-[9px] border border-border bg-card p-6">
      <h2 id="titulo-desglose-caja" className="text-xl font-semibold">
        {tramite.tipoTramite.nombre}
      </h2>
      <p className="mt-1 text-sm text-muted-foreground">
        <span className="font-semibold">{tramite.codigo}</span> · {tramite.organismo.nombre}
      </p>
      <table className="mt-4 w-full text-sm">
        <caption className="sr-only">Desglose del cobro</caption>
        <thead>
          <tr className="text-left text-[10px] font-semibold uppercase tracking-wider" style={{ background: "#DFE5ED", color: "var(--flit-navy)" }}>
            <th scope="col" className="px-4 py-2.5">
              Concepto
            </th>
            <th scope="col" className="px-4 py-2.5 text-right">
              Valor
            </th>
          </tr>
        </thead>
        <tbody>
          {tramite.lineas.map((l, i) => (
            <tr key={`${l.concepto}-${i}`} className="border-b border-border">
              <td className="px-4 py-2.5">{l.concepto}</td>
              <td className="px-4 py-2.5 text-right tabular-nums">{formatearPesos(l.valorCop)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" className="px-4 py-3 text-left text-base font-bold">
              Total a cobrar
            </th>
            <td className="px-4 py-3 text-right text-lg font-bold tabular-nums" data-testid="total-caja">
              {formatearPesos(totalCop)}
            </td>
          </tr>
        </tfoot>
      </table>
    </section>
  );
}
