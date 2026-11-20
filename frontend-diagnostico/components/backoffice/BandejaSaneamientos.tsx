"use client";

import { useEffect, useId, useRef, useState, type FormEvent, type MouseEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { BarraSeleccion } from "@flit/ui/BarraSeleccion";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { ChipFiltro, CHIPS_FILTRO_FILA_CLS } from "@flit/ui/ChipsFiltro";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { SIN_PERMISO_SANEAMIENTO } from "@/components/backoffice/AccesoSaneamientos";
import { SelectorEmpresaBackoffice } from "@/components/backoffice/SelectorEmpresaBackoffice";
import {
  AYUDA_CLS,
  BOTON_REINTENTAR_CLS,
  BOTON_SECUNDARIO_CLS,
  CAMPO_CLS,
  CAMPO_FECHA_CLS,
  ENLACE_ACCION_CLS,
  ETIQUETA_CLS,
  FOCO_CLS,
  MARCA_CLS,
  TARJETA_CLS,
} from "@/components/configuracion/estilos";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";
import { useRegionVivaPantalla } from "@/components/saneamiento/RegionVivaPantalla";
import { EtiquetaTono } from "@/components/saneamiento/SeccionesDeuda";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, CONTENEDOR_TABLA_CLS, FILA_TABLA_CLS, FILA_TABLA_HOVER_CLS } from "@/components/tablero/vistasDetalle/comun";
import { ApiError } from "@/lib/api";
import {
  ESTADOS_BANDEJA_POR_DEFECTO,
  MAXIMO_TOMAR,
  TAMANO_BANDEJA_POR_DEFECTO,
  hayFiltrosBandeja,
  listarBandeja,
  tomarSaneamientos,
  urlBandeja,
  urlExcelBandeja,
  type FilaBandeja,
  type FiltrosBandeja,
  type ListaBandeja,
  type ResultadoTomar,
} from "@/lib/backoffice/saneamientos";
import { rotuloEmpresa, useNombresEmpresas } from "@/lib/backoffice/useNombresEmpresas";
import { useResumenSaneamientos } from "@/lib/backoffice/useResumenSaneamientos";
import { descargarArchivo } from "@/lib/reportes/api";
import { mensajeErrorSaneamiento, type EstadoSaneamiento } from "@/lib/saneamiento/api";
import { ESTADOS_SANEAMIENTO, etiquetaSaneamiento, laPlaca } from "@/lib/saneamiento/estados";

// PS-4, bandeja «Saneamientos» del backoffice (spec-ui §5; plan §7.5; HU #14577 AC26–AC28). Filtros en la URL (estado
// multiselección, compañía, placa y fechas de solicitud), tabla paginada en el servidor que pasa a tarjetas por debajo de
// 1024 px, «Tomar (N)» sin confirmación (D-07) para las solicitudes sin tomar y «Descargar Excel» con los mismos filtros.
// Cuatro estados del patrón de Diagnóstico (D-01). Todo valor visual sale de tokens del kit; sin hex.

export interface BandejaSaneamientosProps {
  /** Filtros de la URL (`filtrosBandejaDesdeUrl`). */
  filtros: FiltrosBandeja;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; lista: ListaBandeja };
type Aviso = { tono: "success" | "warning"; texto: string };

const ERROR_DESCARGA = "No pudimos generar el archivo. Intenta de nuevo.";
const MOTIVO_NO_SELECCIONABLE = "Solo se toman las solicitudes sin tomar";
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;
const COLUMNAS = ["Placa", "Compañía", "Diagnóstico", "Modo", "Dictamen", "Estado", "Solicitado", "Tomado", "Cerrado", "Pendientes"] as const;

const plural = (n: number, singular: string, varios: string) => (n === 1 ? singular : varios);

