"use client";

import { Cargador } from "@flit/ui/Loader";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { formatoPesos } from "@/lib/format/date";
import { pesosCorto, textoPeriodo, type RespuestaDineroInicio } from "@/lib/reportes";

/**
 * La franja de dinero del Inicio (rediseño R2; segunda desde R4): cuatro tarjetas en un bloque plegable y abierto, para
 * que la plata no compita con la operación. La tendencia, la dona, el dinero por organismo y el cobro coactivo están en Reportes.
 */
export function DineroInicio({ datos }: { datos: RespuestaDineroInicio | null }) {
  if (!datos)
    return (
      <TarjetaPlegable titulo="Dinero" abiertaInicial nivelTitulo={2}>
        {/* El alto de una fila de tarjetas: el bloque no crece cuando llegan las cifras (sin salto de la operación). */}
        <div className="flex min-h-[112px] items-center justify-center" aria-busy="true">
          <Cargador />
        </div>
      </TarjetaPlegable>
    );
  const d = datos.dinero;
  const periodo = textoPeriodo(datos.alcance.desde, datos.alcance.hasta);
  return (
    <TarjetaPlegable titulo="Dinero" abiertaInicial nivelTitulo={2}>
      <div className={METRICAS_GRID_CLS} data-testid="kpis-dinero">
        <TarjetaMetrica
          etiqueta="Deuda que se puede cobrar"
          cifra={pesosCorto(d.exigible.valor)}
          title={formatoPesos(d.exigible.valor)}
          pie={`${d.exigible.cantidad.toLocaleString("es-CO")} comparendos sin notificar, en tiempo o vencidos`}
        />
        <TarjetaMetrica
          etiqueta="Caducada o prescrita"
          cifra={pesosCorto(d.noExigible.valor)}
          colorCifra="var(--di-text-secondary)"
          title={formatoPesos(d.noExigible.valor)}
          pie={`${d.noExigible.cantidad.toLocaleString("es-CO")} comparendos que el organismo ya no puede cobrar`}
        />
        <TarjetaMetrica
          etiqueta="Ahorro logrado"
          cifra={pesosCorto(d.ahorroLogrado)}
          colorCifra="var(--flit-success-ink)"
          title={formatoPesos(d.ahorroLogrado)}
          pie={`Derechos de petición favorables · ${periodo}`}
        />
        <TarjetaMetrica
          etiqueta="Pagado"
          cifra={pesosCorto(d.pagado.total)}
          title={`${formatoPesos(d.pagado.total)} · CC ${formatoPesos(d.pagado.cc)} · PC ${formatoPesos(d.pagado.pc)} · BC ${formatoPesos(d.pagado.bc)}`}
          pie={`${d.pagado.cantidad.toLocaleString("es-CO")} pagos · ${periodo}`}
        />
        </div>
    </TarjetaPlegable>
  );
}
