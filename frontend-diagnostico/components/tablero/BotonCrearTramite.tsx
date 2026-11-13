"use client";

import { useEffect, useEffectEvent, useId, useRef, useState } from "react";
import { ExternalLink, FilePlus2 } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Modal } from "@flit/ui/Modal";
import { etiquetaKit } from "@flit/ui/StatusBadge";
import {
  AYUDA_CLS,
  BOTON_PRIMARIO_CLS,
  BOTON_REINTENTAR_CLS,
  BOTON_SECUNDARIO_CLS,
  CAMPO_CLS,
  ETIQUETA_CLS,
  FOCO_CLS,
} from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { listarTiposTramite, type ModoDiagnostico, type TipoTramite } from "@/lib/diagnosticos/api";
import type { FilaVehiculo, TramiteFila } from "@/lib/tablero/api";
import {
  crearTramite,
  derivacionDeFila,
  mensajeCrearTramite,
  numeroVisible,
  pideTipo,
  rotuloMotivo,
  urlTramite,
} from "@/lib/tablero/derivacion";

/** Pastilla compacta de la fila, como «Reintentar placa». */
const FILA_CLS = `inline-flex items-center gap-1 rounded-full border border-current px-2.5 py-0.5 text-xs font-semibold text-[var(--flit-brand-ink)] transition disabled:cursor-not-allowed disabled:opacity-60 ${FOCO_CLS}`;
const ENLACE_CLS = `inline-flex items-center gap-1 rounded-full text-xs font-semibold text-[var(--flit-brand-ink)] underline underline-offset-2 ${FOCO_CLS}`;

type EstadoTipos = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; tipos: TipoTramite[] };

export interface ModalTipoTramiteProps {
  identificador: string;
  onClose: () => void;
  onConfirmar: (codigo: string) => void;
  /** Texto bajo el título; por defecto, el de una placa (`identificador`). El masivo lo cambia por el de las N seleccionadas. */
  descripcion?: string;
  /** Texto del botón de confirmar; por defecto «Crear trámite» (masivo: «Crear trámites (N)»; fila Simple: «Ir al wizard»). */
  textoConfirmar?: string;
  /** Título del modal; por defecto «Tipo de trámite» (fila Simple: «Crear trámite para {placa}», HU #14562 AC37). */
  titulo?: string;
  /** Ayuda bajo el tipo; por defecto, que el borrador queda en Trámites. */
  ayuda?: string;
  /** El borrador se abre en una pestaña nueva al confirmar: el botón lleva el ícono de enlace externo. */
  abreEnPestana?: boolean;
}

/**
 * Modal del tipo de trámite (Punto 4, decisión 4.3): solo en lote Simple. Pide el tipo una vez con el catálogo de
 * GET /tipos-tramite (el mismo de la cabecera del diagnóstico). Cuatro estados: cargando, error con Reintentar, vacío y lleno.
 */
