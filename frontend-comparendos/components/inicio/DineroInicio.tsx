"use client";

import Link from "next/link";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { BarrasApiladas } from "@/components/inicio/BarrasApiladas";
import { GraficoArea } from "@/components/inicio/GraficoArea";
import { GraficoDona } from "@/components/inicio/GraficoDona";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { ESTADO_LEGAL } from "@/lib/etiquetas";
import { formatoPesos } from "@/lib/format/date";
import { etiquetaMes, hrefBandeja, numeroCorto, pesosCorto, textoPeriodo, type RespuestaDinero } from "@/lib/reportes";

// Bloque de dinero del Inicio (rediseño de Comparendos, S2). Vive aparte para que la página lo cargue con
// next/dynamic: recharts solo baja cuando la empresa tiene el módulo de reportes (HANNAH).

/** Color de cada estado legal: el mismo tono que su etiqueta en la bandeja. */
const COLOR_TONO: Record<string, string> = {
  neutral: "var(--kit-gris)",
  success: "var(--kit-verde)",
  danger: "var(--kit-rojo)",
  warning: "var(--kit-naranja)",
  info: "var(--kit-azul)",
};

const SERIES_MENSUAL = [
  { clave: "valorIngresado", etiqueta: "Valor de lo que ingresó", color: "var(--kit-azul)" },
  { clave: "pagado", etiqueta: "Pagado", color: "var(--kit-verde)" },
];

const SERIES_ORGANISMO = [
  { clave: "exigible", etiqueta: "Se puede cobrar", color: "var(--kit-azul)" },
  { clave: "noExigible", etiqueta: "Caducado o prescrito", color: "var(--kit-gris)" },
];

/** Tarjetas de dinero, tendencia, deuda por estado legal y dinero por organismo, de /reportes/dinero. */
export function DineroInicio({ datos, empresa }: { datos: RespuestaDinero; empresa: string | null }) {
  const d = datos.dinero;
  const periodo = textoPeriodo(datos.alcance.desde, datos.alcance.hasta);
  const total = d.exigible.valor + d.noExigible.valor;
  const porciones = d.estados.map((e) => ({
    clave: e.estado,
    etiqueta: ESTADO_LEGAL[e.estado]?.texto ?? e.estado,
    valor: e.valor,
    color: COLOR_TONO[ESTADO_LEGAL[e.estado]?.tono ?? "neutral"] ?? "var(--kit-gris)",
    detalle: e.cantidad.toLocaleString("es-CO"),
  }));
  return (
    <>
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
        {d.cobroCoactivo.cantidad > 0 && (
          <TarjetaMetrica
            etiqueta="En cobro coactivo"
            cifra={pesosCorto(d.cobroCoactivo.valor)}
            pendiente
            title={formatoPesos(d.cobroCoactivo.valor)}
            aria-label={`En cobro coactivo: ${d.cobroCoactivo.cantidad.toLocaleString("es-CO")} comparendos · ${formatoPesos(d.cobroCoactivo.valor)}. Ver en la bandeja`}
            pie={`${d.cobroCoactivo.cantidad.toLocaleString("es-CO")} comparendos · ver en la bandeja`}
            href={hrefBandeja({ cobroCoactivo: "true" }, { empresa })}
            enlace={Link}
          />
        )}
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <PanelInicio titulo="Tendencia de los últimos 12 meses" subtitulo="Valor de los comparendos que ingresaron y lo que se pagó, mes a mes." icono="reportes">
          <GraficoArea
            puntos={d.mensual.map((m) => ({ etiqueta: etiquetaMes(m.periodo), valorIngresado: m.valorIngresado, pagado: m.pagado }))}
            series={SERIES_MENSUAL}
            formatoEje={(v) => (v === 0 ? "0" : `$${numeroCorto(v)}`)}
            formato={formatoPesos}
          />
        </PanelInicio>
        <PanelInicio titulo="Deuda por estado legal" subtitulo={`Lo activo y sin pagar, a hoy: ${pesosCorto(total)}.`} icono="mazo">
          <GraficoDona
            porciones={porciones}
            total={d.exigible.cantidad + d.noExigible.cantidad}
            centro={(d.exigible.cantidad + d.noExigible.cantidad).toLocaleString("es-CO")}
            rotuloCentro="en deuda"
            formato={pesosCorto}
            titulo={(p) => formatoPesos(p.valor)}
          />
        </PanelInicio>
      </div>

      <PanelInicio titulo="Dinero por organismo de tránsito" subtitulo="Los 10 con más deuda; la parte gris ya caducó o prescribió." icono="pesos">
        {d.organismos.length === 0 ? (
          <EstadoVacio titulo="Sin deuda pendiente" mensaje="No hay comparendos activos sin pagar." />
        ) : (
          <BarrasApiladas
            filas={d.organismos.map((o) => ({ etiqueta: o.nombre, exigible: o.exigible, noExigible: o.valor - o.exigible }))}
            series={SERIES_ORGANISMO}
            formatoEje={(v) => (v === 0 ? "0" : `$${numeroCorto(v)}`)}
            formato={formatoPesos}
            anchoRotulo={150}
          />
        )}
      </PanelInicio>
    </>
  );
}