/** «Se tomaron X saneamientos. Y no se pudieron tomar porque …» (spec-ui §5.3; D-62: el motivo se explica por código). */
export function textoResultadoTomar(r: ResultadoTomar): Aviso {
  const partes = [
    r.tomados === 0 ? "No se tomó ningún saneamiento." : `${plural(r.tomados, "Se tomó", "Se tomaron")} ${r.tomados} ${plural(r.tomados, "saneamiento", "saneamientos")}.`,
  ];
  const cambiaron = r.omitidos.filter((o) => o.motivo !== "NO_ENCONTRADO").length;
  const noExisten = r.omitidos.length - cambiaron;
  if (cambiaron > 0) partes.push(`${cambiaron} ${plural(cambiaron, "no se pudo tomar porque cambió de estado.", "no se pudieron tomar porque cambiaron de estado.")}`);
  if (noExisten > 0) partes.push(`${noExisten} ${plural(noExisten, "no se pudo tomar porque ya no existe.", "no se pudieron tomar porque ya no existen.")}`);
  return { tono: r.omitidos.length > 0 || r.tomados === 0 ? "warning" : "success", texto: partes.join(" ") };
}

const textoModo = (modo: FilaBandeja["modo"]) => (modo === "CON_ANALISIS" ? "Con análisis IA" : "Simple");
const fecha = (iso: string | null) => (iso ? fechaDelDato(iso) : "—");
const irA = (f: FilaBandeja) => `/backoffice/saneamientos/${encodeURIComponent(f.id)}?empresa=${encodeURIComponent(f.empresa)}`;

