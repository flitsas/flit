"use client";

import { useEffect, useMemo, useState, type ReactNode } from "react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { DataTable, type DataTableColumn } from "@flit/ui/DataTable";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { BOTON_PRIMARIO_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { nombreFuente } from "@/lib/configuracion";
import { ambitoDe, esErrorDeRango, mensajeErrorEstadisticas } from "@/lib/estadisticas/api";
import { formatoEntero, formatoPorcentaje, formatoSegundos, rotuloCubeta, TEXTO_GRANULARIDAD } from "@/lib/estadisticas/formato";
import { hoyColombia, leerRango, rangoPorDefecto } from "@/lib/estadisticas/periodo";
import type { EstadisticaFuente, EstadisticasDto, Rango } from "@/lib/estadisticas/tipos";
import { FiltrosDashboard } from "./FiltrosDashboard";
import { SelectorCompania } from "./SelectorCompania";
import { TarjetasResumen } from "./TarjetasResumen";
import { useEstadisticas } from "./useEstadisticas";
import { BarraApilada } from "./graficas/BarraApilada";
import { BarrasSerie } from "./graficas/BarrasSerie";
import { SECCION_CLS, TITULO_SECCION_CLS } from "./graficas/Figura";
import { ListaBarras } from "./graficas/ListaBarras";


const CHIP_GLOBAL_CLS =
  "inline-flex w-fit items-center gap-1 rounded-full bg-[var(--di-accent-soft)] px-2.5 py-0.5 text-xs font-semibold text-[color:var(--kit-azul-texto)]";

const ESTADOS_SANEAMIENTO: Array<{ estado: string; nombre: string }> = [
  { estado: "SOLICITADO", nombre: "Solicitado" },
  { estado: "EN_GESTION", nombre: "En gestión" },
  { estado: "SANEADO", nombre: "Saneado" },
  { estado: "NO_PROCEDE", nombre: "No procede" },
  { estado: "ANULADO", nombre: "Anulado" },
];

const COLUMNAS_FUENTES: DataTableColumn<EstadisticaFuente>[] = [
  { key: "fuente", header: "Fuente", render: (f) => <span className="font-semibold">{nombreFuente(f.fuente)}</span> },
  { key: "consultas", header: "Consultas", align: "right", render: (f) => formatoEntero(f.consultas) },
  { key: "exitosas", header: "Exitosas", align: "right", render: (f) => formatoEntero(f.exitosas) },
  { key: "con_error", header: "Con error", align: "right", render: (f) => formatoEntero(f.con_error) },
  { key: "no_aplica", header: "No aplica", align: "right", render: (f) => formatoEntero(f.no_aplica) },
  { key: "pendientes", header: "Pendientes", align: "right", render: (f) => formatoEntero(f.pendientes) },
  {
    key: "tasa",
    header: "Tasa de éxito",
    render: (f) => (
      <span className="flex items-center gap-2">
        <span aria-hidden="true" className="h-1.5 w-16 shrink-0 overflow-hidden rounded-full bg-[var(--di-surface-sunken)]">
          <span className="block h-full rounded-full bg-[var(--di-success-dot)]" style={{ width: `${Math.round((f.tasa_exito ?? 0) * 100)}%` }} />
        </span>
        <span className="tabular-nums">{formatoPorcentaje(f.tasa_exito)}</span>
      </span>
    ),
  },
  { key: "tiempo", header: "Tiempo promedio", align: "right", render: (f) => formatoSegundos(f.tiempo_promedio_ms) },
];

function urlCon(base: string, params: Record<string, string | undefined>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) q.set(k, v);
  const s = q.toString();
  return s ? `${base}?${s}` : base;
}

interface Vista {
  rango: Rango;
  empresa: string;
}

function vistaDeUrl(params: Pick<URLSearchParams, "get">, hoy: string, vistaGlobal: boolean): Vista {
  const empresa = vistaGlobal ? (params.get("empresa") ?? "").trim() : "";
  return { rango: leerRango(params, hoy), empresa };
}