export function ModalTipoTramite({
  identificador,
  onClose,
  onConfirmar,
  descripcion,
  textoConfirmar = "Crear trámite",
  titulo = "Tipo de trámite",
  ayuda = "El borrador queda en Trámites para que lo completes allí.",
  abreEnPestana = false,
}: ModalTipoTramiteProps) {
  const id = useId();
  const [intento, setIntento] = useState(0);
  const [tipos, setTipos] = useState<EstadoTipos>({ tipo: "cargando" });
  const [codigo, setCodigo] = useState("");
  const selectRef = useRef<HTMLSelectElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    listarTiposTramite(controller.signal)
      .then((lista) => setTipos({ tipo: "listo", tipos: lista }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setTipos({ tipo: "error", mensaje: e instanceof Error ? e.message : "Intenta de nuevo en un momento." });
      });
    return () => controller.abort();
  }, [intento]);

  const hayTipos = tipos.tipo === "listo" && tipos.tipos.length > 0;
  useEffect(() => {
    if (hayTipos) selectRef.current?.focus();
  }, [hayTipos]);

  function reintentar() {
    setTipos({ tipo: "cargando" });
    setIntento((n) => n + 1);
  }

  let cuerpo;
  if (tipos.tipo === "cargando") {
    cuerpo = (
      <p role="status" className={AYUDA_CLS}>
        Cargando los tipos de trámite…
      </p>
    );
  } else if (tipos.tipo === "error") {
    cuerpo = (
      <InlineAlert
        tone="error"
        title="No se pudieron cargar los tipos de trámite"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {tipos.mensaje}
      </InlineAlert>
    );
  } else if (tipos.tipos.length === 0) {
    cuerpo = (
      <InlineAlert tone="warning" title="No hay tipos de trámite disponibles">
        Tu compañía no tiene tipos de trámite habilitados para crear el borrador.
      </InlineAlert>
    );
  } else {
    cuerpo = (
      <div className="flex flex-col gap-1.5">
        <label htmlFor={`${id}-tipo`} className={ETIQUETA_CLS}>
          Tipo de trámite
        </label>
        <select
          ref={selectRef}
          id={`${id}-tipo`}
          value={codigo}
          onChange={(e) => setCodigo(e.target.value)}
          aria-describedby={`${id}-ayuda`}
          className={CAMPO_CLS}
        >
          <option value="">Selecciona un tipo</option>
          {tipos.tipos.map((t) => (
            <option key={t.codigo} value={t.codigo}>
              {t.nombre}
            </option>
          ))}
        </select>
        <p id={`${id}-ayuda`} className={AYUDA_CLS}>
          {ayuda}
        </p>
      </div>
    );
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      zClassName="z-[110]"
      title={titulo}
      description={descripcion ?? `Elige el tipo del borrador para la placa ${identificador}.`}
      footer={
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" onClick={onClose} className={BOTON_SECUNDARIO_CLS}>
            Cancelar
          </button>
          <button type="button" onClick={() => onConfirmar(codigo)} disabled={!hayTipos || !codigo} className={BOTON_PRIMARIO_CLS}>
            {abreEnPestana ? <ExternalLink className="h-4 w-4" aria-hidden="true" /> : <FilePlus2 className="h-4 w-4" aria-hidden="true" />}
            {textoConfirmar}
          </button>
        </div>
      }
    >
      {cuerpo}
    </Modal>
  );
}

export interface ModalConfirmarTramiteProps {
  identificador: string;
  /** Trámite analizado del lote (`grilla.lote.tipo_tramite.nombre`). Sin él, el texto dice «trámite». */
  tramite?: string | null;
  onClose: () => void;
  onConfirmar: () => void;
}

/** «traspaso» para el título y el botón («¿Crear el traspaso…?», «Crear traspaso»); otro nombre o ninguno, «trámite». */
function sustantivoTramite(tramite?: string | null): string {
  const nombre = tramite?.trim() ?? "";
  return /^traspaso/i.test(nombre) ? nombre.toLowerCase() : "trámite";
}

/**
 * Confirmación del trámite en un lote Con análisis IA con veredicto Viable (HU #14562 AC37, spec 05): «¿Crear el traspaso
 * de {placa}?», la etiqueta Viable «para {trámite}» y el aviso de que se abre en una pestaña nueva. El tipo es el del lote,
 * así que no se pide.
 */
export function ModalConfirmarTramite({ identificador, tramite, onClose, onConfirmar }: ModalConfirmarTramiteProps) {
  const sustantivo = sustantivoTramite(tramite);
  const confirmarRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    confirmarRef.current?.focus();
  }, []);
  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      zClassName="z-[110]"
      title={`¿Crear el ${sustantivo} de ${identificador}?`}
      footer={
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" onClick={onClose} className={BOTON_SECUNDARIO_CLS}>
            Cancelar
          </button>
          <button ref={confirmarRef} type="button" onClick={onConfirmar} className={BOTON_PRIMARIO_CLS}>
            <ExternalLink className="h-4 w-4" aria-hidden="true" />
            {`Crear ${sustantivo}`}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="flex flex-wrap items-center gap-2 text-sm text-flit-primary">
          <span {...etiquetaKit("success")}>Viable</span>
          {tramite && <span>{`para ${tramite}`}</span>}
        </p>
        <p className={AYUDA_CLS}>Se abre en una pestaña nueva en Trámites, con el borrador listo para completar.</p>
      </div>
    </Modal>
  );
}

/**
 * Pestaña para el borrador, abierta en el mismo clic (si se abre después de la respuesta, el navegador la bloquea). Sin
 * `opener`, para que Trámites no pueda tocar esta página. Bloqueada: null, y la fila igual ofrece «Ver trámite #N».
 */
function abrirPestana(): Window | null {
  const pestana = window.open("", "_blank");
  if (pestana) pestana.opener = null;
  return pestana ?? null;
}

