"use client";

import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { ListaDefinicion } from "@flit/ui/ListaDefinicion";
import { PanelLateral } from "@flit/ui/PanelLateral";
import { Placa } from "@flit/ui/Placa";
import type { StatusTone } from "@flit/ui/StatusBadge";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, FOCO_CLS } from "@/components/configuracion/estilos";
import { RegionDesplazable } from "@/components/comun/RegionDesplazable";
import { EtiquetaTono, SeccionesDeuda } from "@/components/saneamiento/SeccionesDeuda";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, FILA_TABLA_CLS } from "@/components/tablero/vistasDetalle/comun";
import { ApiError } from "@/lib/api";
import {
  obtenerDeuda,
  obtenerSaneamiento,
  urlDeudaExcel,
  urlDeudaPdf,
  urlEvidencia,
  type EstadoSaneamientoActivo,
  type EventoSaneamiento,
  type PanelDeuda,
  type RenglonSaneamiento,
  type ResultadoRenglon,
  type Saneamiento,
  type SaneamientoFila,
} from "@/lib/saneamiento/api";
import { ROTULO_HALLAZGO, ROTULO_RESULTADO, etiquetaSaneamiento, rotuloConcepto } from "@/lib/saneamiento/estados";
import { useDescarga } from "@/lib/reportes/useDescarga";

// PS-2 «Deuda y saneamiento» (spec-ui §3; plan §7.3; HU #14577 AC13–AC19). PanelLateral de 640 px: foco atrapado, Escape
// y «Cerrar», y el foco vuelve a quien lo abrió. Pide en paralelo GET …/deuda y GET …/saneamiento. Cuatro estados del
// patrón de Diagnóstico (D-01): cargando con role="status", error con «Reintentar», «Sin pendientes» en lugar de vacío y
// el 409 VEHICULO_NO_TERMINADO como aviso sin código. Todo valor visual sale de tokens (--di-*, --badge-*); sin hex.
// La bandera no vive aquí: es BotonSanear (FE-A) y entra por `accionBandera` (D-41).

/** Lo que el panel le pasa a la bandera de su cabecera (BotonSanear, variante «cabecera»). */
export interface ContextoBandera {
  /** Saneamiento como lo ve la fila (§1.3): el vehículo del panel ya terminó, así que es elegible si no tiene uno. */
  saneamiento: SaneamientoFila;
  /** Capacidad `sanear` de la sesión. */
  puedeSanear: boolean;
  /** Cambio optimista y su reversión (mismo contrato que `BotonSanear.onCambio`): solo actualiza la etiqueta. */
  onCambio: (estado: EstadoSaneamientoActivo | null) => void;
  /** La API confirmó el cambio: recarga solo GET …/saneamiento (no la deuda) y lo anuncia en la región viva (§3.7). */
  onCambioSaneamiento: (estado: EstadoSaneamientoActivo | null) => void;
  /** Recarga el detalle sin anunciar (409 YA_SOLICITADO o SANEAMIENTO_EN_GESTION: `BotonSanear.onRefrescar`). */
  onRefrescar: () => void;
}

export interface PanelDeudaSaneamientoProps {
  diagnosticoId: string;
  vehiculoId: string;
  /** Placa, VIN o identificador del vehículo: descripción del panel y anuncios. */
  identificador: string;
  /** Capacidad `sanear`: la ayuda «Marca la bandera…» y el contexto de la bandera. */
  puedeSanear: boolean;
  /** Capacidad `descargarReportes`: «Descargar PDF», «Descargar Excel» y «Descargar evidencia». */
  puedeDescargar: boolean;
  onClose: () => void;
  /** Control que abrió el panel (o la fila que abrió el dictamen, si este lo reemplazó): recibe el foco al cerrar. */
  devolverFocoA?: HTMLElement | null;
  /** Bandera de la cabecera. Con una función recibe el estado y los callbacks del panel (ver `ContextoBandera`). */
  accionBandera?: ReactNode | ((contexto: ContextoBandera) => ReactNode);
  /** Avisa a quien pinta el panel (p. ej. la fila del tablero) cada cambio de estado del saneamiento. */
  onCambioEstado?: (estado: EstadoSaneamientoActivo | null) => void;
}

type CargaDeuda = { tipo: "cargando" } | { tipo: "error" } | { tipo: "no-terminado" } | { tipo: "listo"; deuda: PanelDeuda };
type CargaDetalle = { tipo: "cargando" } | { tipo: "error" } | { tipo: "listo"; saneamiento: Saneamiento | null };

