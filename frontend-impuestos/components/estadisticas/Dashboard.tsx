"use client";

import { useEffect, useId, useMemo, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { BOTON_PRIMARIO_CLS } from "@/components/comun/estilos";
import { useSesion } from "@/components/ImpuestosShell";
import { ApiError } from "@/lib/api";
import { useEmpresaBackoffice } from "@/lib/backoffice/api";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { mensajeErrorFijo } from "@/lib/errores";
import { ERRORES_DASHBOARD, esErrorDeRango, obtenerDashboard } from "@/lib/estadisticas/api";
import { hoyColombia, leerRango, mensajeRangoInvalido, rangoPorDefecto } from "@/lib/estadisticas/periodo";
import type { DashboardImpuestos, Rango } from "@/lib/estadisticas/tipos";
import { PERMISOS, tienePermiso } from "@/lib/sesion";
import { Contadores, type EnlacesContadores, type EstadoCarga } from "./Contadores";
import { DistribucionDepartamento } from "./DistribucionDepartamento";
import { FiltroPeriodo } from "./FiltroPeriodo";
import { SECCION_CLS, TITULO_SECCION_CLS } from "./Figura";
import { GraficaPorDia } from "./GraficaPorDia";
import { MontoTotal } from "./MontoTotal";
import { RankingEmpresas } from "./RankingEmpresas";
import { UltimosLotes } from "./UltimosLotes";

const CHIP_GLOBAL_CLS =
  "inline-flex w-fit items-center gap-1 rounded-full bg-[var(--di-accent-soft)] px-2.5 py-0.5 text-xs font-semibold text-[color:var(--kit-azul-texto)]";
const SKELETON_CLS = "h-40 animate-pulse rounded-[9px] bg-[var(--di-surface-sunken)] motion-reduce:animate-none";

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; datos: DashboardImpuestos };

function urlCon(base: string, params: Record<string, string | undefined>, empresa: string | null): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) q.set(k, v);
  const s = q.toString();
  return hrefConEmpresa(s ? `${base}?${s}` : base, empresa);
}

/**
 * Bloque que llegó null (forma de BloqueSinDatos de Diagnóstico): conserva su título y dice «Sin datos para este bloque»
 * en vez de pintar ceros. Aviso estático (`vivo={false}`).
 */
function BloqueSinDatos({ titulo }: { titulo: string }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={SECCION_CLS}>
      <h2 id={id} className={TITULO_SECCION_CLS}>
        {titulo}
      </h2>
      <InlineAlert tone="warning" compact vivo={false}>
        Sin datos para este bloque
      </InlineAlert>
    </section>
  );
}

/**
 * Dashboard de Impuestos en / (spec-visual §3.1; D-DASH; impuestos.v1.yaml GET /dashboard). Rango por defecto: los últimos
 * 30 días (también desde ?desde&hasta). La compañía sale del token; el Super Admin sin ?empresa= ve la vista global con
 * el ranking y el chip «Vista global», y con ?empresa= el de esa compañía (el selector vive en la barra). Cuatro estados:
 * cargando (skeleton), vacío (EstadoVacio), error (InlineAlert + Reintentar; 403 → página 403) y lleno. Sin datos
 * personales. Sin impuestos.dashboard.ver, la 403.
 */
