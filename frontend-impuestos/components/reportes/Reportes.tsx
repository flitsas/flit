"use client";

import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Pestanas } from "@flit/ui/Pestanas";
import { UiStateBoundary } from "@flit/ui/UiStateBoundary";
import { TARJETA_CLS } from "@/components/comun/estilos";
import { useSesion } from "@/components/ImpuestosShell";
import { ApiError } from "@/lib/api";
import { useEmpresaBackoffice } from "@/lib/backoffice/api";
import { useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { esRechazoDeEmpresa, mensajeErrorFijo } from "@/lib/errores";
import { formatoEntero } from "@/lib/estadisticas/formato";
import { hoyColombia, leerRango, mensajeRangoInvalido } from "@/lib/estadisticas/periodo";
import {
  ERRORES_REPORTES,
  TAMANO_PAGINA_REPORTE,
  filtrosVacios,
  hayFiltros,
  obtenerCatalogosReportes,
  obtenerReporte,
  type CatalogosReportes,
  type FiltrosReporte as Filtros,
  type PaginaReporte,
} from "@/lib/reportes/api";
import { REPORTES, reporteDe, type TipoReporte } from "@/lib/reportes/catalogo";
import { useDescarga } from "@/lib/reportes/useDescarga";
import { PERMISOS, tienePermiso, type SesionImpuestos } from "@/lib/sesion";
import { BotonesExportar } from "./BotonesExportar";
import { FiltrosReporte } from "./FiltrosReporte";
import { TablaReporte } from "./TablaReporte";

const PARAM_REPORTE = "reporte";
const AVISO_PII =
  "Este reporte contiene datos personales (documentos y nombres) protegidos por la Ley 1581 de 2012. Úsalos solo para la finalidad autorizada. Cada descarga queda registrada con tu usuario.";
const AVISO_REGISTRO = "Cada descarga queda registrada con tu usuario.";

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; datos: PaginaReporte };

/**
 * /reportes (spec-visual §3.7; D-REPORTES; impuestos.v1.yaml tag Reportes). Cuatro pestañas (?reporte=), filtros del
 * reporte activo, tabla paginada con las columnas de la API y exportación a Excel (≤ 5.000) y PDF (≤ 1.000) de TODO el
 * filtro, sin límite de exportaciones por usuario (DA-9b). La compañía sale del token; el Super Admin la elige en la barra
 * (?empresa=); sin ella ve la vista global, con la columna «Compañía» (delta 11). Las opciones de «Departamento» y
 * «Usuario» salen de GET /catalogos/reportes (delta 12). Los errores de regla y de la compañía se pintan en línea con su
 * literal. Aviso de Ley 1581 persistente. Sin impuestos.reportes.ver, la 403; sin .exportar, no hay botones.
 */
export function Reportes() {
  const router = useRouter();
  const { sesion } = useSesion();
  const puede = tienePermiso(sesion, PERMISOS.reportesVer);
  const sinAcceso = sesion !== null && !puede;

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISO");
  }, [sinAcceso, router]);

  if (!sesion || !puede) return null;
  return <VistaReportes sesion={sesion} />;
}