export interface AccionTramiteFilaProps {
  diagnosticoId: string;
  vehiculo: FilaVehiculo;
  /** Modo del lote (si la grilla lo trae): en SIMPLE se pide el tipo antes de crear. */
  modo?: ModoDiagnostico;
  /** Origen de Trámites (TRAMITES_URL, el mismo del shell). */
  tramitesUrl: string;
  /** Compañía de la sesión, para el `?t=` del detalle en Trámites. */
  empresaId?: string | null;
  /** El borrador quedó creado (o ya existía): quien pinta la grilla marca la fila sin esperar al sondeo. */
  onCreado: (vehiculoId: string, tramite: TramiteFila) => void;
  /** El estado cambió en el servidor (409): conviene volver a leer la grilla. */
  onRefrescar: () => void;
  /** Trámite analizado del lote (`grilla.lote.tipo_tramite.nombre`), para el modal del lote Con análisis IA. */
  tramiteAnalizado?: string | null;
  /**
   * HU #14562 F-05: cada valor nuevo pide el mismo flujo que pulsar «Crear trámite» en la fila (lo usa el atajo del pie del
   * panel del dictamen). Sin efecto si la fila ya no es elegible o está creando.
   */
  solicitudCrear?: number;
}

/** Texto y número del trámite con un solo espacio (sin número, solo el texto). */
const unir = (texto: string, numero: string) => (numero ? `${texto} ${numero}` : texto);

type Mensaje = { tono: "info" | "error"; texto: string };

/**
 * Acción de trámite de una fila del tablero (Punto 4, incremento 2). Solo se pinta con la capacidad `derivar` (lo decide
 * quien la usa). Con trámite: «Ver trámite #N», que abre el borrador en Trámites en una pestaña nueva. Elegible: «Crear
 * trámite» (en lote Simple, primero el modal del tipo), con cargando y deshabilitado mientras responde. No elegible: sin
 * acción, con el motivo en lenguaje de negocio. El resultado (creado, «ya tenía trámite», error de negocio o de permiso) se
 * anuncia en la propia fila. La API vuelve a exigir el permiso (403).
 */