export function Dashboard() {
  const router = useRouter();
  const params = useSearchParams();
  const { sesion } = useSesion();
  const { empresa: empresaUrl, elegir } = useEmpresaSeleccionada();
  const puede = tienePermiso(sesion, PERMISOS.dashboardVer);
  const sinAcceso = sesion !== null && !puede;
  const superAdmin = sesion?.esSuperAdmin === true;
  // Solo el Super Admin elige compañía: para otro usuario `empresa` respondería 400 EMPRESA_NO_PERMITIDA.
  const empresa = superAdmin ? empresaUrl : null;
  const vistaGlobal = superAdmin && !empresa;

  const hoy = useMemo(() => hoyColombia(), []);
  const [rango, setRango] = useState<Rango>(() => leerRango(params ?? new URLSearchParams(), hoy));
  const [intento, setIntento] = useState(0);
  // El resultado lleva la clave de la consulta que lo pidió: con otra clave (rango, compañía o reintento) se ve «cargando».
  const clave = `${rango.desde}|${rango.hasta}|${empresa ?? ""}|${intento}`;
  const [resultado, setResultado] = useState<{ clave: string; carga: Carga } | null>(null);
  const carga: Carga = resultado?.clave === clave ? resultado.carga : { tipo: "cargando" };
  const elegida = useEmpresaBackoffice(empresa);

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISO");
  }, [sinAcceso, router]);

  useEffect(() => {
    if (!sesion || !puede) return;
    const controller = new AbortController();
    obtenerDashboard(rango, empresa, controller.signal).then(
      (datos) => {
        if (!controller.signal.aborted) setResultado({ clave, carga: { tipo: "listo", datos } });
      },
      (error: unknown) => {
        if (!controller.signal.aborted) setResultado({ clave, carga: { tipo: "error", error } });
      },
    );
    return () => controller.abort();
  }, [sesion, puede, rango, empresa, clave]);

  const error = carga.tipo === "error" ? carga.error : null;
  const codigo403 = error instanceof ApiError && error.status === 403 ? error.code || "SIN_PERMISO" : null;
  useEffect(() => {
    if (codigo403) router.replace(`/403?code=${encodeURIComponent(codigo403)}`);
  }, [codigo403, router]);

  if (!sesion || !puede) return null;

  const errorDeRango = esErrorDeRango(error) ? mensajeRangoInvalido(rango, hoy) : null;
  const empresaNoEncontrada = error instanceof ApiError && error.code === "EMPRESA_NO_ENCONTRADA";
  const datos = carga.tipo === "listo" ? carga.datos : null;
  const estadoKpis: EstadoCarga = carga.tipo === "listo" ? "listo" : carga.tipo === "error" ? "error" : "cargando";

  const nombreAmbito = vistaGlobal ? "todas las compañías" : empresa ? (elegida?.nombre ?? "la compañía elegida") : sesion.empresa.nombre || "tu compañía";
  const suspendido = sesion.aprovisionamiento?.estado === "SUSPENDIDO";
  const puedeLiquidar = tienePermiso(sesion, PERMISOS.liquidacionesCrear) && !suspendido && !vistaGlobal;
  const periodo = { desde: rango.desde, hasta: rango.hasta };
  const enlaces: EnlacesContadores = {
    liquidaciones: urlCon("/liquidaciones", periodo, empresa),
    lotes: urlCon("/liquidaciones", { tipo: "MASIVA", estado: "EN_PROCESO" }, empresa),
    errores: urlCon("/reportes", { reporte: "errores", ...periodo }, empresa),
    deuda: urlCon("/reportes", { reporte: "deuda", ...periodo }, empresa),
  };

  let contenido: ReactNode = null;
  if (carga.tipo === "cargando") {
    contenido = (
      <div role="status" aria-busy="true" className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <span className="sr-only">Cargando el resumen…</span>
        <div aria-hidden="true" className={`${SKELETON_CLS} lg:col-span-2`} />
        <div aria-hidden="true" className={SKELETON_CLS} />
      </div>
    );
  } else if (carga.tipo === "error") {
    contenido =
      codigo403 || errorDeRango ? null : (
        <InlineAlert
          tone="error"
          action={
            empresaNoEncontrada ? (
              <Button variant="secondary" size="sm" onClick={() => elegir(null)}>
                Ver todas las compañías
              </Button>
            ) : (
              <Button variant="secondary" size="sm" onClick={() => setIntento((n) => n + 1)}>
                Reintentar
              </Button>
            )
          }
        >
          {mensajeErrorFijo(error, "No se pudo cargar el resumen", ERRORES_DASHBOARD)}
        </InlineAlert>
      );
  } else if (datos && datos.contadores?.total === 0) {
    const acciones: ReactNode[] = [];
    if (puedeLiquidar)
      acciones.push(
        <Link key="liquidar" href={hrefConEmpresa("/liquidaciones/nueva", empresa)} className={BOTON_PRIMARIO_CLS}>
          Liquidar por placa
        </Link>,
      );
    acciones.push(
      <Button key="90" variant="secondary" size="sm" onClick={() => setRango(rangoPorDefecto(hoy, 90))}>
        Ver los últimos 90 días
      </Button>,
    );
    contenido = (
      <section className={SECCION_CLS}>
        <EstadoVacio titulo="Aún no hay liquidaciones en este periodo" mensaje="Cambia el rango de fechas o liquida una placa." acciones={acciones} />
      </section>
    );
  } else if (datos) {
    contenido = (
      <>
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          {datos.serie ? (
            <section className={`${SECCION_CLS} lg:col-span-2`}>
              <GraficaPorDia serie={datos.serie} />
            </section>
          ) : (
            <div className="lg:col-span-2">
              <BloqueSinDatos titulo="Liquidaciones por día" />
            </div>
          )}
          <MontoTotal montoTotal={datos.montoTotal} liquidacionesSinMonto={datos.liquidacionesSinMonto} parcial={datos.parcial} />
        </div>
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          {datos.porDepartamento ? (
            <section className={`${SECCION_CLS} lg:col-span-2`}>
              <DistribucionDepartamento items={datos.porDepartamento} />
            </section>
          ) : (
            <div className="lg:col-span-2">
              <BloqueSinDatos titulo="Por departamento" />
            </div>
          )}
          {datos.ultimosLotes ? <UltimosLotes lotes={datos.ultimosLotes} empresa={empresa} /> : <BloqueSinDatos titulo="Últimos 5 lotes" />}
        </div>
        {vistaGlobal && datos.ranking && datos.ranking.length > 0 ? (
          <section className={SECCION_CLS}>
            <RankingEmpresas ranking={datos.ranking} onElegir={(id) => elegir(id)} />
          </section>
        ) : null}
      </>
    );
  }

  return (
    <>
      <CabeceraModulo
        as="section"
        aria-labelledby="dashboard-titulo"
        tituloId="dashboard-titulo"
        titulo="Dashboard"
        icono="pagos"
        descripcion={
          <span className="flex flex-col gap-1">
            <span>Resumen de {nombreAmbito}</span>
            {vistaGlobal ? <span className={CHIP_GLOBAL_CLS}>Vista global</span> : null}
          </span>
        }
        acciones={<FiltroPeriodo valor={rango} hoy={hoy} onAplicar={setRango} errorServidor={errorDeRango} />}
      />
      <Contadores datos={datos} estado={estadoKpis} enlaces={enlaces} />
      {contenido}
    </>
  );
}
