"use client";

import { useEffect, useId, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { BarraProgreso } from "@flit/ui/BarraProgreso";
import { Button } from "@flit/ui/Button";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Icono } from "@flit/ui/Icono";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import { Pestanas } from "@flit/ui/Pestanas";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import {
  AYUDA_CLS,
  BOTON_PRIMARIO_CLS,
  BOTON_REINTENTAR_CLS,
  BOTON_SECUNDARIO_CLS,
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CAMPO_CLS,
  CAMPO_FECHA_CLS,
  CELDA_TABLA_CLS,
  CONTENEDOR_TABLA_CLS,
  ETIQUETA_CLS,
  FILA_TABLA_CLS,
  FILA_TABLA_HOVER_CLS,
  FOCO_CLS,
  TARJETA_CLS,
} from "@/components/comun/estilos";
import { ChipEstado } from "@/components/liquidaciones/ChipEstado";
import { EligeCompania } from "@/components/liquidaciones/EligeCompania";
import { FormularioPlaca } from "@/components/liquidaciones/FormularioPlaca";
import { ApiError } from "@/lib/api";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { ERRORES_EMPRESA, esErrorDeEmpresa, mensajeErrorFijo } from "@/lib/errores";
import { descargarPlantilla, listarLiquidaciones, queryListado } from "@/lib/liquidaciones/api";
import { ERRORES_LIQUIDACIONES, ESTADOS_LIQUIDACION, PANTALLA, chipFila, nombreTipo, terminadas } from "@/lib/liquidaciones/estados";
import { formatoFecha, idCorto, normalizarPlaca } from "@/lib/liquidaciones/formato";
import type { EstadoLiquidacion, FiltrosLiquidaciones, Pagina, ResumenLiquidacion, TipoLiquidacion } from "@/lib/liquidaciones/tipos";

type Pestana = "todas" | "masivas" | "placa";
type Carga = { tipo: "cargando" } | { tipo: "error"; texto: string } | { tipo: "listo"; pagina: Pagina<ResumenLiquidacion> };

const TAMANO_POR_DEFECTO = 10;
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;
const IconoBuscar = iconoComo("buscar");
const IconoPortapapeles = iconoComo("portapapeles");

const TIPO_DE_PESTANA: Record<Pestana, TipoLiquidacion | null> = { todas: null, masivas: "MASIVA", placa: "INDIVIDUAL" };
const pestanaDe = (tipo: TipoLiquidacion | null): Pestana => (tipo === "MASIVA" ? "masivas" : tipo === "INDIVIDUAL" ? "placa" : "todas");

const entero = (v: string | null, porDefecto: number) => {
  const n = Number(v);
  return Number.isInteger(n) && n > 0 ? n : porDefecto;
};

function filtrosDesdeUrl(p: URLSearchParams): FiltrosLiquidaciones {
  const tipo = p.get("tipo");
  const estado = p.get("estado");
  return {
    tipo: tipo === "MASIVA" || tipo === "INDIVIDUAL" ? tipo : null,
    estado: estado && Object.prototype.hasOwnProperty.call(ESTADOS_LIQUIDACION, estado) ? (estado as EstadoLiquidacion) : null,
    desde: p.get("desde") ?? "",
    hasta: p.get("hasta") ?? "",
    placa: normalizarPlaca(p.get("placa") ?? ""),
    page: entero(p.get("page"), 1),
    pageSize: entero(p.get("pageSize"), TAMANO_POR_DEFECTO),
  };
}

const hayFiltros = (f: FiltrosLiquidaciones) => Boolean(f.estado || f.desde || f.hasta || f.placa);

/** URL de la pantalla con los filtros (sin la página 1 ni el tamaño por defecto) y la compañía elegida. */
function urlPantalla(f: FiltrosLiquidaciones, empresa: string | null): string {
  const q = queryListado({ ...f, page: f.page > 1 ? f.page : undefined, pageSize: f.pageSize !== TAMANO_POR_DEFECTO ? f.pageSize : undefined });
  return hrefConEmpresa(q ? `/liquidaciones?${q}` : "/liquidaciones", empresa);
}

