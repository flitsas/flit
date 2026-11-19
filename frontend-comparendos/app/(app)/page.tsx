"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { InlineAlert } from "@flit/ui";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Cargador } from "@flit/ui/Loader";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { BarrasApiladas } from "@/components/inicio/BarrasApiladas";
import { GraficoArea } from "@/components/inicio/GraficoArea";
import { GraficoDona } from "@/components/inicio/GraficoDona";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { apiFetch } from "@/lib/api";
import { ESTADO_LEGAL } from "@/lib/etiquetas";
import { diaColombia, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import {
  etiquetaMes,
  FRENTES,
  hrefBandeja,
  inicioDeMes,
  numeroCorto,
  pesosCorto,
  textoPeriodo,
  type Frentes,
  type Monto,
  type RespuestaDinero,
} from "@/lib/reportes";
import { capacidades, tienePermiso } from "@/lib/sesion";
import type { Contadores } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

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

/** Frentes sin el módulo de reportes: solo cantidades, de los mismos contadores de la bandeja. */
function useFrentesSinDinero(listo: boolean, empresa: string | null): { frentes: Record<keyof Frentes, Monto | null> | null; error: string | null } {
  const q = { empresa: empresa ?? undefined };
  const base = useCarga(listo ? `inicio-contadores:${empresa}` : null, (signal) => apiFetch<Contadores>("/comparendos/contadores", { signal, query: q }));
  const sinGestion = useCarga(listo ? `inicio-sin-gestion:${empresa}` : null, (signal) =>
    apiFetch<Contadores>("/comparendos/contadores", { signal, query: { ...q, sinGestion: true } }),
  );
  const caducidad = useCarga(listo ? `inicio-caducidad:${empresa}` : null, (signal) =>
    apiFetch<Contadores>("/comparendos/contadores", { signal, query: { ...q, juridico: "CaducidadInmediata" } }),
  );
  const error = base.error ?? sinGestion.error ?? caducidad.error ?? null;
  if (!base.datos) return { frentes: null, error };
  const n = (cantidad: number | undefined): Monto | null => (cantidad === undefined ? null : { cantidad, valor: 0 });
  return {
    error,
    frentes: {
      proximosAVencer: n(base.datos.proximosAVencer),
      sinContraventor: n(base.datos.sinContraventor),
      rechazadosOt: n(base.datos.rechazadosOt),
      vencidos: n(base.datos.estadosLegales.Vencido),
      sinGestion: n(sinGestion.datos?.total),
      caducidadInmediata: n(caducidad.datos?.total),
    },
  };
}

function ParaHoy({
  frentes,
  conDinero,
  descuento,
  empresa,
}: {
  frentes: Record<keyof Frentes, Monto | null> | null;
  conDinero: boolean;
  descuento: { cantidad: number; ahorro: number } | null;
  empresa: string | null;
}) {
  const conCifra = FRENTES.filter((f) => (frentes?.[f.clave]?.cantidad ?? 0) > 0).length;
  const subtitulo = !frentes
    ? "Contando lo que necesita atención…"
    : conCifra === 0
      ? "Nada urgente hoy."
      : `Hay ${conCifra} ${conCifra === 1 ? "frente" : "frentes"} por atender. Cada tarjeta abre la bandeja con ese filtro.`;
  return (
    <PanelInicio titulo="Para hoy" subtitulo={subtitulo} icono="alerta" data-testid="para-hoy">
      <div className={METRICAS_GRID_CLS}>
        {FRENTES.map((f) => {
          const m = frentes?.[f.clave] ?? null;
          const cantidad = m?.cantidad ?? null;
          const vacio = cantidad === 0;
          return (
            <TarjetaMetrica
              key={f.clave}
              data-testid={`frente-${f.clave}`}
              etiqueta={f.titulo}
              cifra={cantidad === null ? "—" : cantidad.toLocaleString("es-CO")}
              pendiente={!!f.pendiente && !!cantidad}
              colorCifra={vacio ? "var(--di-text-tertiary)" : undefined}
              title={conDinero && m ? `${cantidad?.toLocaleString("es-CO")} comparendos · ${formatoPesos(m.valor)}` : undefined}
              pie={conDinero && m && !vacio ? `${pesosCorto(m.valor)} · ${f.accion}` : f.accion}
              href={vacio || cantidad === null ? undefined : hrefBandeja(f.query, { empresa })}
              enlace={Link}
            />
          );
        })}
        {conDinero && descuento && (
          <TarjetaMetrica
            data-testid="frente-descuento"
            etiqueta="En ventana de descuento"
            cifra={descuento.cantidad.toLocaleString("es-CO")}
            colorCifra={descuento.cantidad === 0 ? "var(--di-text-tertiary)" : "var(--flit-success-ink)"}
            title={`Ahorro posible estimado: ${formatoPesos(descuento.ahorro)}`}
            pie={descuento.cantidad > 0 ? `Pagar ya ahorraría ~${pesosCorto(descuento.ahorro)} (estimado)` : "Ninguno con descuento vigente"}
          />
        )}
      </div>
    </PanelInicio>
  );
}

function Dinero({ datos, empresa }: { datos: RespuestaDinero; empresa: string | null }) {
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
        <PanelInicio titulo="Deuda por estado legal" subtitulo="Lo activo y sin pagar, a hoy." icono="mazo">
          <GraficoDona
            porciones={porciones}
            total={d.exigible.cantidad + d.noExigible.cantidad}
            centro={pesosCorto(total).replace("$ ", "$")}
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

function Contenido() {
  const { sesion } = useSesion();
  const params = useSearchParams();
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  const [empresa, setEmpresa] = useState<string | null>(() => params.get("empresa"));
  const listo = !!sesion && (!esSuperAdmin || !!empresa);
  const conDinero = listo && capacidades(sesion).reportes && tienePermiso(sesion, P.reportesVer);
  const veBandeja = tienePermiso(sesion, P.bandejaVer);
  const empresaQuery = esSuperAdmin ? empresa : null;

  const hoy = diaColombia();
  const dinero = useCarga(conDinero ? `inicio-dinero:${empresaQuery}` : null, (signal) =>
    apiFetch<RespuestaDinero>("/reportes/dinero", { signal, query: { empresa: empresaQuery, desde: inicioDeMes(hoy), hasta: hoy } }),
  );
  const sinDinero = useFrentesSinDinero(listo && !conDinero && veBandeja, empresaQuery);
  const d = dinero.datos?.dinero;
  const frentes = conDinero ? (d?.frentes ?? null) : sinDinero.frentes;
  const error = conDinero ? dinero.error : sinDinero.error;

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Inicio"
        ilustracion="comparendos"
        descripcion={
          esSuperAdmin
            ? "Vista de administración FLIT: elige una empresa para ver su tablero."
            : `${sesion?.empresa?.nombre ? `${sesion.empresa.nombre} · ` : ""}Lo que hay que atender hoy${conDinero ? " y cómo va la plata" : ""}.`
        }
        acciones={esSuperAdmin ? <SelectorEmpresa valor={empresa} onCambio={setEmpresa} /> : undefined}
      />

      {!sesion && <Cargador />}
      {sesion && esSuperAdmin && !empresa && (
        <EstadoVacio titulo="Elige una empresa" mensaje="El tablero muestra los comparendos de una empresa a la vez." />
      )}
      {error && (
        <InlineAlert tone="error" title="No se pudo cargar el tablero">
          {error}{" "}
          <button type="button" className="font-semibold underline" onClick={conDinero ? dinero.recargar : () => window.location.reload()}>
            Reintentar
          </button>
        </InlineAlert>
      )}

      {listo && veBandeja && (
        <ParaHoy
          frentes={frentes}
          conDinero={conDinero}
          descuento={d ? { cantidad: d.descuento.d50.cantidad + d.descuento.d25.cantidad, ahorro: d.descuento.d50.ahorroEstimado + d.descuento.d25.ahorroEstimado } : null}
          empresa={empresaQuery}
        />
      )}
      {conDinero && dinero.estado === "loading" && <Cargador />}
      {conDinero && dinero.datos && <Dinero datos={dinero.datos} empresa={empresaQuery} />}
    </section>
  );
}

/**
 * Inicio de Comparendos (rediseño, S2): «Para hoy» con los frentes que llevan a la bandeja filtrada y, si la empresa
 * tiene el módulo de reportes, la lente de dinero (deuda cobrable contra caducada o prescrita, ahorro, pagado,
 * tendencia y organismos). Sin el módulo solo usa los contadores de la bandeja: nada de cifras en pesos ni 403.
 */
export default function Inicio() {
  return (
    <Suspense fallback={<Cargador />}>
      <Contenido />
    </Suspense>
  );
}