export function BandejaSaneamientos({ filtros: iniciales }: BandejaSaneamientosProps) {
  const router = useRouter();
  const id = useId();
  const nombres = useNombresEmpresas();
  const resumen = useResumenSaneamientos();
  // Región viva única de la pantalla (spec-ui §1.6). El resultado de «Tomar» no va aquí: el foco va al aviso (UA-4).
  const { region } = useRegionVivaPantalla();
  const [filtros, setFiltros] = useState<FiltrosBandeja>(iniciales);
  const [placa, setPlaca] = useState(iniciales.placa);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [seleccion, setSeleccion] = useState<ReadonlySet<string>>(() => new Set());
  const [tomando, setTomando] = useState(false);
  const [aviso, setAviso] = useState<Aviso | null>(null);
  const [errorTomar, setErrorTomar] = useState<string | null>(null);
  const [descargando, setDescargando] = useState(false);
  const [errorExcel, setErrorExcel] = useState<string | null>(null);
  const avisoRef = useRef<HTMLDivElement>(null);
  const enfocarAviso = useRef(false);

  useEffect(() => {
    const control = new AbortController();
    listarBandeja(filtros, control.signal).then(
      (lista) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", lista });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [filtros, intento]);

  const es403 = carga.tipo === "error" && carga.error instanceof ApiError && carga.error.status === 403;
  useEffect(() => {
    if (es403) router.replace(`/403?code=${SIN_PERMISO_SANEAMIENTO}`);
  }, [es403, router]);

  // Tras «Tomar», el foco va al aviso con el resultado (spec-ui §5.3).
  useEffect(() => {
    if (aviso && enfocarAviso.current) {
      enfocarAviso.current = false;
      avisoRef.current?.focus();
    }
  }, [aviso]);

  function aplicar(cambios: Partial<FiltrosBandeja>) {
    const siguiente = { ...filtros, pagina: 1, ...cambios };
    setFiltros(siguiente);
    setCarga({ tipo: "cargando" });
    setSeleccion(new Set());
    setAviso(null);
    setErrorExcel(null);
    router.replace(urlBandeja(siguiente), { scroll: false });
  }

  function alternarEstado(estado: EstadoSaneamiento) {
    const activos = filtros.estados.includes(estado) ? filtros.estados.filter((e) => e !== estado) : [...filtros.estados, estado];
    aplicar({ estados: ESTADOS_SANEAMIENTO.filter((e) => activos.includes(e)) });
  }

  function limpiar() {
    setPlaca("");
    aplicar({ estados: [...ESTADOS_BANDEJA_POR_DEFECTO], empresa: "", placa: "", desde: "", hasta: "", tamano: TAMANO_BANDEJA_POR_DEFECTO });
  }

  function aplicarPlaca() {
    if (placa.trim() !== filtros.placa.trim()) aplicar({ placa: placa.trim() });
  }

  function alEnviar(e: FormEvent) {
    e.preventDefault();
    aplicarPlaca();
  }

  const filas = carga.tipo === "listo" ? carga.lista.saneamientos : [];
  const seleccionables = filas.filter((f) => f.estado === "SOLICITADO");
  const elegidas = seleccionables.filter((f) => seleccion.has(f.id));

  function alternar(fila: FilaBandeja) {
    setSeleccion((previa) => {
      const s = new Set(previa);
      if (s.has(fila.id)) s.delete(fila.id);
      else s.add(fila.id);
      return s;
    });
  }

  async function tomar() {
    if (tomando || elegidas.length === 0) return;
    setTomando(true);
    setErrorTomar(null);
    try {
      const r = await tomarSaneamientos(elegidas.slice(0, MAXIMO_TOMAR).map((f) => ({ empresa: f.empresa, id: f.id })));
      const resultado = textoResultadoTomar(r);
      // UA-4: un solo canal. El foco va al aviso (que no es región viva) y ese foco lo lee; no se anuncia aparte.
      enfocarAviso.current = true;
      setAviso(resultado);
      setSeleccion(new Set());
      setIntento((n) => n + 1);
      resumen.refrescar();
    } catch (e) {
      if (e instanceof ApiError && e.status === 403) router.replace(`/403?code=${SIN_PERMISO_SANEAMIENTO}`);
      else setErrorTomar(mensajeErrorSaneamiento(e, "No pudimos tomar los saneamientos. Intenta de nuevo."));
    } finally {
      setTomando(false);
    }
  }

  function descargarExcel(e: MouseEvent<HTMLAnchorElement>) {
    e.preventDefault();
    if (descargando) return;
    const url = e.currentTarget.getAttribute("href");
    if (!url) return;
    setDescargando(true);
    setErrorExcel(null);
    descargarArchivo(url, "saneamientos.xlsx")
      .catch((err: unknown) => setErrorExcel(mensajeErrorSaneamiento(err, ERROR_DESCARGA)))
      .finally(() => setDescargando(false));
  }

  let contenido: React.ReactNode;
  if (carga.tipo === "cargando" || es403) {
    contenido = <VehicleLoader label="Cargando saneamientos…" />;
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No pudimos cargar la bandeja"
        action={
          <button
            type="button"
            onClick={() => {
              setCarga({ tipo: "cargando" });
              setIntento((n) => n + 1);
            }}
            className={BOTON_REINTENTAR_CLS}
          >
            Reintentar
          </button>
        }
      >
        Intenta de nuevo en un momento.
      </InlineAlert>
    );
  } else if (filas.length === 0) {
    contenido = hayFiltrosBandeja(filtros) ? (
      <EstadoVacio
        anunciar
        titulo="Ningún saneamiento coincide"
        mensaje="Cambia los filtros o vuelve a los de por defecto."
        acciones={[
          <button key="limpiar" type="button" onClick={limpiar} className={BOTON_SECUNDARIO_CLS}>
            Limpiar filtros
          </button>,
        ]}
      />
    ) : (
      <EstadoVacio icono={<Icono nombre="check" className="h-7 w-7" />} titulo="No hay solicitudes por tomar" mensaje="Cuando una compañía pida un saneamiento aparecerá aquí." />
    );
  } else {
    const { lista } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        {/* UA-1: «todas» es un botón visible antes de la barra, como el tablero y PS-3; el <thead> no lleva controles. */}
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => setSeleccion(new Set(seleccionables.map((f) => f.id)))}
            disabled={seleccionables.length === 0 || tomando}
            className={BOTON_SECUNDARIO_CLS}
          >
            <Icono nombre="check" className="h-4 w-4" />
            Seleccionar las solicitudes sin tomar
          </button>
        </div>
        <BarraSeleccion
          cantidad={elegidas.length}
          etiqueta="Saneamientos elegidos"
          textoSeleccion={(n) => `${n} ${plural(n, "saneamiento elegido", "saneamientos elegidos")}`}
          onQuitar={() => setSeleccion(new Set())}
          acciones={
            // UA-13: el Button del kit, con su girador y aria-busy mientras toma.
            <Button size="sm" className="gap-1.5 text-sm" cargando={tomando} onClick={() => void tomar()}>
              {tomando ? (
                "Tomando…"
              ) : (
                <>
                  <Icono nombre="check" className="h-4 w-4" />
                  {`Tomar (${elegidas.length})`}
                </>
              )}
            </Button>
          }
        />
        {errorTomar && (
          <p role="alert" className="text-sm font-semibold text-[color:var(--di-danger)]">
            {errorTomar}
          </p>
        )}
        <div className={CONTENEDOR_TABLA_CLS}>
          <table aria-label="Saneamientos" className={`w-full border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
            <thead className={TABLA_MOVIL_THEAD_CLS}>
              <tr>
                <th scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                  <span className="sr-only">Selección</span>
                </th>
                {COLUMNAS.map((c) => (
                  <th key={c} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className={TABLA_MOVIL_TBODY_CLS}>
              {lista.saneamientos.map((f) => {
                // UA-10: la placa, o el VIN, o el identificador; sin ninguno, «Sin placa». Nunca el vehiculo_id.
                const identificador = f.placa || f.vin || f.identificador || "";
                const seleccionable = f.estado === "SOLICITADO";
                const motivoId = `${id}-${f.id}-motivo`;
                return (
                  <tr
                    key={f.id}
                    // Fila clicable con un enlace real en la placa para el teclado (patrón del listado de diagnósticos).
                    onClick={(e) => {
                      if ((e.target as HTMLElement).closest("a, button, input, label")) return;
                      router.push(irA(f));
                    }}
                    className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} cursor-pointer ${TABLA_MOVIL_ROW_CLS}`}
                  >
                    <td data-label="Elegir" className={CELDA_CLS}>
                      <input
                        type="checkbox"
                        className={MARCA_CLS}
                        aria-label={`Seleccionar ${laPlaca(identificador)}`}
                        checked={seleccion.has(f.id)}
                        disabled={!seleccionable}
                        aria-describedby={seleccionable ? undefined : motivoId}
                        onChange={() => alternar(f)}
                      />
                      {!seleccionable && (
                        <span id={motivoId} hidden>
                          {MOTIVO_NO_SELECCIONABLE}
                        </span>
                      )}
                    </td>
                    <th scope="row" data-label="Placa" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} text-left`}>
                      {/* AP-2 (N-1): el respaldo «Sin placa» va en texto normal, como en PS-3; solo un identificador real va en mayúsculas. */}
                      <Link
                        href={irA(f)}
                        className={`font-semibold ${identificador ? "uppercase tracking-wider" : ""} text-[var(--flit-brand-ink)] underline-offset-4 hover:underline ${FOCO_CLS}`}
                      >
                        {identificador || "Sin placa"}
                      </Link>
                    </th>
                    <td data-label="Compañía" className={CELDA_CLS}>
                      {rotuloEmpresa(nombres, f.empresa)}
                    </td>
                    <td data-label="Diagnóstico" className={`${CELDA_CLS} break-words`}>
                      {f.diagnostico_nombre}
                    </td>
                    <td data-label="Modo" className={CELDA_CLS}>
                      {textoModo(f.modo)}
                    </td>
                    <td data-label="Dictamen" className={CELDA_CLS}>
                      {f.veredicto ? (
                        <EtiquetaTono tono={f.veredicto === "BLOQUEADO" ? "danger" : "success"}>{f.veredicto === "BLOQUEADO" ? "Bloqueado" : "Viable"}</EtiquetaTono>
                      ) : (
                        "—"
                      )}
                    </td>
                    <td data-label="Estado" className={CELDA_CLS}>
                      <EtiquetaSaneamiento estado={f.estado} backoffice />
                    </td>
                    <td data-label="Solicitado" className={`${CELDA_CLS} tabular-nums`}>
                      {fecha(f.solicitado_en)}
                    </td>
                    <td data-label="Tomado" className={`${CELDA_CLS} tabular-nums`}>
                      {fecha(f.tomado_en)}
                    </td>
                    <td data-label="Cerrado" className={`${CELDA_CLS} tabular-nums`}>
                      {fecha(f.cerrado_en)}
                    </td>
                    <td data-label="Pendientes" className={`${CELDA_CLS} tabular-nums`}>
                      {f.renglones_total > 0 ? `${f.renglones_pendientes} de ${f.renglones_total}` : "—"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        {/* UA-2 (rechaza D-61): el pie estándar del kit, con «Filas por página» de TAMANOS_DE_PAGINA. */}
        <Pagination
          page={lista.pagina}
          pageSize={lista.tamano}
          totalCount={lista.total}
          onPageChange={(pagina) => aplicar({ pagina })}
          onPageSizeChange={(tamano) => aplicar({ tamano, pagina: 1 })}
          ariaLabel="Paginación de saneamientos"
          noun="saneamientos"
        />
      </div>
    );
  }

  const total = carga.tipo === "listo" ? carga.lista.total : null;

  return (
    <>
      <CabeceraModulo
        titulo="Saneamientos"
        descripcion="Solicitudes de saneamiento de las compañías"
        icono="pago"
        acciones={
          <span className="flex flex-col items-end gap-1">
            <a href={urlExcelBandeja(filtros)} download onClick={descargarExcel} aria-busy={descargando || undefined} className={BOTON_SECUNDARIO_CLS}>
              <Icono nombre="nube-bajar" className="h-4 w-4" />
              {descargando ? "Descargando…" : "Descargar Excel"}
            </a>
            {errorExcel && (
              <span role="alert" className="max-w-xs text-right text-xs font-semibold text-[color:var(--di-danger)]">
                {errorExcel}
              </span>
            )}
          </span>
        }
      />
      <section aria-label="Filtros" className={`${TARJETA_CLS} flex flex-col gap-4`}>
        <div role="group" aria-label="Filtrar por estado" className={CHIPS_FILTRO_FILA_CLS}>
          {ESTADOS_SANEAMIENTO.map((estado) => (
            <ChipFiltro
              key={estado}
              etiqueta={etiquetaSaneamiento(estado, { backoffice: true })?.texto ?? ""}
              activo={filtros.estados.includes(estado)}
              onClick={() => alternarEstado(estado)}
            />
          ))}
        </div>
        <div className="flex flex-col gap-1">
          <SelectorEmpresaBackoffice id={`${id}-empresa`} value={filtros.empresa} onChange={(empresa) => aplicar({ empresa: empresa.id })} className="w-full max-w-xl" />
          {filtros.empresa && (
            <button type="button" onClick={() => aplicar({ empresa: "" })} className={`self-start ${ENLACE_ACCION_CLS}`}>
              Todas las compañías
            </button>
          )}
        </div>
        <form role="search" aria-label="Filtros de la bandeja" onSubmit={alEnviar} className="grid gap-3 sm:grid-cols-3">
          <label className="flex flex-col gap-1">
            <span className={ETIQUETA_CLS}>Placa</span>
            <input type="text" value={placa} maxLength={20} onChange={(e) => setPlaca(e.target.value)} onBlur={aplicarPlaca} className={`${CAMPO_CLS} uppercase`} autoComplete="off" />
          </label>
          <label className="flex flex-col gap-1">
            <span className={ETIQUETA_CLS}>Desde</span>
            <input type="date" value={filtros.desde} max={filtros.hasta || undefined} onChange={(e) => aplicar({ desde: e.target.value })} className={CAMPO_FECHA_CLS} />
          </label>
          <label className="flex flex-col gap-1">
            <span className={ETIQUETA_CLS}>Hasta</span>
            <input type="date" value={filtros.hasta} min={filtros.desde || undefined} onChange={(e) => aplicar({ hasta: e.target.value })} className={CAMPO_FECHA_CLS} />
          </label>
          <span className={`${AYUDA_CLS} sm:col-span-3`}>Las fechas son las de la solicitud. La placa se aplica al pulsar Enter o al salir del campo.</span>
        </form>
        {hayFiltrosBandeja(filtros) && (
          <button type="button" onClick={limpiar} className={`self-start ${BOTON_SECUNDARIO_CLS}`}>
            Limpiar filtros
          </button>
        )}
      </section>
      {aviso && (
        // UA-9: el destino del foco conserva el indicador visible. UA-4: el aviso no es región viva; lo lee el foco.
        <div ref={avisoRef} tabIndex={-1} className={`rounded-[9px] ${FOCO_CLS}`}>
          <InlineAlert tone={aviso.tono} title={aviso.tono === "success" ? "Saneamientos tomados" : "Algunos no se tomaron"} vivo={false}>
            {aviso.texto}
          </InlineAlert>
        </div>
      )}
      {total !== null && <p className={AYUDA_CLS}>{`${total} ${plural(total, "saneamiento", "saneamientos")}`}</p>}
      {contenido}
      <div data-region-viva>{region}</div>
    </>
  );
}