export interface GridLiquidacionesProps {
  /** impuestos.liquidaciones.crear: tarjetas «Liquidar placa» y «Carga masiva». */
  puedeCrear: boolean;
  /** Super Admin en vista global: columna «Compañía». */
  mostrarCompania: boolean;
  /** Literal del contrato por el que no se puede crear (suspendido / no listo); null = se puede. */
  bloqueo: string | null;
  /**
   * Super Admin: el grid y la creación son de la compañía de ?empresa (regla del backend 1.1.0). Sin compañía elegida no
   * se pide nada: «Elige una compañía.» en la tabla y las acciones de creación deshabilitadas con ese motivo.
   */
  esSuperAdmin?: boolean;
}

/**
 * Grid de liquidaciones (spec-visual §3.2, F3-CF6, D-UI-PESTANAS). Tarjetas de creación (solo con permiso), pestañas
 * «Todas» (por defecto, con columna Tipo) / «Masivas» / «Por placa», filtros en la URL (cada cambio vuelve a la página 1)
 * y la tabla paginada. Cuatro estados: cargando (las tarjetas siguen usables), error con Reintentar, vacío sin datos y
 * vacío por filtro, y lleno. La compañía elegida por el Super Admin (?empresa=) viaja a la API y a cada enlace.
 */
