"use client";

import { InlineAlert } from "@flit/ui";
import { Cargador } from "@flit/ui/Loader";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { Encabezado } from "@/components/Pantalla";
import { BarrasSimples } from "@/components/inicio/BarrasSimples";
import { OperacionInicio, type CorteBandeja } from "@/components/inicio/OperacionInicio";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { formatoFecha, formatoPesos } from "@/lib/format/date";
import { hrefBandeja, pesosCorto } from "@/lib/reportes";
import { useCarga } from "@/lib/useCarga";
import { BarraFiltros, useFiltrosUrl } from "./filtrosUrl";
import type { RespuestaGestion } from "./tipos";

const n = (x: number) => x.toLocaleString("es-CO");

/**
 * Pestaña Gestión (Reportes 2.0): lo que salió del Inicio (vehículos, infracciones, evitables, antigüedad, DP por
 * organismo y el movimiento) más el top de organismos. Sin fechas en la URL es lo pendiente de hoy, igual que el Inicio
 * (así «Ver en Reportes» muestra las mismas cifras); con fechas, lo impuesto en el periodo.
 */
export function GestionReporte() {
  const { filtros, escribir, errorUrl } = useFiltrosUrl();
  const conPeriodo = !!(filtros.desde || filtros.hasta);
  const clave = errorUrl ? null : `gestion:${JSON.stringify(filtros)}`;
  const datos = useCarga(clave, (signal) =>
    apiFetch<RespuestaGestion>("/reportes/gestion", { signal, query: { empresa: filtros.empresa, nit: filtros.nit, desde: filtros.desde, hasta: filtros.hasta } }),
  );
  const g = datos.estado === "ready" ? datos.datos?.gestion : undefined;
  const a = datos.estado === "ready" ? datos.datos?.alcance : undefined;
  const corte: CorteBandeja = {
    imposicionDesde: a?.desde ?? undefined,
    imposicionHasta: a?.hasta ?? undefined,
    nits: filtros.nit ?? undefined,
  };
  const r = g?.operacion.resumen;
  const ventana = g?.conPeriodo && a?.desde && a?.hasta ? `del ${formatoFecha(a.desde)} al ${formatoFecha(a.hasta)}` : "en los últimos 30 días";

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Gestión"
        descripcion="Cómo va la gestión de lo pendiente: vehículos que más repiten, infracciones, lo que se pudo evitar, antigüedad, calendario legal y derechos de petición por organismo. Cada cifra abre la bandeja con el mismo corte."
      />
      <BarraFiltros filtros={filtros} escribir={escribir} errorUrl={errorUrl} />
      <div className="flex flex-wrap items-center gap-3 text-sm text-[color:var(--di-text-secondary)]" data-testid="gestion-alcance">
        {conPeriodo ? (
          <>
            <span>Comparendos pendientes impuestos en el periodo elegido (lo que no tiene fecha de imposición queda fuera).</span>
            <button type="button" className={controlCls(false)} onClick={() => escribir({ desde: null, hasta: null })}>
              Ver todo lo pendiente
            </button>
          </>
        ) : (
          <span>Todo lo pendiente a hoy, sin importar cuándo se impuso. Elige un periodo y pulsa «Consultar» para ver solo lo impuesto en él.</span>
        )}
      </div>

      {datos.estado === "loading" && <Cargador />}
      {datos.estado === "error" && (
        <InlineAlert tone="error" title="No se pudo cargar la gestión">
          {datos.error}{" "}
          <button type="button" className="font-semibold underline" onClick={datos.recargar}>
            Reintentar
          </button>
        </InlineAlert>
      )}

      {g && r && (
        <>
          <div className={METRICAS_GRID_CLS} data-testid="gestion-resumen">
            <TarjetaMetrica
              etiqueta="Pendientes"
              cifra={n(r.pendientes)}
              pie={r.valorPendiente === null ? "Activos y sin pagar" : `${pesosCorto(r.valorPendiente)} · activos y sin pagar`}
              title={r.valorPendiente === null ? undefined : formatoPesos(r.valorPendiente)}
            />
            <TarjetaMetrica etiqueta="Vehículos" cifra={n(r.vehiculos)} pie="Con al menos un pendiente" />
            <TarjetaMetrica etiqueta="Entraron" cifra={n(r.nuevos30)} pie={`Comparendos nuevos ${ventana}`} />
            <TarjetaMetrica etiqueta="Dejaron de estar pendientes" cifra={n(r.resueltos30)} pie={`Pagados o cerrados ${ventana}`} />
          </div>

          <OperacionInicio o={g.operacion} empresa={filtros.empresa} corte={corte} />

          <PanelInicio
            titulo="Organismos con más comparendos"
            subtitulo="Dónde se concentra lo pendiente. Cada organismo abre la bandeja con sus comparendos sin pagar."
            icono="ruta"
            data-testid="top-organismos"
          >
            <BarrasSimples
              color="var(--kit-azul)"
              vacio="Sin comparendos pendientes."
              filas={g.topOrganismos.map((o) => ({
                clave: o.organismoId ?? "sin-organismo",
                etiqueta: o.nombre,
                valor: o.cantidad,
                cifra: `${n(o.cantidad)} · ${pesosCorto(o.valor)}`,
                href: o.organismoId && o.cantidad > 0 ? hrefBandeja({ organismos: o.organismoId, pagado: "false", ...limpio(corte) }, { empresa: filtros.empresa }) : undefined,
                ariaLabel: o.organismoId ? `${o.nombre}: ${n(o.cantidad)} comparendos pendientes por ${formatoPesos(o.valor)}. Ver en la bandeja` : undefined,
              }))}
            />
          </PanelInicio>
        </>
      )}
    </section>
  );
}

const limpio = (c: CorteBandeja): Record<string, string> => Object.fromEntries(Object.entries(c).filter(([, v]) => !!v)) as Record<string, string>;
