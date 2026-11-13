"use client";

import { useEffect, useRef, useState } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { PanelLateral } from "@flit/ui/PanelLateral";
import { BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, FOCO_CLS } from "@/components/configuracion/estilos";
import { Button } from "@flit/ui/Button";
import { CamposOverrideDictamen } from "@/components/backoffice/CamposOverrideDictamen";
import { ChipEstadoCelda } from "@/components/tablero/CeldaSemaforo";
import { IconoDxCrearTramite } from "@/components/dx/iconos/IconoDxCrearTramite";
import {
  cuerpoAjusteDictamen,
  errorAjusteDictamen,
  puedeAplicarAjuste,
  type ErrorAjuste,
  type FilaAlertamiento,
} from "@/lib/backoffice/ajusteDictamen";
import { nombreFuente, type Fuente } from "@/lib/configuracion";
import type { EstadoCelda } from "@/lib/tablero/api";
import { Etiqueta, TEXTO_SECUNDARIO_CLS, Vacio } from "@/components/tablero/vistasDetalle/comun";
import {
  ajustarDictamen,
  esSinDictamen,
  obtenerDictamen,
  obtenerDictamenBackoffice,
  rotuloAlerta,
  rotuloEfecto,
  rotuloFuenteAlerta,
  type Dictamen,
} from "@/lib/dictamen/api";

export interface PanelDictamenProps {
  diagnosticoId: string;
  vehiculoId: string;
  /** Placa, VIN o identificador, solo para el título. No viaja en el evento. */
  identificador: string;
  modo: "compania" | "backoffice";
  /** Compañía explícita del backoffice. La compañía del token no la usa. */
  empresa?: string;
  /** Capacidad `corregirDictamen`. En la compañía no se pinta aunque llegue en true. */
  puedeCorregir: boolean;
  onClose: () => void;
  /** Botón que abrió el panel: recibe el foco al cerrarlo. */
  devolverFocoA?: HTMLElement | null;
  /** Trámite analizado del lote (`grilla.lote.tipo_tramite`, rótulo de negocio). Sin él, la línea se omite. */
  tramiteAnalizado?: string | null;
  /** HU #14562 AC35: consultas del vehículo con su estado (de las celdas de la fila). Sin ellas, la sección se omite. */
  consultas?: ConsultaPanel[];
  /**
   * HU #14562 F-05: atajo «Crear trámite» en el pie, solo con el dictamen VIABLE (AC36). Quien pinta el panel lo pasa cuando
   * la fila es elegible y la sesión puede derivar; al pulsarlo cierra el panel y abre el flujo de la fila (modales AC37), sin
   * apilar un modal sobre el panel.
   */
  onCrearTramite?: () => void;
}

/** dd/mm/aaaa en la hora de Colombia. Fecha ilegible: el texto crudo. */
function fechaCorta(iso: string): string {
  const fecha = new Date(iso);
  if (Number.isNaN(fecha.getTime())) return iso;
  return fecha.toLocaleDateString("es-CO", { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "America/Bogota" });
}

type Carga = { tipo: "cargando" } | { tipo: "vacio" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; dictamen: Dictamen };

const BOTON_DICTAMEN_CLS = BOTON_SECUNDARIO_CLS.replace("px-4 py-2 text-sm", "px-3 py-1 text-xs");

/** «Dictamen» bajo la placa. Al abrirlo el panel pide el GET; no entra en el sondeo de la grilla. */
export function BotonAbrirDictamen({ identificador, onAbrir }: { identificador: string; onAbrir: (disparador: HTMLElement) => void }) {
  return (
    <button type="button" className={BOTON_DICTAMEN_CLS} aria-label={`Ver dictamen de ${identificador}`} onClick={(e) => onAbrir(e.currentTarget)}>
      Dictamen
    </button>
  );
}

/**
 * HU #14562 AC28: celda de la columna Dictamen del tablero. El veredicto viene en la fila (`FilaVehiculo.veredicto`, B1), así
 * que las cifras y la columna no piden el dictamen por vehículo; el GET sale solo al abrir el panel. Con `ajustado` (B7, D6)
 * se lee «Ajustado por FLIT», sin autor ni motivo.
 */
