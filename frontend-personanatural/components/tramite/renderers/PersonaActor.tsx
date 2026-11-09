"use client";

import { useMutation } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { MobilityMini } from "@/components/MobilityLoader";

/** Texto del botón mientras se envía una solicitud de validación: el carrito gira junto al mensaje. */
const EnviandoConCarro = () => (
  <span className="inline-flex items-center gap-2">
    <MobilityMini />
    Enviando...
  </span>
);
import { useEffect, useRef, useState } from "react";
import { AddressField } from "@/components/tramite/AddressField";
import { AlertaIdentidadValidada, nombrePropio } from "@/components/tramite/AlertaIdentidadValidada";
import { Badge, Btn, Field } from "@/components/tramite/piezas";
import type { ActorRol, ClientePortal, EstadoValidacion, ParteIdentidad, PersonaDetalle } from "@/lib/api/cliente";
import { ApiError, esperaDe, mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import {
  enCurso,
  documentoLegible,
  ETIQUETA_VALIDACION,
  MENSAJE_CELULAR,
  MENSAJE_CONSULTAR,
  soloDigitos,
  TIPOS_DOCUMENTO,
  TONO_VALIDACION,
  validarFila,
  type CampoActor,
  type ErroresActor,
  type FilaActor,
  type TipoDocumento,
} from "@/lib/tramite/actores";
import { ciudadCanonica, filtrarCiudades } from "@/lib/catalogos/ciudades";
import { consentimientoDeConsulta } from "@/lib/tramite/diagnostico";
import { formatearFecha } from "@/lib/formato";
import { erroresRadicantePunto } from "@/lib/punto/contacto";

interface Props {
  fila: FilaActor;
  rol: ActorRol;
  /** Título de la persona dentro del rol cuando el rol admite varias («Copropietario 2»); `undefined` con una sola. */
  titulo?: string;
  /**
   * La primera persona del trámite (vendedor o titular): una vez consultada se pinta como la ficha del propietario
   * del diseño (nombre, documento junto y teléfono en la misma fila).
   */
  esPropietario?: boolean;
  /** El documento vino del primer paso (cédula del inicio): se muestra precargado y no se puede editar. */
  documentoFijo?: boolean;
  /**
   * Portal web: la validación se envía sola al pasar de Actores, así que no hay botón para el primer envío. Si ese envío
   * falló (`fallaEnvio`) se muestra el motivo y un botón de alerta para reintentarlo.
   */
  envioAutomatico?: boolean;
  fallaEnvio?: string | undefined;
  solicitudId: string;
  cliente: ClientePortal;
  /** El ciudadano intentó avanzar con el paso incompleto: se muestran todos los errores. */
  mostrarErrores: boolean;
  erroresServidor?: ErroresActor | undefined;
  estado: EstadoValidacion;
  parte?: ParteIdentidad | undefined;
  /** Cambia cada vez que llega un estado nuevo de la identidad (para reiniciar la espera que informa el back). */
  versionEstado: number;
  /** Muestra el ícono de ayuda de la dirección de entrega (cuando la definición lo marque). */
  direccionEntrega?: boolean;
  onCambio: (patch: Partial<FilaActor>) => void;
  onQuitar?: (() => void) | undefined;
  /** Guarda a todas las personas del rol en el back (el actor debe existir allá antes de pedir su validación). */
  guardarRol: () => Promise<void>;
  /** Vuelve a leer el estado de la identidad. */
  refrescarIdentidad: () => void;
  reportarError: (error: unknown) => void;
  /**
   * Canal del punto u otros flujos: sustituye `cliente.identidad.solicitar/reenviar`.
   * Si devuelve `capturaUrl`, se abre en una pestaña (validación in-situ).
   */
  onValidarIdentidad?: (accion: "solicitar" | "reenviar") => Promise<{ estado: string; capturaUrl?: string | null }>;
  /** Etiqueta del botón primario de validación (default: «Reenviar Validación»). */
  textoBotonValidar?: string;
  /** Si false, el botón de validación queda deshabilitado (p. ej. invitaciones gated). */
  puedeValidarIdentidad?: boolean;
  /** Canal del punto, quien radica: su identidad se lanza sola in-situ (sin botones ni reenvíos) y correo/celular son opcionales. */
  radicantePunto?: boolean;
  /** Portal web con varias personas: al aprobarse su identidad, la tarjeta muestra la alerta «validó su identidad». */
  confirmarIdentidad?: boolean;
  /** El paso está a la vista: un paso oculto no consulta el documento por su cuenta (cada consulta cuesta una llamada al proveedor). */
  consultaActiva?: boolean;
}

const SEGUNDOS_ENTRE_REENVIOS = 60;
/** Espera tras la última tecla antes de consultar sola el documento que se está escribiendo. */
export const ESPERA_CONSULTA_AUTOMATICA_MS = 500;
/** Cuántas veces se reintenta sola la consulta cuando el proveedor pide esperar, y la espera más larga que se acepta para hacerlo. */
export const MAX_REINTENTOS_AUTOMATICOS = 3;
const ESPERA_MAX_REINTENTO_S = 120;

interface PeticionConsulta {
  tipoDocumento: TipoDocumento;
  documento: string;
  signal: AbortSignal;
}
const AVISO_ENLACE_MS = 8000;

/** Una persona (actor) dentro de la tarjeta de su rol: documento y consulta, contacto, dirección y validación de identidad. */
export function PersonaActor({
  fila,
  rol,
  titulo,
  esPropietario = false,
  documentoFijo = false,
  envioAutomatico = false,
  fallaEnvio,
  solicitudId,
  cliente,
  mostrarErrores,
  erroresServidor,
  estado,
  parte,
  versionEstado,
  direccionEntrega = false,
  onCambio,
  onQuitar,
  guardarRol,
  refrescarIdentidad,
  reportarError,
  onValidarIdentidad,
  textoBotonValidar,
  puedeValidarIdentidad = true,
  radicantePunto = false,
  confirmarIdentidad = false,
  consultaActiva = true,
}: Props) {
  const [tocados, setTocados] = useState<Partial<Record<CampoActor, boolean>>>({});
  const [errorConsulta, setErrorConsulta] = useState<string | null>(null);
  /** Aviso bajo los campos cuando el nombre se escribe a mano porque la consulta no lo trajo (no es un error). */
  const [avisoManual, setAvisoManual] = useState<string | null>(null);
  const [aviso, setAviso] = useState<{ tipo: "ok" | "error"; texto: string } | null>(null);
  const [errorContacto, setErrorContacto] = useState<Partial<Record<"correo" | "celular", string>>>({});
  const [ocupado, setOcupado] = useState(false);
  const bloqueoConsulta = useBloqueo();
  const esperaReenvio = useBloqueo();
  const idCampo = (n: string) => `${fila.clave}-${n}`;

  const erroresBase = validarFila(fila, rol);
  const errores = radicantePunto ? erroresRadicantePunto(erroresBase, fila.correo) : erroresBase;
  const juridica = !rol.personaNaturalPermitida || fila.tipoDocumento === "NIT";
  const error = (campo: CampoActor): string | undefined => {
    const delServidor = erroresServidor?.[campo] ?? (campo === "correo" || campo === "celular" ? errorContacto[campo] : undefined);
    if (delServidor) return delServidor;
    const e = errores[campo];
    if (!e) return undefined;
    if (campo === "correo" && radicantePunto) return e; // el dato dañado se señala de inmediato
    if (campo === "numeroDocumento") {
      if (juridica) return e; // el aviso de persona jurídica sale de inmediato
      if (e === MENSAJE_CONSULTAR) return mostrarErrores ? e : undefined;
    }
    return mostrarErrores || tocados[campo] ? e : undefined;
  };
  const tocar = (campo: CampoActor) => setTocados((t) => ({ ...t, [campo]: true }));

  // ── Consulta del documento ──────────────────────────────────────────────────────────────────────────────────────────

  // La consulta lleva el rol de la tarjeta y la sesión de la solicitud (sin captcha); devuelve los nombres para rellenar al actor.
  // Se dispara sola (sin botón): al llegar con el documento diligenciado y al terminar de escribir uno válido.
  const filaActual = useRef(fila);
  filaActual.current = fila;
  const reintentos = useRef(0);
  const reintentoPendiente = useRef(false);
  /** La consulta en curso solo completa las etiquetas RUNT/licencia/multas de una persona ya guardada (no toca sus datos). */
  const soloCompletarRunt = useRef(false);
  /** La espera que se muestra es la de un reintento automático (cambia el texto: ya no hay nada que pulsar). */
  const [reintentoAuto, setReintentoAuto] = useState(false);
  /** El último error se puede resolver volviendo a consultar a mano (no es una espera automática ni un tope de consultas). */
  const [errorReintentable, setErrorReintentable] = useState(false);
  /** La consulta en curso la pidió el botón «Volver a consultar»: si falla no programa reintento automático. */
  const consultaManual = useRef(false);
  const consulta = useMutation<PersonaDetalle, unknown, PeticionConsulta>({
    mutationFn: ({ tipoDocumento, documento, signal }) =>
      cliente.consultas.personaDetalle(solicitudId, { tipoDocumento, documento, rol: fila.rol, ...consentimientoDeConsulta }, { signal }),
    onSuccess: (r, { signal }) => {
      if (signal.aborted) return; // el documento cambió mientras consultaba: esta respuesta ya no aplica
      setErrorConsulta(null);
      setErrorReintentable(false);
      setReintentoAuto(false);
      reintentos.current = 0;
      const nombre = (r.existe ? (r.nombreCompleto ?? [r.nombres, r.apellidos].filter(Boolean).join(" ")) : "").trim();
      // No se pisa una ciudad que la persona ya había escrito.
      const ciudad = r.ciudad && !filaActual.current.ciudad ? ciudadCanonica(r.ciudad) ?? "" : "";
      const licencia =
        r.existe && (r.estadoLicencia || r.vigenciaLicencia)
          ? { estado: r.estadoLicencia ?? null, vigencia: r.vigenciaLicencia ?? null }
          : null;
      const runt = {
        inscrito: r.existe,
        multasPendientes: r.existe ? (r.tieneMultasPendientes ?? null) : null,
        nroPazYSalvo: r.nroPazYSalvo ?? null,
        comparendos: r.comparendos ?? null,
      };
      if (nombre) {
        setAvisoManual(null);
        // Una persona ya guardada con su nombre (borrador reabierto) conserva lo que tenía: la consulta solo completa el RUNT.
        const conservaNombre = filaActual.current.consultado && filaActual.current.nombre.trim() !== "" && filaActual.current.runt?.inscrito !== false;
        onCambio({ consultado: true, ...(conservaNombre ? {} : { nombreConsultado: true, nombre }), licencia, runt, ...(ciudad ? { ciudad } : {}) });
      } else if (soloCompletarRunt.current) {
        // Borrador reabierto: lo guardado no se toca; la etiqueta de RUNT refleja lo que dijo la consulta.
        onCambio({ licencia, runt });
      } else {
        // Sin poder corregir el documento (titular o precargado) lo único posible es volver a consultar.
        setAvisoManual(
          esPropietario || documentoFijo
            ? "No se encontró en RUNT. Vuelve a consultar; sin inscripción RUNT no puedes continuar con esta persona."
            : "No se encontró en RUNT. Corrige el documento e intenta de nuevo; sin inscripción RUNT no puedes continuar con esta persona.",
        );
        onCambio({ consultado: true, nombreConsultado: false, nombre: "", licencia: null, runt, ...(ciudad ? { ciudad } : {}) });
      }
    },
    onError: (e, { signal }) => {
      if (signal.aborted) return;
      reportarError(e);
      if (soloCompletarRunt.current) {
        // Completar las etiquetas es opcional: si falla, se conservan los datos y no se bloquea nada.
        return;
      }
      if (e instanceof ApiError && e.code === "detalle_agotado") {
        // Sin consultas disponibles: se sigue a mano, como cuando no hay coincidencia.
        setAvisoManual(mensajeDe(e));
        setErrorReintentable(false);
        onCambio({ consultado: true, nombreConsultado: false, nombre: "", licencia: null, runt: null });
        return;
      }
      const espera = esperaDe(e);
      const bloqueo = bloqueoConsulta.registrar(e);
      // El proveedor pide esperar: al terminar la cuenta se vuelve a consultar sola (con tope); después, a mano.
      const auto = !consultaManual.current && bloqueo && espera !== undefined && espera <= ESPERA_MAX_REINTENTO_S && reintentos.current < MAX_REINTENTOS_AUTOMATICOS;
      if (auto) {
        reintentos.current += 1;
        reintentoPendiente.current = true;
      }
      setReintentoAuto(auto);
      // Con las consultas pausadas, volver a consultar no serviría (el cupo agotado ya salió arriba).
      setErrorReintentable(!auto && !(e instanceof ApiError && e.code === "consultas_no_disponibles"));
      setErrorConsulta(auto ? mensajeDe(e).replace(/\s*Inténtalo de nuevo\.?/i, "").trim() : mensajeDe(e));
    },
  });

  // ── Validación de identidad ─────────────────────────────────────────────────────────────────────────────────────────

  // La espera entre reenvíos que informa el back se toma una sola vez por cada estado nuevo que llega.
  const versionAtendida = useRef(-1);
  const iniciarEspera = esperaReenvio.iniciar;
  useEffect(() => {
    if (versionAtendida.current === versionEstado) return;
    versionAtendida.current = versionEstado;
    if (parte && parte.esperaSegundos > 0 && enCurso(parte.estado)) iniciarEspera(parte.esperaSegundos);
  }, [versionEstado, parte, iniciarEspera]);

  const temporizadorAviso = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (temporizadorAviso.current) clearTimeout(temporizadorAviso.current);
    },
    [],
  );

  const avisar = (a: { tipo: "ok" | "error"; texto: string }) => {
    setAviso(a);
    if (temporizadorAviso.current) clearTimeout(temporizadorAviso.current);
    if (a.tipo === "ok") temporizadorAviso.current = setTimeout(() => setAviso(null), AVISO_ENLACE_MS);
  };

  const pedirIdentidad = async (accion: "solicitar" | "reenviar") => {
    const e = validarFila(fila, rol);
    if (Object.keys(e).length > 0) {
      setTocados({ numeroDocumento: true, nombre: true, correo: true, celular: true, direccion: true });
      avisar({ tipo: "error", texto: "Completa y corrige los datos de esta persona antes de validar su identidad." });
      return;
    }
    // Regla FLIT 2: hay que consultar RUNT (inscrito) antes de validar identidad.
    if (!fila.runt?.inscrito) {
      avisar({
        tipo: "error",
        texto: fila.runt
          ? "Sin inscripción en RUNT no puedes validar la identidad de esta persona. Corrige el documento y vuelve a consultar."
          : "Consulta primero esta persona en el RUNT antes de enviar la validación.",
      });
      return;
    }
    setOcupado(true);
    setAviso(null);
    try {
      await guardarRol();
      const resultado = onValidarIdentidad
        ? await onValidarIdentidad(accion)
        : await cliente.identidad[accion](solicitudId, fila.rol, fila.ordinal);
      refrescarIdentidad();
      if (accion === "reenviar") esperaReenvio.iniciar(SEGUNDOS_ENTRE_REENVIOS);
      if (resultado.capturaUrl) {
        window.open(resultado.capturaUrl, "_blank", "noopener,noreferrer");
      }
      avisar(
        resultado.estado === "pendiente_envio"
          ? { tipo: "ok", texto: "Aún no pudimos enviar el enlace: lo intentaremos de nuevo automáticamente." }
          : resultado.capturaUrl
            ? { tipo: "ok", texto: "Abre la ventana de validación y completa tu identidad en el punto." }
            : { tipo: "ok", texto: `Enlace enviado a ${fila.correo}` },
      );
    } catch (err) {
      reportarError(err);
      refrescarIdentidad();
      if (esperaDe(err)) esperaReenvio.registrar(err);
      if (err instanceof ApiError && err.code === "contacto_invalido" && (err.campo === "celular" || err.campo === "correo")) {
        setTocados((t) => ({ ...t, [err.campo!]: true }));
        setErrorContacto({ [err.campo]: err.campo === "celular" ? MENSAJE_CELULAR : "Ingresa un correo electrónico válido" });
      }
      avisar({ tipo: "error", texto: textoDeError(err) });
    } finally {
      setOcupado(false);
    }
  };

  // ── Presentación ────────────────────────────────────────────────────────────────────────────────────────────────────

  const identidadAprobada = estado === "aprobada";
  const identidadAbierta = identidadAprobada || enCurso(estado);
  // Solo la identidad ya aprobada sella el documento; con pendiente/sin consultar RUNT se puede corregir.
  const documentoBloqueado = fila.consultado && identidadAprobada;
  const nombreMostrado = fila.consultado ? fila.nombre : "";
  const pideIdentidad = rol.biometria;
  const vence = formatearFecha(fila.licencia?.vigencia);
  const etiquetaLicencia = fila.licencia?.estado
    ? `Licencia ${fila.licencia.estado.toLowerCase()}${fila.licencia.vigencia ? ` · ${fila.licencia.vigencia === "vigente" ? "vigente" : "no vigente"}${vence ? ` · vence ${vence}` : ""}` : ""}`
    : fila.licencia?.vigencia
      ? `Licencia ${fila.licencia.vigencia === "vigente" ? "vigente" : "no vigente"}`
      : null;
  // Con 0 comparendos no hay pendientes aunque el indicador diga lo contrario; sin número de paz y salvo no se inventa uno.
  const multasPendientes = fila.runt?.multasPendientes === true && fila.runt.comparendos !== 0;
  const etiquetaMultas =
    fila.runt?.inscrito && fila.runt.multasPendientes != null
      ? multasPendientes
        ? `Multas: pendientes${fila.runt.comparendos != null ? ` (${fila.runt.comparendos})` : ""}`
        : fila.runt.nroPazYSalvo
          ? `Multas: paz y salvo · ${fila.runt.nroPazYSalvo}`
          : "Multas: sin pendientes"
      : null;
  const reenviosAgotados = parte !== undefined && enCurso(estado) && parte.reenviosRestantes === 0;
  // «Cambiar documento» si aún no está aprobada (incluye pendiente de invitación sin RUNT).
  const puedeCambiarDocumento = fila.consultado && !identidadAprobada && !esPropietario;
  // Mostrar Consultar si no hay ficha o si el borrador trae datos guardados sin haber pasado por RUNT.
  const faltaConsultaRunt = fila.consultado && fila.runt === null && !identidadAprobada && !esPropietario;

  // ── Consulta automática del documento ───────────────────────────────────────────────────────────────────────────────
  const claveDoc = `${fila.tipoDocumento}|${fila.numeroDocumento}`;
  const formatoValido = !errores.numeroDocumento || errores.numeroDocumento === MENSAJE_CONSULTAR;
  // Persona ya guardada con su nombre (identidad aprobada o titular) a la que le faltan las etiquetas RUNT: se completan sin tocar lo guardado.
  const faltaCompletarRunt =
    fila.consultado && fila.runt === null && fila.nombre.trim() !== "" && (identidadAprobada || esPropietario) && fila.numeroDocumento.trim() !== "";
  const consultable =
    consultaActiva &&
    formatoValido &&
    !juridica &&
    ((!identidadAprobada && (!fila.consultado || faltaConsultaRunt)) || faltaCompletarRunt) &&
    !bloqueoConsulta.bloqueado;
  const claveVista = useRef<string | null>(null);
  const claveConsultada = useRef<string | null>(null);
  const controlador = useRef<AbortController | null>(null);
  const montado = useRef(false);
  const consultar = consulta.mutate;
  useEffect(() => {
    montado.current = true;
    return () => {
      montado.current = false;
      controlador.current?.abort();
    };
  }, []);
  useEffect(() => {
    if (claveVista.current !== claveDoc) {
      // Documento distinto: lo que iba en vuelo ya no sirve y el nuevo vuelve a poder consultarse.
      claveVista.current = claveDoc;
      claveConsultada.current = null;
      reintentos.current = 0;
      reintentoPendiente.current = false;
      controlador.current?.abort();
      controlador.current = null;
    }
    if (consultable && reintentoPendiente.current) {
      // Terminó la espera que pidió el proveedor: se permite consultar de nuevo el mismo documento.
      reintentoPendiente.current = false;
      claveConsultada.current = null;
    }
    if (!consultable || claveConsultada.current === claveDoc) return;
    const tipoDocumento = fila.tipoDocumento;
    const documento = fila.numeroDocumento;
    const completar = faltaCompletarRunt;
    const temporizador = setTimeout(() => {
      claveConsultada.current = claveDoc;
      const c = new AbortController();
      controlador.current = c;
      soloCompletarRunt.current = completar;
      consultaManual.current = false;
      setErrorConsulta(null);
      consultar({ tipoDocumento, documento, signal: c.signal });
    }, montado.current ? ESPERA_CONSULTA_AUTOMATICA_MS : 0);
    return () => clearTimeout(temporizador);
  }, [claveDoc, consultable, faltaCompletarRunt, consultar, fila.tipoDocumento, fila.numeroDocumento]);
  const fichaPropietario = esPropietario && fila.consultado;
  // «Volver a consultar»: el mismo documento, cuando no se halló en RUNT o la consulta falló y repetirla puede servir.
  const puedeReconsultar =
    consultaActiva &&
    !juridica &&
    formatoValido &&
    !identidadAprobada &&
    !faltaCompletarRunt &&
    ((fila.consultado && fila.runt?.inscrito === false) || (errorConsulta !== null && errorReintentable));
  const reconsultar = () => {
    controlador.current?.abort();
    const c = new AbortController();
    controlador.current = c;
    soloCompletarRunt.current = false;
    consultaManual.current = true;
    claveConsultada.current = claveDoc;
    consultar({ tipoDocumento: fila.tipoDocumento, documento: fila.numeroDocumento, signal: c.signal });
  };
  // Envío automático fallido (o enlace que no salió): botón de alerta para reintentarlo.
  const mostrarFalla = envioAutomatico && Boolean(fallaEnvio) && (estado === "sin_solicitar" || estado === "pendiente_envio");

  return (
    <div role={titulo ? "group" : undefined} aria-label={titulo} className="space-y-4">
      {titulo && (
        <div className="flex items-center justify-between gap-3">
          <p className="font-semibold">{titulo}</p>
          {onQuitar && (
            <Btn tone="secondary" onClick={onQuitar} disabled={identidadAbierta}>
              Quitar {titulo}
            </Btn>
          )}
        </div>
      )}
      <div className="grid gap-4 md:grid-cols-3">
        {fichaPropietario && (
          <>
            <Field label="Nombre completo" error={error("nombre")}>
              <input
                className="field"
                autoComplete="off"
                readOnly
                aria-invalid={error("nombre") ? true : undefined}
                value={nombreMostrado}
                onChange={(e) => {
                  if (!documentoBloqueado) onCambio({ nombre: e.target.value });
                }}
              />
            </Field>
            <Field label="Tipo y Número de Documento">
              <input className="field" readOnly disabled value={documentoLegible(fila.tipoDocumento, fila.numeroDocumento)} />
            </Field>
          </>
        )}
        {!fichaPropietario && (
          <>
        <Field label="Tipo de documento">
          <select
            className="field"
            disabled={documentoFijo || (fila.consultado && !faltaConsultaRunt)}
            value={fila.tipoDocumento}
            onChange={(e) => {
              onCambio({ tipoDocumento: e.target.value as TipoDocumento, runt: null });
              setErrorConsulta(null);
            }}
          >
            {TIPOS_DOCUMENTO.map((t) => (
              <option key={t.valor} value={t.valor}>
                {t.etiqueta}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Número de documento *" error={error("numeroDocumento") ?? errorConsulta ?? undefined}>
          <div className="relative">
            {!documentoFijo && <Search aria-hidden className="absolute left-3 top-1/2 h-5 w-5 -translate-y-1/2 text-primary" />}
            <input
              className={documentoFijo ? "field" : "field pl-10"}
              inputMode="numeric"
              autoComplete="off"
              placeholder="Escribe el documento"
              aria-busy={consulta.isPending || undefined}
              disabled={documentoFijo || (fila.consultado && !faltaConsultaRunt)}
              readOnly={documentoFijo || (fila.consultado && !faltaConsultaRunt) ? true : undefined}
              aria-invalid={error("numeroDocumento") || errorConsulta ? true : undefined}
              value={fila.numeroDocumento}
              onChange={(e) => {
                onCambio({ numeroDocumento: soloDigitos(e.target.value), runt: null });
                setErrorConsulta(null);
              }}
              onBlur={() => tocar("numeroDocumento")}
            />
            {consulta.isPending && (
              <>
                {/* El carro va en un contenedor propio: `.mobility-track` es `position: relative` y pisaría un `absolute` puesto sobre él. */}
                <span aria-hidden className="pointer-events-none absolute right-3 top-1/2 flex -translate-y-1/2 text-primary">
                  <MobilityMini />
                </span>
                <span role="status" className="sr-only">
                  Consultando el documento...
                </span>
              </>
            )}
          </div>
        </Field>
        <div className="flex flex-wrap items-end gap-2">
          {puedeCambiarDocumento && !faltaConsultaRunt && (
            <Btn tone="secondary" onClick={() => {
                setAvisoManual(null);
                onCambio({ consultado: false, licencia: null, runt: null, nombreConsultado: false, ...(fila.nombreConsultado ? { nombre: "" } : {}) });
              }}>
              Cambiar documento
            </Btn>
          )}
        </div>
        <Field label="Nombre completo *" error={error("nombre")}>
          <input
            className="field"
            autoComplete="off"
            disabled={!fila.consultado}
            readOnly={documentoBloqueado || fila.nombreConsultado}
            aria-invalid={error("nombre") ? true : undefined}
            value={nombreMostrado}
            onChange={(e) => onCambio({ nombre: e.target.value })}
            onBlur={() => tocar("nombre")}
          />
        </Field>
          </>
        )}
        <Field label={radicantePunto ? "Teléfono" : "Teléfono *"} error={error("celular")}>
          <input
            className="field"
            inputMode="numeric"
            autoComplete="off"
            disabled={!fila.consultado}
            aria-invalid={error("celular") ? true : undefined}
            value={fila.celular}
            onChange={(e) => {
              setErrorContacto((actual) => (actual.celular ? { ...actual, celular: undefined } : actual));
              onCambio({ celular: soloDigitos(e.target.value) });
            }}
            onBlur={() => tocar("celular")}
          />
        </Field>
        <Field label={radicantePunto ? "Correo electrónico" : "Correo electrónico *"} error={error("correo")}>
          <input
            className="field"
            type="email"
            autoComplete="off"
            disabled={!fila.consultado}
            aria-invalid={error("correo") ? true : undefined}
            value={fila.correo}
            onChange={(e) => {
              setErrorContacto((actual) => (actual.correo ? { ...actual, correo: undefined } : actual));
              onCambio({ correo: e.target.value });
            }}
            onBlur={() => tocar("correo")}
          />
        </Field>
        <CampoCiudad
          id={idCampo("ciudad")}
          value={fila.ciudad}
          disabled={!fila.consultado}
          error={error("ciudad")}
          onChange={(ciudad) => onCambio({ ciudad })}
          onBlur={() => tocar("ciudad")}
        />
        <div className={fichaPropietario ? "md:col-span-2" : "md:col-span-2"}>
          <AddressField
            id={idCampo("direccion")}
            label="Dirección"
            entrega={direccionEntrega}
            disabled={!fila.consultado}
            value={fila.direccion}
            error={error("direccion")}
            onChange={(v) => onCambio({ direccion: v })}
          />
        </div>
      </div>

      {fila.consultado && avisoManual && (
        <p role={fila.runt && !fila.runt.inscrito ? "alert" : "status"} className={`text-xs ${fila.runt && !fila.runt.inscrito ? "text-alert" : ""}`}>
          {avisoManual}
        </p>
      )}
      {bloqueoConsulta.bloqueado && (
        <p role="status" className="text-xs">
          {reintentoAuto ? "Volveremos a consultar" : "Podrás intentarlo de nuevo"} en {bloqueoConsulta.segundosRestantes} s.
        </p>
      )}
      {fichaPropietario && errorConsulta && (
        <p role="alert" className="text-xs text-alert">
          {errorConsulta}
        </p>
      )}
      {puedeReconsultar && (
        <div>
          <Btn tone="secondary" onClick={reconsultar} disabled={bloqueoConsulta.bloqueado || consulta.isPending}>
            {consulta.isPending ? (
              <span className="inline-flex items-center gap-2">
                <MobilityMini />
                Consultando...
              </span>
            ) : (
              "Volver a consultar"
            )}
          </Btn>
        </div>
      )}

      {fila.consultado && (fila.runt || etiquetaLicencia || etiquetaMultas || pideIdentidad) && (
        <div className="flex flex-wrap items-center gap-3" role="status" aria-live="polite">
          {fila.runt && (
            <Badge tone={fila.runt.inscrito ? "success" : "warning"}>
              {fila.runt.inscrito ? "Inscrito en RUNT" : "No inscrito en RUNT"}
            </Badge>
          )}
          {etiquetaLicencia && <Badge tone="info">{etiquetaLicencia}</Badge>}
          {etiquetaMultas && (
            <Badge tone={multasPendientes ? "warning" : "success"}>{etiquetaMultas}</Badge>
          )}
          {pideIdentidad && !radicantePunto && <Badge tone={TONO_VALIDACION(estado)}>Validación: {ETIQUETA_VALIDACION[estado] ?? estado}</Badge>}
          {pideIdentidad && !radicantePunto && estado !== "aprobada" && (
            <span className="ml-auto flex flex-wrap items-center gap-3">
              {enCurso(estado) && parte && !reenviosAgotados && <span className="text-xs font-light text-muted-foreground">Te quedan {parte.reenviosRestantes} reenvíos</span>}
              {enCurso(estado) && esperaReenvio.bloqueado && <span className="text-xs font-light text-muted-foreground">Podrás reenviar en {esperaReenvio.segundosRestantes} s</span>}
              {mostrarFalla ? (
                <Btn
                  tone="alert"
                  onClick={() => void pedirIdentidad(enCurso(estado) ? "reenviar" : "solicitar")}
                  disabled={ocupado || esperaReenvio.bloqueado || !puedeValidarIdentidad}
                >
                  {ocupado ? <EnviandoConCarro /> : "Reenviar validación"}
                </Btn>
              ) : enCurso(estado) ? (
                <Btn
                  onClick={() => void pedirIdentidad("reenviar")}
                  disabled={ocupado || esperaReenvio.bloqueado || reenviosAgotados || !puedeValidarIdentidad || !fila.runt?.inscrito}
                >
                  {ocupado ? <EnviandoConCarro /> : (textoBotonValidar ?? "Reenviar Validación")}
                </Btn>
              ) : envioAutomatico && estado === "sin_solicitar" ? null : (
                <Btn onClick={() => void pedirIdentidad("solicitar")} disabled={ocupado || !puedeValidarIdentidad || !fila.runt?.inscrito}>
                  {ocupado ? <EnviandoConCarro /> : (textoBotonValidar ?? "Reenviar Validación")}
                </Btn>
              )}
            </span>
          )}
        </div>
      )}
      {confirmarIdentidad && pideIdentidad && !radicantePunto && fila.consultado && identidadAprobada && (
        <AlertaIdentidadValidada>
          {nombrePropio(fila.nombre) || "Esta persona"} validó su identidad correctamente.
        </AlertaIdentidadValidada>
      )}
      {mostrarFalla && (
        <p role="alert" className="text-sm text-alert">
          No pudimos enviar la validación de identidad: {fallaEnvio}
        </p>
      )}
      <div aria-live="polite">
        {aviso && (
          <p role={aviso.tipo === "error" ? "alert" : "status"} className={`text-sm ${aviso.tipo === "error" ? "text-alert" : "text-success"}`}>
            {aviso.texto}
          </p>
        )}
      </div>
      {estado === "rechazada" && pideIdentidad && fila.consultado && (
        <p role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
          La validación de identidad de esta persona fue rechazada. Puedes enviarle un enlace nuevo con «Reenviar Validación».
        </p>
      )}
    </div>
  );
}

/** Ciudad del actor. Se elige de la misma lista que FLIT 2. */
function CampoCiudad({ id, value, disabled, error, onChange, onBlur }: { id: string; value: string; disabled: boolean; error?: string; onChange: (valor: string) => void; onBlur: () => void }) {
  const [abierto, setAbierto] = useState(false);
  const sugerencias = filtrarCiudades(value);
  const mostrar = abierto && !disabled && sugerencias.length > 0;
  return (
    <Field label="Ciudad *" error={error}>
      <div className="relative">
        <input
          id={id}
          className="field"
          autoComplete="off"
          disabled={disabled}
          aria-invalid={error ? true : undefined}
          aria-autocomplete="list"
          aria-expanded={mostrar}
          value={value}
          onChange={(e) => {
            onChange(e.target.value);
            setAbierto(true);
          }}
          onFocus={() => setAbierto(true)}
          onBlur={() => {
            window.setTimeout(() => setAbierto(false), 150);
            onBlur();
          }}
        />
        {mostrar && (
          <ul role="listbox" aria-label="Sugerencias de ciudad" className="absolute z-40 mt-1 max-h-48 w-full overflow-auto rounded-[9px] border border-border bg-card shadow">
            {sugerencias.map((ciudad) => (
              <li key={ciudad}>
                <button
                  type="button"
                  role="option"
                  className="w-full px-3 py-2 text-left text-sm hover:bg-muted"
                  onMouseDown={(e) => {
                    e.preventDefault();
                    onChange(ciudad);
                    setAbierto(false);
                  }}
                >
                  {ciudad}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Field>
  );
}

/** Mensaje de un error de identidad; con el código del 429 muestra la espera exacta. */
function textoDeError(error: unknown): string {
  const espera = esperaDe(error);
  if (error instanceof ApiError && error.code === "reenvio_muy_pronto" && espera) return `Espera ${espera} s antes de reenviar el enlace de nuevo.`;
  if (error instanceof ApiError && error.code === "servicio_no_disponible") return "La validación de identidad no está disponible por ahora. Intenta de nuevo en unos minutos.";
  return mensajeDe(error);
}