/** Secciones del dashboard con datos (estado «lleno»). Cada bloque cae a su vacío si viene sin datos. */
function Secciones({ datos, vistaGlobal, onElegirEmpresa }: { datos: EstadisticasDto; vistaGlobal: boolean; onElegirEmpresa: (id: string) => void }) {
  const granularidad = datos.periodo?.granularidad ?? "DIA";
  const serie = datos.serie ?? [];
  const veredictos = datos.veredictos ?? { viables: 0, bloqueados: 0, sin_dictamen: 0 };
  const tipos = datos.tipos_analisis ?? { simple: 0, con_analisis: 0 };
  const derivaciones = datos.derivaciones ?? { creadas: 0, omitidas: 0, fallidas: 0, en_curso: 0 };
  const saneamientos = new Map((datos.saneamientos ?? []).map((s) => [s.estado as string, s.cantidad]));
  const top = datos.top_empresas ?? [];

  return (
    <>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <section className={`${SECCION_CLS} lg:col-span-2`}>
          <BarrasSerie
            nivel={2}
            titulo="Uso en el tiempo"
            subtitulo={`Diagnósticos y vehículos ${TEXTO_GRANULARIDAD[granularidad]}`}
            etiquetas={serie.map((p) => rotuloCubeta(p.inicio, granularidad))}
            series={[
              { nombre: "Diagnósticos", color: "var(--chart-1)", valores: serie.map((p) => p.diagnosticos) },
              { nombre: "Vehículos", color: "var(--chart-2)", valores: serie.map((p) => p.vehiculos) },
            ]}
          />
        </section>
        <section aria-labelledby="dashboard-resultado" className={SECCION_CLS}>
          <h2 id="dashboard-resultado" className={TITULO_SECCION_CLS}>
            Resultado
          </h2>
          <BarraApilada
            titulo="Veredictos"
            subtitulo="Dictamen vigente de los vehículos"
            segmentos={[
              { nombre: "Viables", valor: veredictos.viables, color: "var(--di-success-dot)" },
              { nombre: "Bloqueados", valor: veredictos.bloqueados, color: "var(--di-danger)" },
              { nombre: "Sin dictamen", valor: veredictos.sin_dictamen, color: "var(--di-border-strong)" },
            ]}
          />
          <BarraApilada
            titulo="Tipo de análisis"
            segmentos={[
              { nombre: "Simple", valor: tipos.simple, color: "var(--chart-1)" },
              { nombre: "Con análisis", valor: tipos.con_analisis, color: "var(--chart-3)" },
            ]}
          />
        </section>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <section aria-labelledby="dashboard-fuentes" className={`${SECCION_CLS} min-w-0 lg:col-span-2`}>
          <h2 id="dashboard-fuentes" className={TITULO_SECCION_CLS}>
            Salud de las fuentes
          </h2>
          <DataTable
            ariaLabel="Salud de las fuentes"
            columns={COLUMNAS_FUENTES}
            rows={datos.fuentes ?? []}
            getRowKey={(f) => f.fuente}
            emptyMessage="No hubo consultas a fuentes en este periodo."
            minWidth={720}
            tarjeta={false}
          />
        </section>
        <div className="flex flex-col gap-4">
          <section className={SECCION_CLS}>
            <ListaBarras
              nivel={2}
              titulo="Saneamientos"
              subtitulo="Por estado, según la fecha de solicitud"
              items={ESTADOS_SANEAMIENTO.map((e) => ({ clave: e.estado, nombre: e.nombre, valor: saneamientos.get(e.estado) ?? 0 }))}
            />
          </section>
          <section className={SECCION_CLS}>
            <ListaBarras
              nivel={2}
              titulo="Trámites"
              subtitulo="Derivaciones a trámite del periodo"
              items={[
                { clave: "creadas", nombre: "Creadas", valor: derivaciones.creadas, color: "var(--di-success-dot)" },
                { clave: "omitidas", nombre: "Omitidas", valor: derivaciones.omitidas, color: "var(--di-border-strong)" },
                { clave: "fallidas", nombre: "Fallidas", valor: derivaciones.fallidas, color: "var(--di-danger)" },
                { clave: "en_curso", nombre: "En curso", valor: derivaciones.en_curso, color: "var(--chart-3)" },
              ]}
            />
          </section>
        </div>
      </div>

      {vistaGlobal && top.length > 0 ? (
        <section className={SECCION_CLS}>
          <ListaBarras
            nivel={2}
            titulo="Compañías con más uso"
            subtitulo="Por diagnósticos creados en el periodo"
            items={top.map((e) => ({
              clave: e.empresa_id,
              nombre: e.empresa_nombre,
              valor: e.diagnosticos,
              detalle: `${formatoEntero(e.vehiculos)} vehículos`,
            }))}
            onElegir={onElegirEmpresa}
            etiquetaAccion={(i) => `Ver el resumen de ${i.nombre}`}
          />
        </section>
      ) : null}
    </>
  );
}

/**
 * Dashboard de Diagnóstico (HU FE-5, spec-ui §4). Sin `vistaGlobal` (capacidad verEstadisticasGlobales) pide GET
 * /estadisticas de la compañía de la sesión, sin selector ni top. Con ella abre en «Todas las compañías» (GET
 * /backoffice/estadisticas/global, con el top) y el selector elige una compañía (?empresa=). El rango (últimos 30 días
 * por defecto) y la compañía viajan en la URL (desde, hasta, empresa). Cuatro estados: carga (VehicleLoader), vacío
 * (EstadoVacio), 403 (página 403) y error/503 (InlineAlert con Reintentar). Cero PII: la API no la manda (SP-16).
 */
