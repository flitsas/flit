"use client";

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { SECCION_CLS } from "@/components/estadisticas/graficas/Figura";
import { ApiError } from "@/lib/api";
import { mensajeErrorFijo } from "@/lib/errores";
import { formatoEntero } from "@/lib/estadisticas/formato";
import { useEmpresasBackoffice } from "@/components/estadisticas/SelectorCompania";
import { hoyColombia, mensajeRangoInvalido } from "@/lib/estadisticas/periodo";
import { ERRORES_REPORTE, ambitoReporte, descargarReporte, type ConsultaReporte } from "@/lib/reportes/api";
import { TAMANO_REPORTE, filtrosPorDefecto, hayFiltrosPropios, leerFiltrosDeUrl, urlPantalla } from "@/lib/reportes/catalogo";
import { ordenarCatalogo } from "@/lib/reportes/formato";
import type { FiltrosReporteValor, ReporteCatalogo, TipoReporte } from "@/lib/reportes/tipos";
import { BotonesExportar } from "./BotonesExportar";
import { FiltrosReporte } from "./FiltrosReporte";
import { SelectorReporte } from "./SelectorReporte";
import { TablaReporte } from "./TablaReporte";
import { useCatalogoReportes, useReporte } from "./useReporte";

const AVISO_ID = "reportes-aviso-datos-personales";
const LEY = "protegidos por la Ley 1581 de 2012. Úsalos solo para la finalidad autorizada. Cada consulta y descarga queda registrada con tu usuario.";

/**
 * Aviso persistente de datos personales (spec-ui §5.1.1 con Q1/O10): el nombre del propietario no sale, así que el texto
 * de PII alta habla de números de documento, no de nombres. Sin PII (consumo): solo el registro de descargas.
 */
function AvisoDatosPersonales({ reporte }: { reporte: ReporteCatalogo | undefined }) {
  if (reporte?.tipo === "consumo")
    return (
      <InlineAlert id={AVISO_ID} tone="info" vivo={false}>
        Cada descarga queda registrada con tu usuario.
      </InlineAlert>
    );
  return (
    <InlineAlert id={AVISO_ID} tone="warning" vivo={false}>
      {reporte?.contiene_pii
        ? `Este reporte contiene datos personales (números de documento de identidad) ${LEY}`
        : `Este reporte contiene datos personales ${LEY}`}
    </InlineAlert>
  );
}

interface Seleccion {
  tipo: TipoReporte;
  valor: FiltrosReporteValor;
  pagina: number;
  tamano: number;
}

/**
 * Módulo de reportes (HU #14580 bloque 6, spec-ui §5). Con `vistaGlobal` (capacidad verEstadisticasGlobales, Q4) usa el
 * catálogo y las rutas del backoffice («Todas las compañías» → /global, o ?empresa=) y ofrece «Consumo por compañía»; sin
 * ella, el de la compañía. Una pestaña por reporte del catálogo (la activa en ?reporte=), sus filtros, la tabla paginada
 * y, con `puedeDescargar`, Excel y PDF del filtro completo. La URL lleva reporte, desde, hasta, empresa y enums, nunca la
 * placa; nada va al almacenamiento del navegador (SP-18). Cuatro estados en el catálogo y en el reporte.
 */