const TEXTO_EVENTO: Partial<Record<EventoSaneamiento, string>> = {
  SOLICITADO: "Se solicitó el saneamiento",
  DESMARCADO: "Se quitó la solicitud",
  TOMADO: "FLIT empezó el saneamiento",
  RENGLON_AGREGADO: "FLIT agregó un concepto",
  RENGLON_QUITADO: "FLIT quitó un concepto",
  EVIDENCIA_CARGADA: "FLIT cargó una evidencia",
  EVIDENCIA_QUITADA: "FLIT quitó una evidencia",
  CERRADO_SANEADO: "FLIT cerró el saneamiento como saneado",
  CERRADO_NO_PROCEDE: "FLIT cerró el saneamiento: no procede",
  REABIERTO: "FLIT reabrió el saneamiento",
  ANULADO_FLIT: "FLIT anuló el saneamiento",
};

const TONO_RESULTADO: Record<ResultadoRenglon, StatusTone> = { PENDIENTE: "neutral", SANEADO: "success", NO_PROCEDE: "neutral" };

const ENLACE_CLS = `inline-flex items-center gap-1 rounded-full text-xs font-semibold text-[var(--flit-brand-ink)] underline underline-offset-2 ${FOCO_CLS}`;
const ERROR_CLS = "text-xs font-semibold text-[color:var(--di-danger)]";
const ERROR_DESCARGA = "No pudimos generar el archivo. Intenta de nuevo.";

/** dd/mm/aaaa en la hora de Colombia (fechas con hora del saneamiento). Ilegible: el texto tal cual. */
function fechaColombia(iso: string | null): string | null {
  if (!iso) return null;
  const fecha = new Date(iso);
  if (Number.isNaN(fecha.getTime())) return iso;
  return fecha.toLocaleDateString("es-CO", { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "America/Bogota" });
}

/** Para la compañía un ANULADO es «sin saneamiento» (§6.3, D-06). */
const activo = (s: Saneamiento | null): EstadoSaneamientoActivo | null => (s && s.estado !== "ANULADO" ? s.estado : null);

const es403 = (error: unknown): error is ApiError => error instanceof ApiError && error.status === 403;

/** «Descargar evidencia» de un renglón por useDescarga; el nombre lo da el servidor. */
function EnlaceEvidencia({ href, concepto }: { href: string; concepto: string }) {
  const { descargando, error, alHacerClic } = useDescarga("evidencia.pdf");
  return (
    <span className="inline-flex flex-col items-end gap-1 min-[901px]:items-start">
      <a href={href} download onClick={alHacerClic} aria-label={`Descargar evidencia de ${concepto}`} aria-busy={descargando || undefined} className={ENLACE_CLS}>
        {descargando ? "Descargando…" : "Descargar evidencia"}
      </a>
      {error && (
        <span role="alert" className={ERROR_CLS}>
          {ERROR_DESCARGA}
        </span>
      )}
    </span>
  );
}

const COLUMNAS_RENGLONES = ["Concepto", "Hallazgo", "Resultado", "Nota", "Evidencia"] as const;

