import { useId } from "react";
import { StatusBadge } from "@flit/ui/StatusBadge";
import { TEXTO_SECUNDARIO_CLS } from "@/components/comun/estilos";
import { MONTO_SIN_DATO, formatoMonto } from "@/lib/estadisticas/formato";
import { SECCION_CLS, TITULO_SECCION_CLS } from "./Figura";

/** x-flit-literales.pantalla del contrato (impuestos.v1.yaml). */
const LITERAL_SIN_MONTO = "{n} liquidaciones sin monto";
const LITERAL_PARCIAL = "Parcial";
/** spec-visual §3.1: nota cuando el proveedor devolvió resultados parciales. */
const NOTA_PARCIAL = "Incluye resultados parciales del proveedor";

/**
 * «Monto total liquidado» del dashboard (spec-visual §3.1; F4a-CF3). Es la suma de totalAPagar sin los null: con N > 0
 * liquidaciones sin monto se dice cuántas (literal del contrato) y lleva la marca «Parcial», igual que con `parcial`. Un
 * monto ausente dice «Sin dato» y nunca $ 0; un 0 es un dato real.
 */
export function MontoTotal({ montoTotal, liquidacionesSinMonto, parcial }: { montoTotal: number | null; liquidacionesSinMonto: number; parcial: boolean }) {
  const id = useId();
  const sinMonto = Number.isFinite(liquidacionesSinMonto) && liquidacionesSinMonto > 0 ? liquidacionesSinMonto : 0;
  const esParcial = parcial === true || sinMonto > 0;
  const sinDato = montoTotal == null || !Number.isFinite(montoTotal);

  return (
    <section aria-labelledby={`${id}-titulo`} className={`${SECCION_CLS} flex flex-col gap-2`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id={`${id}-titulo`} className={TITULO_SECCION_CLS}>
          Monto total liquidado
        </h2>
        {esParcial ? <StatusBadge tone="purple" label={LITERAL_PARCIAL} /> : null}
      </div>
      <p
        className={`m-0 text-[length:var(--di-text-cifra)] font-bold tabular-nums ${
          sinDato ? "text-[color:var(--di-text-tertiary)]" : "text-[color:var(--kit-azul-texto)]"
        }`}
      >
        {sinDato ? MONTO_SIN_DATO : formatoMonto(montoTotal)}
      </p>
      {sinMonto > 0 ? <p className={`m-0 ${TEXTO_SECUNDARIO_CLS}`}>{LITERAL_SIN_MONTO.replace("{n}", String(sinMonto))}</p> : null}
      {parcial === true ? <p className={`m-0 ${TEXTO_SECUNDARIO_CLS}`}>{NOTA_PARCIAL}</p> : null}
    </section>
  );
}