export function BotonDictamenFila({
  identificador,
  veredicto,
  ajustado = false,
  onAbrir,
}: {
  identificador: string;
  veredicto: { texto: string; tono: "success" | "danger" | "neutral" };
  ajustado?: boolean;
  onAbrir: (disparador: HTMLElement) => void;
}) {
  return (
    <div className="flex flex-col items-start gap-1">
      <button
        type="button"
        className={`rounded-full ${FOCO_CLS}`}
        aria-label={`Ver dictamen de ${identificador}`}
        onClick={(e) => onAbrir(e.currentTarget)}
      >
        <Etiqueta tono={veredicto.tono}>{veredicto.texto}</Etiqueta>
      </button>
      {ajustado && <span className={TEXTO_SECUNDARIO_CLS}>Ajustado por FLIT</span>}
    </div>
  );
}

const nombreVeredicto = (veredicto: Dictamen["veredicto"]) => (veredicto === "BLOQUEADO" ? "Bloqueado" : "Viable");

/** Consulta del vehículo que el panel lista con su estado (AC35); sale de las celdas de la fila, sin pedir nada. */
export interface ConsultaPanel {
  fuente: Fuente;
  estado: EstadoCelda;
}

/** Alertamientos del dictamen vigente como filas editables del ajuste (clave estable «Alertamiento N»). */
const filasDe = (dictamen: Dictamen): FilaAlertamiento[] =>
  dictamen.alertamientos.map((a, i) => ({
    clave: `Alertamiento ${i + 1}`,
    codigo: a.codigo,
    texto: a.texto,
    efecto: a.efecto === "BLOQUEA" ? "BLOQUEA" : "ADVIERTE",
    fuente: a.fuente,
  }));

/**
 * Panel del dictamen vigente (corte 7; HU #14562 F-05), el mismo en el tablero del lote y en la grilla del backoffice: un
 * `PanelLateral` de 640 px con el tablero visible detrás. No es una ruta y no entra en el sondeo: al abrirse pide el GET.
 * Cuatro estados: vacío (SIN_DICTAMEN), cargando, error con Reintentar y lleno. En lleno: veredicto (con texto, no solo
 * color), aviso si está bloqueado, trámite analizado, fecha de emisión, versión del catálogo, la marca «Ajustado por FLIT»
 * (D6, sin autor ni motivo), las consultas del vehículo con su estado y cada alertamiento con título, efecto, detalle
 * (`alertamientos[].texto`, O4) y fuente, nunca el código (Punto 5). Ningún documento de persona (AC38).
 *
 * El ajuste FLIT (ADR-0086, D9; B-PR6 en el frontend) solo se ofrece en el backoffice con la capacidad: veredicto,
 * alertamientos editables, motivo obligatorio y la confirmación explícita antes de «Aplicar». El PUT lleva
 * `version_esperada`; un 409 de versión ofrece «Recargar» y el 409 VEHICULO_CON_TRAMITE, «Confirmar y aplicar» con
 * `confirmar_con_tramite`. Lo digitado se conserva ante cualquier error.
 *
 * Pie (F-05): con `onCrearTramite` y el dictamen VIABLE, «Crear trámite»; el vehículo BLOQUEADO nunca lo tiene (AC36).
 */