export function AccionTramiteFila({
  diagnosticoId,
  vehiculo,
  modo,
  tramitesUrl,
  empresaId,
  onCreado,
  onRefrescar,
  tramiteAnalizado,
  solicitudCrear,
}: AccionTramiteFilaProps) {
  const derivacion = derivacionDeFila(vehiculo);
  const identificador = vehiculo.placa || vehiculo.vin || vehiculo.identificador;
  const [enviando, setEnviando] = useState(false);
  const [pidiendoTipo, setPidiendoTipo] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [mensaje, setMensaje] = useState<Mensaje | null>(null);
  // Tras crear (o tras el 409), el foco pasa al enlace en cuanto la fila trae el trámite.
  const enfocarEnlace = useRef(false);
  const botonRef = useRef<HTMLButtonElement>(null);
  const enlaceRef = useRef<HTMLAnchorElement>(null);

  const tramite = derivacion.tramite;
  useEffect(() => {
    // El botón pulsado desaparece al crear el borrador: el foco pasa al enlace que lo reemplaza.
    if (enfocarEnlace.current && tramite) {
      enlaceRef.current?.focus();
      enfocarEnlace.current = false;
    }
  }, [tramite]);

  /** Crea el borrador; con `pestana`, lo abre ahí al responder y la cierra si no se pudo crear. */
  async function crear(tipo?: string, pestana: Window | null = null) {
    if (enviando) {
      pestana?.close();
      return;
    }
    setEnviando(true);
    setMensaje(null);
    try {
      const resultado = await crearTramite(diagnosticoId, vehiculo.id, tipo);
      if (pestana) pestana.location.href = urlTramite(tramitesUrl, resultado.tramite, empresaId);
      const numero = numeroVisible(resultado.tramite);
      setMensaje({
        tono: "info",
        texto: `${unir(resultado.ya_existia ? "La placa ya tenía el trámite" : "Se creó el borrador del trámite", numero)}.`,
      });
      enfocarEnlace.current = true;
      onCreado(vehiculo.id, resultado.tramite);
    } catch (e) {
      pestana?.close();
      if (!tipo && pideTipo(e)) {
        setPidiendoTipo(true);
        return;
      }
      const conflicto = e instanceof ApiError && e.status === 409;
      setMensaje({ tono: conflicto ? "info" : "error", texto: mensajeCrearTramite(e) });
      if (conflicto) {
        enfocarEnlace.current = true;
        onRefrescar();
      }
    } finally {
      setEnviando(false);
    }
  }

  // HU #14562 AC37: Simple pide el tipo y sigue con «Ir al wizard»; Con análisis IA confirma «Crear traspaso». Los dos
  // abren el borrador en una pestaña nueva. Sin modo (API anterior), crea directo y pide el tipo solo si la API lo exige.
  function alPulsar() {
    if (modo === "SIMPLE") setPidiendoTipo(true);
    else if (modo === "CON_ANALISIS") setConfirmando(true);
    else void crear();
  }

  // HU #14562 F-05: el atajo del panel del dictamen abre el mismo flujo de la fila (modales AC37). El foco que quedó en el
  // botón del dictamen lo toma el modal; al cancelarlo, vuelve al botón «Crear trámite» de la fila.
  const atenderSolicitud = useEffectEvent(() => {
    if (derivacion.elegible && !tramite && !enviando && !pidiendoTipo && !confirmando) alPulsar();
  });
  // El valor con que monta la fila ya está atendido: si la fila se vuelve a montar (filtro, búsqueda) no reabre el modal.
  const solicitudAtendida = useRef(solicitudCrear);
  useEffect(() => {
    if (!solicitudCrear || solicitudCrear === solicitudAtendida.current) return;
    solicitudAtendida.current = solicitudCrear;
    atenderSolicitud();
  }, [solicitudCrear]);

  function cerrarModal() {
    setPidiendoTipo(false);
    setConfirmando(false);
    botonRef.current?.focus();
  }

  function confirmarTipo(codigo: string) {
    setPidiendoTipo(false);
    void crear(codigo, abrirPestana());
  }

  function confirmarTramite() {
    setConfirmando(false);
    void crear(undefined, abrirPestana());
  }

  let accion = null;
  if (tramite) {
    const numero = numeroVisible(tramite);
    accion = (
      <a
        ref={enlaceRef}
        href={urlTramite(tramitesUrl, tramite, empresaId)}
        target="_blank"
        rel="noopener noreferrer"
        aria-label={`${unir("Ver trámite", numero)} de la placa ${identificador} (se abre en una pestaña nueva)`}
        className={ENLACE_CLS}
      >
        <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
        {unir("Ver trámite", numero)}
      </a>
    );
  } else if (derivacion.elegible) {
    accion = (
      <button
        ref={botonRef}
        type="button"
        aria-label={`Crear trámite para la placa ${identificador}`}
        aria-busy={enviando}
        disabled={enviando}
        onClick={alPulsar}
        className={FILA_CLS}
      >
        <FilePlus2 className="h-3.5 w-3.5" aria-hidden="true" />
        {enviando ? "Creando…" : "Crear trámite"}
      </button>
    );
  } else if (derivacion.conocida) {
    accion = (
      <p className="max-w-[12rem] text-xs text-muted-foreground">
        <span className="sr-only">{`No se puede crear trámite para la placa ${identificador}: `}</span>
        {rotuloMotivo(derivacion.motivo)}
      </p>
    );
  }

  if (!accion && !mensaje) return null;

  return (
    <div className="flex flex-col items-start gap-1">
      {accion}
      {mensaje && (
        <p
          role={mensaje.tono === "error" ? "alert" : "status"}
          aria-label={`Resultado de crear trámite para la placa ${identificador}`}
          className="max-w-[14rem] text-xs font-semibold"
          style={{ color: mensaje.tono === "error" ? "var(--badge-danger-fg)" : "var(--badge-success-fg)" }}
        >
          {mensaje.texto}
        </p>
      )}
      {pidiendoTipo && (
        <ModalTipoTramite
          identificador={identificador}
          titulo={`Crear trámite para ${identificador}`}
          ayuda="Abriremos el wizard de Trámites en una pestaña nueva con el borrador, para que lo completes allí."
          textoConfirmar="Ir al wizard"
          abreEnPestana
          onClose={cerrarModal}
          onConfirmar={confirmarTipo}
        />
      )}
      {confirmando && (
        <ModalConfirmarTramite identificador={identificador} tramite={tramiteAnalizado} onClose={cerrarModal} onConfirmar={confirmarTramite} />
      )}
    </div>
  );
}