function TablaRenglones({ renglones, evidenciaDe }: { renglones: RenglonSaneamiento[]; evidenciaDe: ((r: RenglonSaneamiento) => string) | null }) {
  return (
    <RegionDesplazable nombre="Conceptos del saneamiento" className="rounded-xl border border-border">
      <table aria-label="Conceptos del saneamiento" className={`w-full border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {COLUMNAS_RENGLONES.map((c) => (
              <th key={c} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {renglones.map((r) => {
            const concepto = rotuloConcepto(r);
            const celdas: ReactNode[] = [
              concepto,
              r.hallazgo ? ROTULO_HALLAZGO[r.hallazgo] : "—",
              <EtiquetaTono key="resultado" tono={TONO_RESULTADO[r.resultado] ?? "neutral"}>
                {ROTULO_RESULTADO[r.resultado] ?? ROTULO_RESULTADO.PENDIENTE}
              </EtiquetaTono>,
              r.nota?.trim() ? r.nota : "—",
              !r.evidencia ? "—" : evidenciaDe ? <EnlaceEvidencia href={evidenciaDe(r)} concepto={concepto} /> : "Con evidencia",
            ];
            return (
              <tr key={r.id} className={`${FILA_TABLA_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                {celdas.map((celda, j) => (
                  <td
                    key={COLUMNAS_RENGLONES[j]}
                    data-label={COLUMNAS_RENGLONES[j]}
                    className={`${CELDA_TABLA_CLS} break-words ${TABLA_MOVIL_CELL_CLS}${j === 0 ? ` ${TABLA_MOVIL_CELL_TITULO_CLS}` : ""}`}
                  >
                    {celda}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </RegionDesplazable>
  );
}

/** Sección «Saneamiento» (§3.6): lo que ve la compañía. Nunca un id ni el nombre de un operador (SP-15). */
function SeccionSaneamiento({
  saneamiento,
  veredicto,
  evidenciaDe,
}: {
  saneamiento: Saneamiento;
  veredicto: "VIABLE" | "BLOQUEADO" | null;
  evidenciaDe: ((r: RenglonSaneamiento) => string) | null;
}) {
  const historial = saneamiento.historial.filter((e) => TEXTO_EVENTO[e.evento]);
  const motivos = [
    ...(saneamiento.motivo_cierre ? [{ rotulo: "Motivo del cierre", texto: saneamiento.motivo_cierre }] : []),
    ...(saneamiento.motivo_anulacion ? [{ rotulo: "Motivo de la anulación", texto: saneamiento.motivo_anulacion }] : []),
  ];
  return (
    <div className="flex flex-col gap-4">
      <ListaDefinicion
        columnas={2}
        vacio="—"
        items={[
          { termino: "Solicitado", valor: fechaColombia(saneamiento.solicitado_en) },
          { termino: "Por", valor: saneamiento.solicitado_por_mi ? "Tú" : "Otro usuario de la compañía" },
          { termino: "Tomado por FLIT", valor: fechaColombia(saneamiento.tomado_en) },
          { termino: "Cerrado", valor: fechaColombia(saneamiento.cerrado_en) },
        ]}
      />
      {saneamiento.estado === "SANEADO" && veredicto === "BLOQUEADO" && (
        <InlineAlert tone="info" title="El dictamen no cambia solo">
          El saneamiento está registrado, pero el dictamen no cambia solo. Se actualiza cuando se vuelvan a consultar las fuentes o FLIT lo ajuste.
        </InlineAlert>
      )}
      {saneamiento.observacion_flit?.trim() && (
        <div className="flex flex-col gap-1">
          <p className="text-xs font-semibold text-flit-primary">Observación de FLIT</p>
          <blockquote className="whitespace-pre-line break-words border-l-2 border-[color:var(--di-border)] pl-3 text-sm text-flit-primary">
            {saneamiento.observacion_flit}
          </blockquote>
        </div>
      )}
      {motivos.map((m) => (
        <div key={m.rotulo} className="flex flex-col gap-1">
          <p className="text-xs font-semibold text-flit-primary">{m.rotulo}</p>
          <p className="whitespace-pre-line break-words text-sm text-flit-primary">{m.texto}</p>
        </div>
      ))}
      {saneamiento.renglones.length > 0 ? (
        <TablaRenglones renglones={saneamiento.renglones} evidenciaDe={evidenciaDe} />
      ) : (
        <p className={AYUDA_CLS}>FLIT aún no registra conceptos.</p>
      )}
      {historial.length > 0 && (
        <ol aria-label="Historial del saneamiento" className="flex flex-col gap-2">
          {historial.map((e, i) => (
            <li key={`${i}-${e.evento}`} className="flex flex-col gap-0.5 border-l-2 border-[color:var(--di-divider)] pl-3 text-sm text-flit-primary">
              <span>
                <span className={AYUDA_CLS}>{fechaColombia(e.ocurrido_en)}</span>
                {" · "}
                {TEXTO_EVENTO[e.evento]}
              </span>
              <span className={AYUDA_CLS}>{e.actor === "FLIT" ? "FLIT" : "Tu compañía"}</span>
              {e.motivo?.trim() && <span className="whitespace-pre-line break-words">{`Motivo: ${e.motivo}`}</span>}
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

/**
 * Panel «Deuda y saneamiento». Secciones: Estado (placa y etiqueta), Dictamen y Deuda por fuente (`SeccionesDeuda`),
 * Saneamiento (si existe) y, en el pie, «Descargar PDF» y «Descargar Excel» con la capacidad. Tras marcar o desmarcar
 * con la bandera se recarga solo el detalle del saneamiento; el foco no se mueve y el resultado se anuncia (§3.7).
 */
export function PanelDeudaSaneamiento({
  diagnosticoId,
  vehiculoId,
  identificador,
  puedeSanear,
  puedeDescargar,
  onClose,
  devolverFocoA,
  accionBandera,
  onCambioEstado,
}: PanelDeudaSaneamientoProps) {
  const router = useRouter();
  const idSeccion = useId();
  const [deuda, setDeuda] = useState<CargaDeuda>({ tipo: "cargando" });
  const [detalle, setDetalle] = useState<CargaDetalle>({ tipo: "cargando" });
  const [intentoDeuda, setIntentoDeuda] = useState(0);
  const [intentoDetalle, setIntentoDetalle] = useState(0);
  // Estado optimista de la bandera; `undefined` = manda lo leído. Se limpia al llegar el detalle recargado.
  const [estadoLocal, setEstadoLocal] = useState<EstadoSaneamientoActivo | null | undefined>(undefined);
  const [anuncio, setAnuncio] = useState<{ n: number; texto: string } | null>(null);
  const pdf = useDescarga(`deuda-${identificador}.pdf`);
  const excel = useDescarga(`deuda-${identificador}.xlsx`);

  // PanelLateral devuelve el foco al elemento activo al abrir; si quien abrió es otro (el dictamen reemplazado), aquí.
  const disparador = useRef(devolverFocoA);
  useEffect(() => {
    const destino = disparador.current;
    return () => {
      if (destino?.isConnected) destino.focus();
    };
  }, []);

  // El router va en una referencia: las lecturas no deben repetirse porque cambie su identidad entre renders.
  const routerRef = useRef(router);
  useEffect(() => {
    routerRef.current = router;
  }, [router]);
  const alNegar = useCallback((error: ApiError) => routerRef.current.replace(`/403?code=${encodeURIComponent(error.code ?? "SIN_PERMISO")}`), []);

  useEffect(() => {
    const control = new AbortController();
    obtenerDeuda(diagnosticoId, vehiculoId, control.signal).then(
      (d) => {
        if (!control.signal.aborted) setDeuda({ tipo: "listo", deuda: d });
      },
      (error: unknown) => {
        if (control.signal.aborted) return;
        if (es403(error)) alNegar(error);
        if (error instanceof ApiError && error.status === 409 && error.code === "VEHICULO_NO_TERMINADO") setDeuda({ tipo: "no-terminado" });
        else setDeuda({ tipo: "error" });
      },
    );
    return () => control.abort();
  }, [diagnosticoId, vehiculoId, intentoDeuda, alNegar]);

  useEffect(() => {
    const control = new AbortController();
    obtenerSaneamiento(diagnosticoId, vehiculoId, control.signal).then(
      (s) => {
        if (control.signal.aborted) return;
        setDetalle({ tipo: "listo", saneamiento: s });
        setEstadoLocal(undefined);
      },
      (error: unknown) => {
        if (control.signal.aborted) return;
        if (es403(error)) alNegar(error);
        setDetalle({ tipo: "error" });
      },
    );
    return () => control.abort();
  }, [diagnosticoId, vehiculoId, intentoDetalle, alNegar]);

  const reintentarTodo = () => {
    setDeuda({ tipo: "cargando" });
    setDetalle({ tipo: "cargando" });
    setIntentoDeuda((n) => n + 1);
    setIntentoDetalle((n) => n + 1);
  };
  const recargarDetalle = () => setIntentoDetalle((n) => n + 1);

  const saneamientoLeido = detalle.tipo === "listo" ? detalle.saneamiento : null;
  const estadoLeido: EstadoSaneamientoActivo | null =
    detalle.tipo === "listo" ? activo(detalle.saneamiento) : deuda.tipo === "listo" ? (deuda.deuda.saneamiento?.estado ?? null) : null;
  const estado = estadoLocal !== undefined ? estadoLocal : estadoLeido;

  const contexto: ContextoBandera = {
    saneamiento: { estado, elegible: estado === null, motivo_no_elegible: estado === null ? null : "YA_SOLICITADO" },
    puedeSanear,
    onCambio: (siguiente) => {
      setEstadoLocal(siguiente);
      onCambioEstado?.(siguiente);
    },
    onCambioSaneamiento: (siguiente) => {
      setEstadoLocal(siguiente);
      onCambioEstado?.(siguiente);
      setAnuncio((a) => ({
        n: (a?.n ?? 0) + 1,
        texto: siguiente ? `Saneamiento solicitado para ${identificador}.` : `Se quitó la solicitud de saneamiento de ${identificador}.`,
      }));
      recargarDetalle();
    },
    onRefrescar: recargarDetalle,
  };

  const evidenciaDe = puedeDescargar ? (r: RenglonSaneamiento) => urlEvidencia(diagnosticoId, vehiculoId, r.id) : null;
  const etiqueta = etiquetaSaneamiento(estado);

  let contenido: ReactNode;
  if (deuda.tipo === "cargando") {
    contenido = (
      <div role="status" aria-busy="true" aria-label="Cargando deuda" className="flex flex-col gap-3">
        <span className="sr-only">Cargando deuda…</span>
        {[0, 1, 2].map((i) => (
          <div key={i} aria-hidden="true" className="h-14 rounded-[9px] bg-[var(--di-surface-sunken)] motion-safe:animate-pulse" />
        ))}
      </div>
    );
  } else if (deuda.tipo === "no-terminado") {
    contenido = (
      <InlineAlert tone="warning" title="El vehículo aún no termina">
        Cuando terminen sus consultas verás aquí la deuda por fuente.
      </InlineAlert>
    );
  } else if (deuda.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No pudimos cargar la deuda"
        action={
          <button type="button" onClick={reintentarTodo} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        Revisa tu conexión e intenta de nuevo.
      </InlineAlert>
    );
  } else {
    const placa = deuda.deuda.vehiculo.placa?.trim();
    const veredicto = deuda.deuda.dictamen?.veredicto ?? null;
    // Al desmarcar (estado local null) la sección se va de inmediato; un ANULADO leído sí se muestra, con su motivo.
    const verSaneamiento = estadoLocal !== null ? saneamientoLeido : null;
    contenido = (
      <div className="flex flex-col gap-6">
        <section aria-label="Estado" className="flex flex-col gap-2">
          <div className="flex flex-wrap items-center gap-3">
            {placa ? <Placa placa={placa} /> : <span className="text-base font-semibold text-flit-primary">{identificador}</span>}
            {etiqueta && <EtiquetaTono tono={etiqueta.tono}>{etiqueta.texto}</EtiquetaTono>}
          </div>
          {estado === null && puedeSanear && <p className={AYUDA_CLS}>Marca la bandera para pedir a FLIT que gestione el saneamiento.</p>}
        </section>
        <SeccionesDeuda deuda={deuda.deuda} />
        {verSaneamiento ? (
          <section aria-labelledby={`${idSeccion}-saneamiento`} className="flex flex-col gap-3">
            <h3 id={`${idSeccion}-saneamiento`} className="text-base font-semibold text-flit-primary">
              Saneamiento
            </h3>
            <SeccionSaneamiento saneamiento={verSaneamiento} veredicto={veredicto} evidenciaDe={evidenciaDe} />
          </section>
        ) : estado !== null && detalle.tipo === "error" ? (
          <InlineAlert
            tone="error"
            title="No pudimos cargar el saneamiento"
            action={
              <button type="button" onClick={recargarDetalle} className={BOTON_REINTENTAR_CLS}>
                Reintentar
              </button>
            }
          >
            El resto del panel sigue disponible.
          </InlineAlert>
        ) : estado !== null && detalle.tipo === "cargando" ? (
          <p role="status" className={AYUDA_CLS}>
            Cargando el saneamiento…
          </p>
        ) : null}
      </div>
    );
  }

  const bandera = deuda.tipo === "listo" ? (typeof accionBandera === "function" ? accionBandera(contexto) : accionBandera) : null;
  const errorDescarga = pdf.error || excel.error;

  return (
    <PanelLateral
      abierto
      onCerrar={onClose}
      ancho={640}
      titulo="Deuda y saneamiento"
      descripcion={identificador}
      acciones={bandera ?? undefined}
      pie={
        puedeDescargar && deuda.tipo === "listo" ? (
          <div className="flex flex-col gap-2">
            <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
              <a
                href={urlDeudaPdf(diagnosticoId, vehiculoId)}
                download
                onClick={pdf.alHacerClic}
                aria-busy={pdf.descargando || undefined}
                className={BOTON_SECUNDARIO_CLS}
              >
                <Icono nombre="nube-bajar" size={16} />
                {pdf.descargando ? "Descargando…" : "Descargar PDF"}
              </a>
              <a
                href={urlDeudaExcel(diagnosticoId, vehiculoId)}
                download
                onClick={excel.alHacerClic}
                aria-busy={excel.descargando || undefined}
                className={BOTON_SECUNDARIO_CLS}
              >
                <Icono nombre="nube-bajar" size={16} />
                {excel.descargando ? "Descargando…" : "Descargar Excel"}
              </a>
            </div>
            {errorDescarga && (
              <p role="alert" className={ERROR_CLS}>
                {ERROR_DESCARGA}
              </p>
            )}
          </div>
        ) : undefined
      }
    >
      {contenido}
      <p role="status" aria-live="polite" className="sr-only" data-region-viva="">
        {anuncio && <span key={anuncio.n}>{anuncio.texto}</span>}
      </p>
    </PanelLateral>
  );
}