export function Dashboard({ vistaGlobal, puedeCrear, empresaNombre }: { vistaGlobal: boolean; puedeCrear: boolean; empresaNombre?: string | null }) {
  const router = useRouter();
  const pathname = usePathname() || "/";
  const params = useSearchParams();
  const hoy = useMemo(() => hoyColombia(), []);
  const claveUrl = params.toString();
  const [urlVista, setUrlVista] = useState(claveUrl);
  const [vista, setVista] = useState<Vista>(() => vistaDeUrl(params, hoy, vistaGlobal));
  // La URL cambió desde fuera (atrás/adelante, enlace compartido): la vista la sigue.
  if (claveUrl !== urlVista) {
    setUrlVista(claveUrl);
    setVista(vistaDeUrl(params, hoy, vistaGlobal));
  }

  const ambito = ambitoDe(vistaGlobal, vista.empresa);
  const { estado, datos, error, reintentar } = useEstadisticas(ambito, vista.rango);

  function aplicar(siguiente: Vista) {
    setVista(siguiente);
    const url = urlCon(pathname, { desde: siguiente.rango.desde, hasta: siguiente.rango.hasta, empresa: vistaGlobal ? siguiente.empresa : undefined });
    router.replace(url, { scroll: false });
  }

  const es403 = estado === "error" && error instanceof ApiError && error.status === 403;
  const codigo403 = es403 ? (error as ApiError).code || "SIN_PERMISO" : null;
  useEffect(() => {
    if (codigo403) router.replace(`/403?code=${encodeURIComponent(codigo403)}`);
  }, [codigo403, router]);
  const errorDeRango = estado === "error" && esErrorDeRango(error) ? mensajeErrorEstadisticas(error) : null;

  const nombreAmbito = !vistaGlobal
    ? empresaNombre || "tu compañía"
    : vista.empresa
      ? (datos?.ambito?.empresa_nombre ?? "la compañía elegida")
      : "todas las compañías";
  const enlaces = !vistaGlobal
    ? {
        diagnosticos: urlCon("/diagnosticos", { desde: vista.rango.desde, hasta: vista.rango.hasta }),
        vehiculos: urlCon("/diagnosticos", { vista: "vehiculos", desde: vista.rango.desde, hasta: vista.rango.hasta }),
        saneamientos: urlCon("/diagnosticos", { vista: "vehiculos", saneamiento: "CON" }),
      }
    : {};

  let contenido: ReactNode;
  if (estado === "cargando") contenido = <VehicleLoader label="Cargando el resumen…" />;
  else if (estado === "error") {
    contenido =
      es403 || errorDeRango ? null : (
        <InlineAlert
          tone="error"
          action={
            <Button variant="secondary" size="sm" onClick={reintentar}>
              Reintentar
            </Button>
          }
        >
          No se pudo cargar el resumen
        </InlineAlert>
      );
  } else if (datos && (datos.resumen?.diagnosticos ?? 0) === 0) {
    const acciones: ReactNode[] = [];
    if (puedeCrear && !vistaGlobal)
      acciones.push(
        <Link key="nuevo" href="/diagnosticos/nuevo" className={BOTON_PRIMARIO_CLS}>
          <Icono nombre="mas" className="h-4 w-4" />
          Nuevo diagnóstico
        </Link>,
      );
    acciones.push(
      <Button key="90" variant="secondary" size="sm" onClick={() => aplicar({ ...vista, rango: rangoPorDefecto(hoy, 90) })}>
        Ver los últimos 90 días
      </Button>,
    );
    contenido = (
      <section className={SECCION_CLS}>
        <EstadoVacio titulo="Aún no hay diagnósticos en este periodo" mensaje="Cambia el rango de fechas o crea un diagnóstico." acciones={acciones} />
      </section>
    );
  } else if (datos) {
    contenido = <Secciones datos={datos} vistaGlobal={vistaGlobal} onElegirEmpresa={(id) => aplicar({ ...vista, empresa: id })} />;
  }

  return (
    <>
      <CabeceraModulo
        as="section"
        aria-labelledby="dashboard-titulo"
        tituloId="dashboard-titulo"
        titulo="Dashboard"
        icono="diagnostico"
        descripcion={
          <span className="flex flex-col gap-1">
            <span>Resumen de diagnósticos de {nombreAmbito}</span>
            {vistaGlobal && !vista.empresa ? <span className={CHIP_GLOBAL_CLS}>Vista global · todas las compañías</span> : null}
          </span>
        }
        acciones={
          <div className="flex w-full flex-wrap items-end gap-3 sm:w-auto">
            <FiltrosDashboard valor={vista.rango} hoy={hoy} onAplicar={(rango) => aplicar({ ...vista, rango })} errorServidor={errorDeRango} />
            {vistaGlobal ? <SelectorCompania value={vista.empresa} onChange={(empresa) => aplicar({ ...vista, empresa })} /> : null}
            {puedeCrear && !vistaGlobal ? (
              <Link href="/diagnosticos/nuevo" className={`${BOTON_PRIMARIO_CLS} w-full sm:w-auto`}>
                <Icono nombre="mas" className="h-4 w-4" />
                Nuevo diagnóstico
              </Link>
            ) : null}
          </div>
        }
      />
      <TarjetasResumen resumen={datos?.resumen ?? null} estado={errorDeRango ? "error" : estado} enlaces={enlaces} />
      {contenido}
    </>
  );
}