export function PanelDictamen({
  diagnosticoId,
  vehiculoId,
  identificador,
  modo,
  empresa,
  puedeCorregir,
  onClose,
  devolverFocoA,
  tramiteAnalizado,
  consultas,
  onCrearTramite,
}: PanelDictamenProps) {
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [elegido, setElegido] = useState<Dictamen["veredicto"] | null>(null);
  const [confirmado, setConfirmado] = useState(false);
  const [motivo, setMotivo] = useState("");
  const [alertamientos, setAlertamientos] = useState<FilaAlertamiento[] | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [errorAjuste, setErrorAjuste] = useState<ErrorAjuste | null>(null);
  // PanelLateral devuelve el foco al elemento activo al abrir; si el que abrió es otro, se le devuelve aquí.
  const disparador = useRef(devolverFocoA);
  useEffect(() => {
    const destino = disparador.current;
    return () => {
      if (destino?.isConnected) destino.focus();
    };
  }, []);
  const ofreceOverride = modo === "backoffice" && puedeCorregir;

  useEffect(() => {
    const control = new AbortController();
    // La compañía inválida del backoffice lanza antes del fetch: el then lo convierte en rechazo, fuera del cuerpo del efecto.
    const pedido = Promise.resolve().then(() =>
      modo === "backoffice"
        ? obtenerDictamenBackoffice(diagnosticoId, vehiculoId, empresa ?? "", control.signal)
        : obtenerDictamen(diagnosticoId, vehiculoId, control.signal),
    );
    pedido.then(
      (dictamen) => {
        if (control.signal.aborted) return;
        setCarga({ tipo: "listo", dictamen });
        // Las filas del ajuste nacen del primer dictamen leído; «Recargar» solo renueva la versión y conserva lo digitado.
        setAlertamientos((actual) => actual ?? filasDe(dictamen));
      },
      (error: unknown) => {
        if (control.signal.aborted) return;
        if (esSinDictamen(error)) setCarga({ tipo: "vacio" });
        else setCarga({ tipo: "error", mensaje: error instanceof Error ? error.message : "No se pudo cargar el dictamen." });
      },
    );
    return () => control.abort();
  }, [diagnosticoId, vehiculoId, empresa, modo, intento]);

  const filas = alertamientos ?? [];
  const listoParaAplicar =
    ofreceOverride && carga.tipo === "listo" && confirmado && !enviando && puedeAplicarAjuste({ veredicto: elegido, motivo, alertamientos: filas });

  async function aplicar(confirmarConTramite = false) {
    if (!ofreceOverride || carga.tipo !== "listo" || !elegido || !empresa || enviando) return;
    setEnviando(true);
    setErrorAjuste(null);
    try {
      const cuerpo = cuerpoAjusteDictamen({ versionEsperada: carga.dictamen.version, veredicto: elegido, alertamientos: filas, motivo, confirmarConTramite });
      const dictamen = await ajustarDictamen(diagnosticoId, vehiculoId, empresa, cuerpo);
      setCarga({ tipo: "listo", dictamen });
      setAlertamientos(filasDe(dictamen));
      setElegido(null);
      setConfirmado(false);
      setMotivo("");
    } catch (error) {
      setErrorAjuste(errorAjusteDictamen(error));
    } finally {
      setEnviando(false);
    }
  }

  function recargar() {
    setErrorAjuste(null);
    setIntento((n) => n + 1);
  }

  const ofreceTramite = onCrearTramite !== undefined && carga.tipo === "listo" && carga.dictamen.veredicto !== "BLOQUEADO";
  const ofreceAplicar = ofreceOverride && carga.tipo === "listo";

  const errorCampo = errorAjuste?.campo ?? null;
  const errorMotivo = errorCampo === "motivo" ? errorAjuste!.mensaje : null;
  const erroresAlertamientos = errorCampo?.startsWith("alertamientos") ? { [errorCampo]: errorAjuste!.mensaje } : undefined;

  let contenido;
  if (carga.tipo === "cargando") {
    contenido = (
      <p role="status" aria-label="Cargando el dictamen" className={TEXTO_SECUNDARIO_CLS}>
        Cargando el dictamen…
      </p>
    );
  } else if (carga.tipo === "vacio") {
    contenido = <Vacio>Este vehículo no tiene un dictamen vigente.</Vacio>;
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo cargar el dictamen"
        action={
          <button type="button" onClick={() => { setCarga({ tipo: "cargando" }); setIntento((n) => n + 1); }} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  } else {
    const { dictamen } = carga;
    contenido = (
      <div className="flex flex-col gap-4">
        <p className="text-sm text-flit-primary">
          Veredicto:{" "}
          <Etiqueta tono={dictamen.veredicto === "BLOQUEADO" ? "danger" : "success"}>{nombreVeredicto(dictamen.veredicto)}</Etiqueta>
        </p>
        {dictamen.veredicto === "BLOQUEADO" && (
          <InlineAlert tone="error" title="Vehículo bloqueado">
            El vehículo está bloqueado: no se puede crear el trámite hasta resolver los impedimentos que bloquean.
          </InlineAlert>
        )}
        <dl className={`grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 ${TEXTO_SECUNDARIO_CLS}`}>
          {tramiteAnalizado && (
            <div className="contents">
              <dt>Trámite analizado</dt>
              <dd className="font-medium text-flit-primary">{tramiteAnalizado}</dd>
            </div>
          )}
          <div className="contents">
            <dt>Emitido el</dt>
            <dd className="font-medium text-flit-primary">{fechaCorta(dictamen.creado_en)}</dd>
          </div>
          <div className="contents">
            <dt>Versión del catálogo</dt>
            <dd className="font-medium text-flit-primary">{dictamen.catalogo_version}</dd>
          </div>
        </dl>
        {dictamen.ajustado_por_flit && (
          <p className="text-sm text-flit-primary">
            <Etiqueta tono="info">Ajustado por FLIT</Etiqueta>
            {dictamen.ajustado_en ? ` el ${fechaCorta(dictamen.ajustado_en)}` : null}
          </p>
        )}
        {dictamen.estado_redaccion === "PENDIENTE" && <p className="text-sm text-flit-primary">redacción IA pendiente</p>}
        <ol aria-label="Alertamientos del dictamen" className="flex flex-col gap-3">
          {dictamen.alertamientos.map((alerta, i) => (
            <li key={`${i}-${alerta.codigo}`} className="rounded-xl border border-border p-3">
              <h3 className="text-sm font-semibold text-flit-primary">{rotuloAlerta(alerta)}</h3>
              <p className={`mt-1 ${TEXTO_SECUNDARIO_CLS}`}>{`Efecto: ${rotuloEfecto(alerta)}`}</p>
              <p className="mt-1 text-sm text-flit-primary">{alerta.texto}</p>
              <p className={TEXTO_SECUNDARIO_CLS}>{`Fuente: ${rotuloFuenteAlerta(alerta)}`}</p>
            </li>
          ))}
        </ol>
        {consultas && consultas.length > 0 && (
          <section aria-labelledby="dictamen-consultas" className="flex flex-col gap-2">
            <h3 id="dictamen-consultas" className="text-sm font-semibold text-flit-primary">
              Consultas del vehículo
            </h3>
            <ul aria-label="Consultas del vehículo" className="flex flex-col gap-1.5">
              {consultas.map((c) => (
                <li key={c.fuente} aria-label={nombreFuente(c.fuente)} className="flex items-center justify-between gap-3 text-sm">
                  <span className="text-flit-primary">{nombreFuente(c.fuente)}</span>
                  <ChipEstadoCelda estado={c.estado} />
                </li>
              ))}
            </ul>
          </section>
        )}
        {ofreceOverride && (
          <CamposOverrideDictamen
            elegido={elegido}
            onElegir={setElegido}
            confirmado={confirmado}
            onConfirmar={setConfirmado}
            aviso={null}
            motivo={motivo}
            onMotivo={setMotivo}
            errorMotivo={errorMotivo}
            alertamientos={filas}
            onAlertamientos={setAlertamientos}
            erroresAlertamientos={erroresAlertamientos}
          />
        )}
        {ofreceOverride && errorAjuste && (
          <InlineAlert
            tone="error"
            title="No se aplicó el ajuste"
            action={
              errorAjuste.ofrecerRecargar ? (
                <button type="button" onClick={recargar} className={BOTON_REINTENTAR_CLS}>
                  Recargar
                </button>
              ) : errorAjuste.pedirConfirmarTramite ? (
                <button type="button" onClick={() => void aplicar(true)} disabled={enviando} className={BOTON_REINTENTAR_CLS}>
                  Confirmar y aplicar
                </button>
              ) : undefined
            }
          >
            {errorAjuste.mensaje}
          </InlineAlert>
        )}
      </div>
    );
  }

  return (
    <PanelLateral
      abierto
      onCerrar={onClose}
      ocupado={enviando}
      ancho={640}
      titulo={`Dictamen · ${identificador}`}
      pie={
        ofreceTramite || ofreceAplicar ? (
          <div className="flex flex-wrap justify-end gap-2">
            {ofreceTramite && (
              <Button size="sm" className="gap-1.5" onClick={onCrearTramite}>
                <IconoDxCrearTramite tamano={16} />
                Crear trámite
              </Button>
            )}
            {ofreceAplicar && (
              <Button cargando={enviando} disabled={!listoParaAplicar} size="sm" onClick={() => void aplicar()}>
                Aplicar
              </Button>
            )}
          </div>
        ) : undefined
      }
    >
      {contenido}
    </PanelLateral>
  );
}