export function GridLiquidaciones({ puedeCrear, mostrarCompania, bloqueo: bloqueoSesion, esSuperAdmin = false }: GridLiquidacionesProps) {
  const router = useRouter();
  const params = useSearchParams();
  const { empresa } = useEmpresaSeleccionada();
  const faltaEmpresa = esSuperAdmin && !empresa;
  const bloqueo = faltaEmpresa ? ERRORES_EMPRESA.EMPRESA_REQUERIDA : bloqueoSesion;
  const motivoId = useId();
  const clave = params.toString();
  const [claveVista, setClaveVista] = useState(clave);
  const [filtros, setFiltros] = useState<FiltrosLiquidaciones>(() => filtrosDesdeUrl(params));
  const [placaTexto, setPlacaTexto] = useState(filtros.placa);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [errorPlantilla, setErrorPlantilla] = useState<string | null>(null);
  const [acceso, setAcceso] = useState<string | null>(null);

  // La URL cambió desde fuera (atrás/adelante, enlace compartido, otra compañía): los filtros la siguen.
  if (clave !== claveVista) {
    setClaveVista(clave);
    const deUrl = filtrosDesdeUrl(new URLSearchParams(clave));
    if (queryListado(deUrl) !== queryListado(filtros)) {
      setFiltros(deUrl);
      setPlacaTexto(deUrl.placa);
      setCarga({ tipo: "cargando" });
    }
  }

  useEffect(() => {
    if (faltaEmpresa) return;
    const control = new AbortController();
    listarLiquidaciones(filtros, { empresa, signal: control.signal }).then(
      (pagina) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", pagina });
      },
      (e: unknown) => {
        if (control.signal.aborted) return;
        // Un error de compañía (403 EMPRESA_SOLO_SUPER_ADMIN, 400 EMPRESA_REQUERIDA, 404 EMPRESA_NO_ENCONTRADA) rechaza la
        // petición, no el acceso: va en línea con su literal.
        if (e instanceof ApiError && e.status === 403 && e.code && !esErrorDeEmpresa(e)) setAcceso(e.code);
        setCarga({ tipo: "error", texto: mensajeErrorFijo(e, "Revisa tu conexión e intenta de nuevo.", ERRORES_LIQUIDACIONES) });
      },
    );
    return () => control.abort();
  }, [filtros, empresa, intento, faltaEmpresa]);

  useEffect(() => {
    if (acceso) router.replace(`/403?code=${encodeURIComponent(acceso)}`);
  }, [acceso, router]);

  function aplicar(cambios: Partial<FiltrosLiquidaciones>) {
    const siguiente: FiltrosLiquidaciones = { ...filtros, ...cambios, page: cambios.page ?? 1 };
    if (queryListado(siguiente) === queryListado(filtros)) return;
    setFiltros(siguiente);
    setCarga({ tipo: "cargando" });
    router.replace(urlPantalla(siguiente, empresa), { scroll: false });
  }

  const limpiar = () => {
    setPlacaTexto("");
    aplicar({ estado: null, desde: "", hasta: "", placa: "" });
  };
  const reintentar = () => {
    setCarga({ tipo: "cargando" });
    setIntento((n) => n + 1);
  };
  const conFiltros = hayFiltros(filtros);
  const columnaTipo = filtros.tipo === null;

  let contenido: ReactNode;
  if (faltaEmpresa) {
    contenido = <EligeCompania nivelTitulo={3} enTarjeta={false} />;
  } else if (carga.tipo === "cargando" || acceso) {
    contenido = <VehicleLoader label="Cargando liquidaciones…" />;
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudieron cargar las liquidaciones"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {carga.texto}
      </InlineAlert>
    );
  } else if (carga.pagina.items.length === 0 && conFiltros) {
    contenido = (
      <EstadoVacio
        nivelTitulo={3}
        icono={<IconoBuscar className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Ninguna liquidación coincide con los filtros"
        mensaje="Prueba con otro rango o quita algún filtro."
        acciones={[
          <button key="limpiar" type="button" onClick={limpiar} className={BOTON_SECUNDARIO_CLS}>
            Limpiar filtros
          </button>,
        ]}
      />
    );
  } else if (carga.pagina.items.length === 0) {
    contenido = (
      <EstadoVacio
        nivelTitulo={3}
        icono={<IconoPortapapeles className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Aún no hay liquidaciones"
        mensaje="Liquida una placa o sube un archivo para empezar."
        acciones={
          puedeCrear && !bloqueo
            ? [
                <Link key="placa" href={hrefConEmpresa("/liquidaciones/nueva", empresa)} className={BOTON_PRIMARIO_CLS}>
                  Liquidar por placa
                </Link>,
              ]
            : undefined
        }
      />
    );
  } else {
    const { pagina } = carga;
    const columnas = ["ID", ...(columnaTipo ? ["Tipo"] : []), "Creada", "Placas", "Avance", "Estado", "Creada por", ...(mostrarCompania ? ["Compañía"] : [])];
    contenido = (
      <div className="flex flex-col gap-3">
        <div className={CONTENEDOR_TABLA_CLS}>
          <table aria-label="Liquidaciones" className={`w-full min-w-[880px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
            <thead className={TABLA_MOVIL_THEAD_CLS}>
              <tr>
                {columnas.map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className={TABLA_MOVIL_TBODY_CLS}>
              {pagina.items.map((l) => {
                // La individual va directo al detalle de su única placa (itemId, contrato 1.1.0); sin él (respuesta vieja),
                // a la página del lote, que redirige.
                const ruta =
                  l.tipo === "INDIVIDUAL" && l.itemId
                    ? `/liquidaciones/${encodeURIComponent(l.id)}/items/${encodeURIComponent(l.itemId)}`
                    : `/liquidaciones/${encodeURIComponent(l.id)}`;
                const destino = hrefConEmpresa(ruta, empresa);
                const hechas = terminadas(l);
                return (
                  <tr
                    key={l.id}
                    onClick={(e) => {
                      if ((e.target as HTMLElement).closest("a")) return;
                      router.push(destino);
                    }}
                    className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} cursor-pointer ${TABLA_MOVIL_ROW_CLS}`}
                  >
                    <td data-label="ID" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} font-mono tabular-nums`}>
                      <Link href={destino} title={l.id} className={`text-sm font-semibold text-[var(--flit-brand-ink)] underline-offset-4 hover:underline ${FOCO_CLS}`}>
                        {idCorto(l.id)}
                        {l.tipo === "INDIVIDUAL" && l.placa ? <span className="ml-2 font-sans uppercase tracking-[0.08em]">{l.placa}</span> : null}
                      </Link>
                    </td>
                    {columnaTipo ? (
                      <td data-label="Tipo" className={CELDA_CLS}>
                        {nombreTipo(l.tipo)}
                      </td>
                    ) : null}
                    <td data-label="Creada" className={`${CELDA_CLS} whitespace-nowrap tabular-nums`}>
                      <time dateTime={l.creadaEn}>{formatoFecha(l.creadaEn)}</time>
                    </td>
                    <td data-label="Placas" className={`${CELDA_CLS} tabular-nums`}>
                      {l.totalItems}
                    </td>
                    <td data-label="Avance" className={CELDA_CLS}>
                      <div className="flex min-w-28 items-center gap-2">
                        <BarraProgreso
                          etiqueta={`Avance de la liquidación ${idCorto(l.id)}`}
                          valor={hechas}
                          maximo={l.totalItems}
                          textoValor={`${hechas} de ${l.totalItems} placas terminadas`}
                        />
                        <span className="whitespace-nowrap tabular-nums">{`${hechas}/${l.totalItems}`}</span>
                      </div>
                    </td>
                    <td data-label="Estado" className={CELDA_CLS}>
                      <ChipEstado chip={chipFila(l)} />
                    </td>
                    <td data-label="Creada por" className={CELDA_CLS}>
                      {l.creadaPor?.nombre}
                    </td>
                    {mostrarCompania ? (
                      <td data-label="Compañía" className={CELDA_CLS}>
                        {l.empresa?.nombre}
                      </td>
                    ) : null}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <Pagination
          page={pagina.page}
          pageSize={pagina.pageSize}
          totalCount={pagina.total}
          onPageChange={(page) => aplicar({ page })}
          onPageSizeChange={(pageSize) => aplicar({ pageSize, page: 1 })}
          ariaLabel="Paginación de liquidaciones"
          noun="liquidaciones"
        />
      </div>
    );
  }

  const activa = pestanaDe(filtros.tipo);
  const listado = (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <label htmlFor={`${motivoId}-estado`} className={ETIQUETA_CLS}>
            Estado
          </label>
          <select
            id={`${motivoId}-estado`}
            value={filtros.estado ?? ""}
            onChange={(e) => aplicar({ estado: (e.target.value || null) as EstadoLiquidacion | null })}
            className={`${CAMPO_CLS} w-56`}
          >
            <option value="">Todos los estados</option>
            {Object.entries(ESTADOS_LIQUIDACION).map(([valor, texto]) => (
              <option key={valor} value={valor}>
                {texto}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor={`${motivoId}-desde`} className={ETIQUETA_CLS}>
            Desde
          </label>
          <input id={`${motivoId}-desde`} type="date" value={filtros.desde} onChange={(e) => aplicar({ desde: e.target.value })} className={`${CAMPO_FECHA_CLS} w-44`} />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor={`${motivoId}-hasta`} className={ETIQUETA_CLS}>
            Hasta
          </label>
          <input id={`${motivoId}-hasta`} type="date" value={filtros.hasta} onChange={(e) => aplicar({ hasta: e.target.value })} className={`${CAMPO_FECHA_CLS} w-44`} />
        </div>
        <form
          role="search"
          aria-label="Buscar placa"
          className="flex flex-col gap-1"
          onSubmit={(e) => {
            e.preventDefault();
            aplicar({ placa: normalizarPlaca(placaTexto) });
          }}
        >
          <label htmlFor={`${motivoId}-buscar`} className={ETIQUETA_CLS}>
            Buscar placa
          </label>
          <input
            id={`${motivoId}-buscar`}
            type="search"
            maxLength={10}
            autoComplete="off"
            value={placaTexto}
            onChange={(e) => setPlacaTexto(e.target.value.toUpperCase())}
            onBlur={() => aplicar({ placa: normalizarPlaca(placaTexto) })}
            className={`${CAMPO_CLS} w-44 uppercase tracking-[0.08em]`}
          />
        </form>
        {conFiltros ? (
          <Button variant="ghost" size="sm" onClick={limpiar} className="ml-auto">
            Limpiar filtros
          </Button>
        ) : null}
      </div>
      <div>{contenido}</div>
    </div>
  );

  return (
    <div className="flex flex-col gap-4">
      {puedeCrear ? (
        <>
          {bloqueo ? (
            <p id={motivoId} className={AYUDA_CLS}>
              {bloqueo}
            </p>
          ) : null}
          <div className="grid gap-4 lg:grid-cols-2">
            <section aria-labelledby={`${motivoId}-t1`} className={`${TARJETA_CLS} flex flex-col gap-2`}>
              <h2 id={`${motivoId}-t1`} className="text-lg font-semibold text-flit-primary">
                Liquidar por placa
              </h2>
              <p className={AYUDA_CLS}>Una placa, sin crear un lote.</p>
              <FormularioPlaca empresa={empresa} bloqueo={bloqueo} esSuperAdmin={esSuperAdmin} motivoId={motivoId} />
            </section>
            <section aria-labelledby={`${motivoId}-t2`} className={`${TARJETA_CLS} flex flex-col gap-2`}>
              <h2 id={`${motivoId}-t2`} className="text-lg font-semibold text-flit-primary">
                Liquidación masiva
              </h2>
              <p className={AYUDA_CLS}>Muchas placas desde un archivo.</p>
              <div className="flex flex-wrap gap-2">
                {bloqueo ? (
                  <button type="button" disabled aria-describedby={motivoId} className={`${BOTON_PRIMARIO_CLS} disabled:opacity-60`}>
                    <Icono nombre="reporte-detallado" size={16} />
                    {PANTALLA.accionCargaMasiva}
                  </button>
                ) : (
                  <Link href={hrefConEmpresa("/liquidaciones/masiva", empresa)} className={BOTON_PRIMARIO_CLS}>
                    <Icono nombre="reporte-detallado" size={16} />
                    {PANTALLA.accionCargaMasiva}
                  </Link>
                )}
                <button
                  type="button"
                  className={`${BOTON_SECUNDARIO_CLS} disabled:opacity-60`}
                  // La plantilla del Super Admin también es de una compañía (?empresa, delta 2).
                  disabled={faltaEmpresa}
                  aria-describedby={faltaEmpresa ? motivoId : undefined}
                  onClick={() => {
                    setErrorPlantilla(null);
                    descargarPlantilla(empresa).catch((e: unknown) => setErrorPlantilla(mensajeErrorFijo(e, "No se descargó la plantilla. Intenta de nuevo.", ERRORES_LIQUIDACIONES)));
                  }}
                >
                  <Icono nombre="nube-bajar" size={16} />
                  {PANTALLA.accionDescargarPlantilla}
                </button>
              </div>
              {errorPlantilla ? (
                <p role="alert" className="text-xs font-medium text-[var(--badge-danger-fg)]">
                  {errorPlantilla}
                </p>
              ) : null}
            </section>
          </div>
        </>
      ) : null}
      <Pestanas<Pestana>
        ariaLabel="Tipo de liquidación"
        active={activa}
        onChange={(p) => aplicar({ tipo: TIPO_DE_PESTANA[p] })}
        tabs={[
          { id: "todas", label: "Todas", content: activa === "todas" ? listado : null },
          { id: "masivas", label: "Masivas", content: activa === "masivas" ? listado : null },
          { id: "placa", label: "Por placa", content: activa === "placa" ? listado : null },
        ]}
      />
    </div>
  );
}