function VistaReportes({ sesion }: { sesion: SesionImpuestos }) {
  const router = useRouter();
  const pathname = usePathname() || "/reportes";
  const params = useSearchParams();
  const { empresa: empresaUrl, elegir } = useEmpresaSeleccionada();
  const superAdmin = sesion.esSuperAdmin === true;
  const empresa = superAdmin ? empresaUrl : null;
  const vistaGlobal = superAdmin && !empresa;
  const puedeExportar = tienePermiso(sesion, PERMISOS.reportesExportar);
  const hoy = useMemo(() => hoyColombia(), []);
  const avisoId = useId();
  const filtrosRef = useRef<HTMLDivElement>(null);

  const reporteUrl = params?.get(PARAM_REPORTE) ?? null;
  const [tipoDeUrl, setTipoDeUrl] = useState(reporteUrl);
  const [tipo, setTipo] = useState<TipoReporte>(reporteDe(reporteUrl).tipo);
  // ?reporte= cambió desde fuera (atrás/adelante, enlace del dashboard): la pestaña lo sigue.
  if (reporteUrl !== tipoDeUrl) {
    setTipoDeUrl(reporteUrl);
    setTipo(reporteDe(reporteUrl).tipo);
  }
  const reporte = reporteDe(tipo);

  const [filtros, setFiltros] = useState<Filtros>(() => filtrosVacios(leerRango(params ?? new URLSearchParams(), hoy)));
  const [page, setPage] = useState(1);
  const [intento, setIntento] = useState(0);
  const clave = JSON.stringify([tipo, filtros, page, empresa, intento]);
  const [resultado, setResultado] = useState<{ clave: string; carga: Carga } | null>(null);
  const carga: Carga = resultado?.clave === clave ? resultado.carga : { tipo: "cargando" };

  useEffect(() => {
    const controller = new AbortController();
    obtenerReporte(tipo, filtros, { page, pageSize: TAMANO_PAGINA_REPORTE }, empresa, controller.signal).then(
      (datos) => {
        if (!controller.signal.aborted) setResultado({ clave, carga: { tipo: "listo", datos } });
      },
      (error: unknown) => {
        if (!controller.signal.aborted) setResultado({ clave, carga: { tipo: "error", error } });
      },
    );
    return () => controller.abort();
  }, [tipo, filtros, page, empresa, clave]);

  const catalogos = useCatalogosReportes(empresa);
  const elegida = useEmpresaBackoffice(empresa);

  const claveExportacion = JSON.stringify([tipo, filtros, empresa]);
  const descarga = useDescarga(tipo, filtros, empresa, claveExportacion);

  const error = carga.tipo === "error" ? carga.error : null;
  // El 403 EMPRESA_SOLO_SUPER_ADMIN (o su alias retirado) rechaza la petición, no el acceso: se pinta en línea con su literal.
  const codigo403 = error instanceof ApiError && error.status === 403 && !esRechazoDeEmpresa(error) ? error.code || "SIN_PERMISO" : null;
  useEffect(() => {
    if (codigo403) router.replace(`/403?code=${encodeURIComponent(codigo403)}`);
  }, [codigo403, router]);
  const errorRango = error instanceof ApiError && error.code === "RANGO_INVALIDO" ? mensajeRangoInvalido(filtros.rango, hoy) : null;
  const empresaNoEncontrada = error instanceof ApiError && error.code === "EMPRESA_NO_ENCONTRADA";

  function cambiarFiltros(cambios: Partial<Filtros>) {
    setFiltros((f) => ({ ...f, ...cambios }));
    setPage(1);
  }
  function limpiar() {
    setFiltros((f) => filtrosVacios(f.rango));
    setPage(1);
  }
  function elegirReporte(siguiente: TipoReporte) {
    if (siguiente === tipo) return;
    setTipo(siguiente);
    setPage(1);
    const q = new URLSearchParams(params?.toString() ?? "");
    q.set(PARAM_REPORTE, siguiente);
    q.delete("page");
    router.replace(`${pathname}?${q.toString()}`, { scroll: false });
  }

  const datos = carga.tipo === "listo" ? carga.datos : null;
  const total = datos ? datos.total : null;
  const conFiltros = hayFiltros(filtros, tipo);

  let cuerpo: ReactNode;
  if (carga.tipo === "cargando") cuerpo = <UiStateBoundary status="loading" skeletonRows={8}>{null}</UiStateBoundary>;
  else if (carga.tipo === "error")
    cuerpo =
      codigo403 || errorRango ? null : (
        <InlineAlert
          tone="error"
          action={
            empresaNoEncontrada && superAdmin ? (
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
          {mensajeErrorFijo(error, "No se pudo cargar el reporte", ERRORES_REPORTES)}
        </InlineAlert>
      );
  else if (datos && datos.items.length === 0)
    cuerpo = conFiltros ? (
      <EstadoVacio
        titulo="Ningún registro coincide con los filtros"
        acciones={[
          <Button key="limpiar" variant="secondary" size="sm" onClick={limpiar}>
            Limpiar filtros
          </Button>,
        ]}
      />
    ) : (
      <EstadoVacio
        titulo="No hay registros en este periodo"
        acciones={[
          <Button key="rango" variant="secondary" size="sm" onClick={() => filtrosRef.current?.querySelector<HTMLInputElement>('input[type="date"]')?.focus()}>
            Cambiar rango
          </Button>,
        ]}
      />
    );
  else if (datos) cuerpo = <TablaReporte reporte={reporte} pagina={datos} vistaGlobal={vistaGlobal} onPagina={setPage} />;

  const panel = (
    <div className="flex flex-col gap-4">
      <InlineAlert id={avisoId} tone={reporte.contienePii ? "warning" : "info"} vivo={false}>
        {reporte.contienePii ? AVISO_PII : AVISO_REGISTRO}
      </InlineAlert>
      <div ref={filtrosRef} className={TARJETA_CLS}>
        <FiltrosReporte
          key={tipo}
          reporte={reporte}
          filtros={filtros}
          hoy={hoy}
          departamentos={catalogos?.departamentos ?? null}
          usuarios={catalogos?.usuarios ?? null}
          errorRango={errorRango}
          onCambiar={cambiarFiltros}
          onLimpiar={limpiar}
        />
      </div>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <p aria-live="polite" className="m-0 text-sm font-semibold text-flit-primary">
          {total === null ? "—" : formatoEntero(total)} {total === 1 ? "registro" : "registros"}
        </p>
        {puedeExportar ? (
          <BotonesExportar
            total={total}
            cargando={carga.tipo === "cargando"}
            avisoId={avisoId}
            enVuelo={descarga.enVuelo}
            error={descarga.error}
            onExportar={descarga.exportar}
          />
        ) : null}
      </div>
      {cuerpo}
    </div>
  );

  const descripcion = superAdmin ? (empresa ? (elegida?.nombre ?? "La compañía elegida") : "Todas las compañías") : "Descarga la información de tus liquidaciones";

  return (
    <>
      <CabeceraModulo titulo="Reportes" icono="reportes" descripcion={descripcion} />
      <Pestanas
        ariaLabel="Reportes"
        active={tipo}
        onChange={elegirReporte}
        tabs={REPORTES.map((r) => ({ id: r.tipo, label: r.nombre, content: r.tipo === tipo ? panel : null }))}
      />
    </>
  );
}

/**
 * Opciones de «Departamento» y «Usuario» (GET /catalogos/reportes, delta 12), de la compañía elegida o, para el Super Admin
 * sin `empresa`, de todas. Con error, null: esos filtros no se pintan y el reporte sigue (la compañía rechazada ya la
 * explica el reporte).
 */
function useCatalogosReportes(empresa: string | null): CatalogosReportes | null {
  const [lista, setLista] = useState<{ empresa: string | null; catalogos: CatalogosReportes | null } | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    obtenerCatalogosReportes(empresa, controller.signal).then(
      (catalogos) => {
        if (!controller.signal.aborted) setLista({ empresa, catalogos });
      },
      () => {
        if (!controller.signal.aborted) setLista({ empresa, catalogos: null });
      },
    );
    return () => controller.abort();
  }, [empresa]);
  return lista?.empresa === empresa ? lista.catalogos : null;
}