export function ModuloReportes({ vistaGlobal, puedeDescargar }: { vistaGlobal: boolean; puedeDescargar: boolean }) {
  const router = useRouter();
  const pathname = usePathname() || "/reportes";
  const params = useSearchParams();
  const hoy = useMemo(() => hoyColombia(), []);
  const catalogo = useCatalogoReportes(vistaGlobal);
  const reportes = useMemo(() => ordenarCatalogo(catalogo.datos?.reportes ?? []), [catalogo.datos]);
  const filtrosRef = useRef<HTMLDivElement>(null);
  // Una sola petición de compañías por montaje: el panel de filtros se remonta en cada cambio de pestaña (CR-FE-11).
  const empresas = useEmpresasBackoffice(vistaGlobal);

  const [sel, setSel] = useState<Seleccion | null>(null);
  // Con el catálogo listo, la primera selección sale de la URL (?reporte= y sus filtros) o del primer reporte.
  if (sel === null && reportes.length > 0) {
    const deUrl = reportes.find((r) => r.tipo === params.get("reporte")) ?? reportes[0];
    setSel({ tipo: deUrl.tipo, valor: leerFiltrosDeUrl(params, hoy, deUrl, vistaGlobal), pagina: 1, tamano: TAMANO_REPORTE });
  }

  const activo = reportes.find((r) => r.tipo === sel?.tipo);
  const valor = sel?.valor ?? filtrosPorDefecto(hoy);
  const ambito = ambitoReporte(vistaGlobal, valor.empresa);
  const consulta: ConsultaReporte = {
    desde: valor.rango.desde,
    hasta: valor.rango.hasta,
    filtros: valor.filtros,
    placa: valor.placa,
    pagina: sel?.pagina ?? 1,
    tamano: sel?.tamano ?? TAMANO_REPORTE,
  };
  const reporte = useReporte(ambito, activo ? activo.tipo : null, consulta);

  const error403 =
    (catalogo.estado === "error" && catalogo.error instanceof ApiError && catalogo.error.status === 403 && catalogo.error) ||
    (reporte.estado === "error" && reporte.error instanceof ApiError && reporte.error.status === 403 && reporte.error) ||
    null;
  const codigo403 = error403 ? error403.code || "SIN_PERMISO" : null;
  useEffect(() => {
    if (codigo403) router.replace(`/403?code=${encodeURIComponent(codigo403)}`);
  }, [codigo403, router]);

  // spec-ui §5 criterio 4: la pestaña activa está en la URL también cuando sale por defecto (primer reporte del catálogo).
  // Se escribe una sola vez, y solo si la URL no la trae ya: replace no relanza la lectura inicial, así que no hay bucle.
  const urlInicialFijada = useRef(false);
  const reporteEnUrl = params.get("reporte");
  useEffect(() => {
    if (!sel || urlInicialFijada.current) return;
    urlInicialFijada.current = true;
    if (reporteEnUrl !== sel.tipo) router.replace(urlPantalla(pathname, sel.tipo, sel.valor, vistaGlobal), { scroll: false });
  }, [sel, reporteEnUrl, pathname, router, vistaGlobal]);

  function seleccionar(siguiente: Seleccion) {
    setSel(siguiente);
    router.replace(urlPantalla(pathname, siguiente.tipo, siguiente.valor, vistaGlobal), { scroll: false });
  }

  const cabecera = (
    <CabeceraModulo
      as="section"
      aria-labelledby="reportes-titulo"
      tituloId="reportes-titulo"
      titulo="Reportes"
      icono="reportes"
      descripcion="Descarga la información detallada de tus diagnósticos"
    />
  );

  if (catalogo.estado === "cargando")
    return (
      <>
        {cabecera}
        <VehicleLoader label="Cargando reportes…" />
      </>
    );
  if (catalogo.estado === "error")
    return (
      <>
        {cabecera}
        {codigo403 ? null : (
          <InlineAlert
            tone="error"
            action={
              <Button variant="secondary" size="sm" onClick={catalogo.reintentar}>
                Reintentar
              </Button>
            }
          >
            {mensajeErrorFijo(catalogo.error, "No se pudo cargar el catálogo de reportes.", ERRORES_REPORTE)}
          </InlineAlert>
        )}
      </>
    );
  if (reportes.length === 0 || !sel || !activo)
    return (
      <>
        {cabecera}
        <section className={SECCION_CLS}>
          <EstadoVacio titulo="No hay reportes disponibles" mensaje="Tu usuario no tiene reportes habilitados en Diagnóstico." />
        </section>
      </>
    );

  const errorRango =
    reporte.estado === "error" && reporte.error instanceof ApiError && reporte.error.status === 400 && reporte.error.code === "RANGO_INVALIDO"
      ? mensajeRangoInvalido(valor.rango, hoy)
      : null;
  // 404 de un ?empresa= viejo o de una compañía apagada (contrato §3): reintentar no sirve; se vuelve a «Todas».
  const empresaNoEncontrada = reporte.estado === "error" && reporte.error instanceof ApiError && reporte.error.code === "EMPRESA_NO_ENCONTRADA";
  // Lo que exporta el botón (sin página ni tamaño, que la exportación no usa): si cambia, se borra su último error (CR-FE-5).
  const claveExportacion = JSON.stringify([activo.tipo, valor.rango, valor.empresa, valor.filtros, valor.placa]);
  const pagina = reporte.estado === "listo" ? reporte.datos : null;
  const total = pagina?.total ?? null;

  let resultados: ReactNode;
  if (reporte.estado === "error") {
    resultados =
      codigo403 || errorRango ? null : (
        <InlineAlert
          tone="error"
          action={
            empresaNoEncontrada ? (
              <Button variant="secondary" size="sm" onClick={() => seleccionar({ ...sel, valor: { ...valor, empresa: "" }, pagina: 1 })}>
                Ver todas las compañías
              </Button>
            ) : (
              <Button variant="secondary" size="sm" onClick={reporte.reintentar}>
                Reintentar
              </Button>
            )
          }
        >
          {mensajeErrorFijo(reporte.error, "No se pudo cargar el reporte.", ERRORES_REPORTE)}
        </InlineAlert>
      );
  } else if (pagina && pagina.total === 0) {
    resultados = hayFiltrosPropios(valor) ? (
      <EstadoVacio
        nivelTitulo={3}
        titulo="Ningún registro coincide con los filtros"
        acciones={[
          <Button key="limpiar" variant="secondary" size="sm" onClick={() => seleccionar({ ...sel, valor: { ...filtrosPorDefecto(hoy), rango: valor.rango, empresa: valor.empresa }, pagina: 1 })}>
            Limpiar filtros
          </Button>,
        ]}
      />
    ) : (
      <EstadoVacio
        nivelTitulo={3}
        titulo="No hay registros en este periodo"
        acciones={[
          <Button key="rango" variant="secondary" size="sm" onClick={() => filtrosRef.current?.querySelector<HTMLInputElement>('input[type="date"]')?.focus()}>
            Cambiar rango
          </Button>,
        ]}
      />
    );
  } else {
    resultados = (
      <TablaReporte
        tipo={activo.tipo}
        pagina={pagina}
        cargando={reporte.estado === "cargando"}
        tamano={sel.tamano}
        onPagina={(n) => setSel({ ...sel, pagina: n })}
        onTamano={(n) => setSel({ ...sel, tamano: n, pagina: 1 })}
      />
    );
  }

  const panel = (
    <div className="flex flex-col gap-4">
      <section aria-label={`Filtros de ${activo.nombre}`} className={SECCION_CLS} ref={filtrosRef}>
        <FiltrosReporte
          reporte={activo}
          valor={valor}
          hoy={hoy}
          vistaGlobal={vistaGlobal}
          errorRango={errorRango}
          empresas={vistaGlobal ? empresas : undefined}
          onCambiar={(v) => seleccionar({ ...sel, valor: v, pagina: 1 })}
        />
      </section>
      <section aria-labelledby="reportes-resultados" className={`${SECCION_CLS} min-w-0`}>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <h2 id="reportes-resultados" className="text-base font-semibold text-flit-primary" aria-live="polite">
            {total === null ? "— registros" : total === 1 ? "1 registro" : `${formatoEntero(total)} registros`}
          </h2>
          {puedeDescargar ? (
            <BotonesExportar
              reporte={activo}
              total={total}
              cargando={reporte.estado === "cargando"}
              avisoId={AVISO_ID}
              claveConsulta={claveExportacion}
              onDescargar={(formato) => descargarReporte(ambito, activo.tipo, consulta, formato)}
            />
          ) : null}
        </div>
        {resultados}
      </section>
    </div>
  );

  return (
    <>
      {cabecera}
      {/* Ronda 2 de E2E (axe color-contrast): el fondo warning del kit es translúcido; sobre el lienzo azulado el texto
          bajaba a 4,31:1. Base opaca de tarjeta con el mismo radio del InlineAlert (9 px). */}
      <div className="rounded-[9px] bg-[var(--di-surface-elevated)]">
        <AvisoDatosPersonales reporte={activo} />
      </div>
      <SelectorReporte
        reportes={reportes}
        activo={activo.tipo}
        onCambiar={(tipo) => seleccionar({ tipo, valor: { ...valor, filtros: {}, placa: "" }, pagina: 1, tamano: sel.tamano })}
        contenido={panel}
      />
    </>
  );
}
