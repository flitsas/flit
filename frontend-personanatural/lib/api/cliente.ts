// Cliente tipado del BFF del portal (contrato: docs/design/PERSONA-NATURAL-arquitectura-y-contratos.md §6.1). Una sola
// puerta de salida: todas las pantallas llaman a estas funciones y ninguna conoce URLs ni cabeceras. El navegador habla
// con el proxy del mismo host (/api/v1/persona-natural), que guarda la sesión del trámite en una cookie HttpOnly.
// Los tipos siguen los DTO reales de core-personanatural (JSON en camelCase).
import { errorDeRespuesta } from "./errores";

export const BASE_BFF = "/api/v1/persona-natural";

// ── Catálogos (GET /organismos y /tipos-tramite) ─────────────────────────────────────────────────────────────────

export interface Organismo {
  id: string;
  nombre: string;
  codigo?: string;
  municipio?: string;
  departamento?: string;
  enlaceTarifas?: string;
}

export interface TipoTramite {
  code: string;
  name: string;
  family: string;
  version: number;
  disponible: boolean;
  motivoNoDisponible?: string;
}

// ── Cotización (POST /cotizaciones y /cotizaciones/enviar-correo) ────────────────────────────────────────────────

export interface CotizacionRequest {
  organismoId: string;
  tipoCodigo: string;
  /** Conceptos adicionales opcionales (máximo 4). */
  adicionales?: string[];
}

/** Una línea de la prefactura: el back ya la calculó (el front no tiene precios) y trae su subtítulo. */
export interface LineaCotizacion {
  codigo: string;
  nombre: string;
  subtitulo: string;
  /** Pesos colombianos enteros. */
  valorCop: number;
}

export interface Cotizacion {
  lineas: LineaCotizacion[];
  totalCop: number;
  versionTarifa: string;
  adicionalesNoAplicados: string[];
}

export interface EnviarCotizacionRequest extends CotizacionRequest {
  correo: string;
}

export interface EnvioCotizacion {
  enviado: boolean;
  /** Verdadero si la misma Idempotency-Key ya se había atendido. */
  repetido: boolean;
}

// ── Consulta de radicado (POST /radicados/consulta) ──────────────────────────────────────────────────────────────

export interface ConsultaRadicadoRequest {
  placa?: string;
  radicado: string;
  cedula: string;
}

export interface MacroEstado {
  /** pendiente_validacion, en_revision_ot, subsanacion o finalizado. */
  codigo: string;
  etiqueta: string;
  indice: number;
}

/** El back envía done, current, rejected y pending; `skipped` está previsto para tipos sin validación de identidad. */
export type EstadoPasoTracking = "done" | "current" | "rejected" | "pending" | "skipped";

export interface PasoTracking {
  clave: string;
  titulo: string;
  estado: EstadoPasoTracking;
  /** ISO 8601; solo viaja si el paso ya ocurrió. */
  fecha: string | null;
}

export interface Subsanacion {
  motivo: string | null;
  accionCorregir: boolean;
}

/** Dato mínimo del trámite: nunca nombres, documentos ni correos de las partes. */
export interface EstadoRadicado {
  radicado: string;
  numeroReferencia: string | null;
  placa: string | null;
  /** Código del tipo de trámite (se traduce a su nombre con el catálogo). */
  tipoTramite: string;
  organismo: string;
  macroEstado: MacroEstado;
  mensaje: string;
  pasos: PasoTracking[];
  subsanacion: Subsanacion | null;
  /** Verdadero si quien consulta es una parte que no es el solicitante principal. */
  soloLectura: boolean;
}

// ── Sesión del trámite (POST /sesiones y /sesiones/reanudar) ─────────────────────────────────────────────────────

export interface IniciarSesionRequest {
  tipoCodigo: string;
  organismoId: string;
  cedula: string;
  placa?: string;
  vin?: string;
  consentimiento: boolean;
  /** Versión del texto de consentimiento que aceptó el ciudadano. */
  consentimientoVersion: string;
}

/** El token de la sesión nunca llega al navegador: el proxy lo guarda en una cookie HttpOnly. */
export interface SesionTramite {
  solicitudId: string;
  codigoPublico: string;
  estado: string;
  tipoTramiteCodigo: string;
  expiraEn: string;
  /** Solo la trae la apertura, no la reanudación. */
  definicionVersion?: number;
}

/** Estado de la solicitud sin ningún dato personal (GET /solicitudes/{id}/estado): lo único que se sondea tras radicar. */
export interface EstadoSolicitud {
  solicitudId: string;
  codigoPublico: string;
  estado: string;
  tipoTramiteCodigo: string;
  actualizadoEn: string;
  /**
   * Número del trámite en FLIT 2 una vez creado. Hoy el BFF no lo incluye en este DTO (solo lo guarda en su espejo interno):
   * el campo es opcional y la pantalla lo muestra solo si llega.
   */
  numeroReferencia?: string | null;
}

/** Algo que falta: código estable, a qué se refiere (clave de campo, rol o documento) y en qué paso de la definición se corrige. */
export interface Pendiente {
  codigo: string;
  referencia: string | null;
  paso: string | null;
}

/** Avance de un paso de la definición, calculado por el back con la definición congelada de la solicitud. */
export interface PasoEstado {
  clave: string;
  titulo: string;
  completo: boolean;
  pendientes: Pendiente[];
}

export interface Adjunto {
  id: string;
  tipo: string;
  nombreArchivo: string;
  mimeType: string;
  tamanoBytes: number;
  sha256: string;
  creadoEn: string;
}

export interface ActorEstado {
  rol: string;
  ordinal: number;
  validacionEstado: string;
  tipoDocumento: string | null;
  numeroDocumento: string | null;
  nombre: string | null;
  correo: string | null;
  celular: string | null;
  direccion: string | null;
}

/** Qué cambió en la definición del tipo mientras el borrador estaba guardado y qué se hizo con los datos. */
export interface DefinicionCambio {
  versionAnterior: number;
  versionNueva: number;
  camposDescartados: string[];
  camposNuevos: string[];
}

/** Resultado de un requisito del diagnóstico: `estado` es cumple, falta o no_aplica. */
export interface Requisito {
  codigo: string;
  descripcion: string;
  estado: string;
}

export interface Diagnostico {
  puedeContinuar: boolean;
  requisitos: Requisito[];
}

/** Lo que el back guarda del diagnóstico en el borrador, con la fecha de la consulta (ISO 8601). */
export interface DiagnosticoGuardado extends Diagnostico {
  fecha: string;
}

export interface DatosSolicitud {
  comercial?: { valor: number | null } | null;
  prenda?: { tiene: boolean; acreedor: string | null; nitAcreedor: string | null } | null;
  requisitos?: { transformaciones: string[]; observaciones: string | null } | null;
  diagnostico?: DiagnosticoGuardado | null;
}

/**
 * El borrador completo (GET /solicitudes/{id} y respuesta de cada escritura). `version` es la que hay que devolver al
 * guardar: si ya cambió, el back responde 409 `conflicto_version`.
 */
export interface Solicitud {
  solicitudId: string;
  codigoPublico: string;
  estado: string;
  tipoTramiteCodigo: string;
  definicionVersion: number;
  definicionHuella: string;
  version: number;
  organismoId: string;
  organismoNombre: string;
  placa: string | null;
  vin: string | null;
  fieldValues: Record<string, string>;
  datos: DatosSolicitud;
  actores: ActorEstado[];
  adjuntos: Adjunto[];
  pasos: PasoEstado[];
  pendientes: Pendiente[];
  completo: boolean;
  definicionCambio: DefinicionCambio | null;
}

export interface ActorRequest {
  rol: string;
  ordinal?: number;
  tipoDocumento: string;
  numeroDocumento: string;
  nombre: string;
  correo: string;
  celular?: string;
  direccion?: string;
}

export interface DiagnosticoRequest {
  propietario?: { tipoDocumento: string; numeroDocumento: string };
}

/** Respuesta 202 de radicar: la solicitud queda en cola (`en_cola`); FLIT 2 la crea de forma asíncrona. */
export interface ResultadoRadicar {
  solicitudId: string;
  codigoPublico: string;
  estado: string;
}

// ── Definición del tipo (GET /tipos-tramite/{code}/definicion) ───────────────────────────────────────────────────

export interface DocumentoRequerido {
  docCode: string;
  nombre: string;
  obligatorio: boolean;
  orden: number;
  instruccionCargue: string;
  condicion: string;
}

export interface CampoDefinicion {
  fieldKey: string;
  etiqueta: string;
  tipo: string;
  obligatorio: boolean;
  opciones: string[];
  validationSchema: string;
}

export interface PasoDefinicion {
  clave: string;
  titulo: string;
  sectionTypes: string[];
  campos: CampoDefinicion[];
}

export interface ActorRol {
  rol: string;
  etiqueta: string;
  min: number;
  max: number;
  biometria: boolean;
  firma: boolean;
  personaNaturalPermitida: boolean;
}

export interface FlagsTipo {
  /** Modo de entrada del vehículo (placa, VIN o ambos), tal como lo informa FLIT 2. */
  entryMode: string;
  requiereComercial: boolean;
  requierePrenda: boolean;
  permiteTransformaciones: boolean;
  permitePrendaComplementaria: boolean;
  requiereBiometria: boolean;
  requiereSolicitudPlaca: boolean;
}

export interface DefinicionTipo {
  code: string;
  name: string;
  family: string;
  version: number;
  huella: string;
  disponible: boolean;
  motivoNoDisponible: string;
  pasos: PasoDefinicion[];
  actores: ActorRol[];
  documentos: DocumentoRequerido[];
  flags: FlagsTipo;
}

// ── Consultas públicas (POST /consultas/vehiculo y /consultas/persona) ───────────────────────────────────────────

export interface DocumentoConsulta {
  tipo: string;
  numero: string;
}

export interface ConsultaVehiculoRequest {
  placa?: string;
  vin?: string;
  propietario?: DocumentoConsulta;
  consentimiento: boolean;
  consentimientoVersion: string;
}

export interface ConsultaPersonaRequest {
  documento: DocumentoConsulta;
  consentimiento: boolean;
  consentimientoVersion: string;
}

export interface VehiculoMinimo {
  existe: boolean;
  placa: string | null;
  clase: string | null;
  marca: string | null;
  linea: string | null;
  modeloAno: string | null;
  tipoServicio: string | null;
  estado: string | null;
  tienePrenda: boolean;
  tieneLimitacion: boolean;
  tieneRestricciones: boolean;
}

/** POST /solicitudes/{id}/consultas/persona-detalle: consulta de una persona con nombres, con la sesión de la solicitud (sin captcha). */
export interface ConsultaPersonaDetalleRequest {
  tipoDocumento: string;
  documento: string;
  /** Rol del actor dentro de la definición del trámite («comprador»). */
  rol: string;
  consentimiento: boolean;
  consentimientoVersion?: string;
}

/** Sin coincidencia: `existe = false` y el resto `null`. Son datos de un tercero: solo viven en memoria de la pantalla. */
export interface PersonaDetalle {
  existe: boolean;
  nombres: string | null;
  apellidos: string | null;
  nombreCompleto: string | null;
  ciudad: string | null;
}

export interface ConductorMinimo {
  existe: boolean;
  estadoLicencia: string | null;
  vigencia: string | null;
}

// ── Identidad por parte (POST/GET /solicitudes/{id}/identidad) ───────────────────────────────────────────────────

/** Estados de la validación de una parte: sin_solicitar, pendiente_envio, pendiente, aprobada, rechazada y vencida. */
export type EstadoValidacion = "sin_solicitar" | "pendiente_envio" | "pendiente" | "aprobada" | "rechazada" | "vencida" | (string & {});

/** Una parte que el tipo obliga a validar, con lo que le queda por reenviar (tope de 5 reenvíos y 60 s entre uno y otro). */
export interface ParteIdentidad {
  /** Rol de la definición. */
  parte: string;
  ordinal: number;
  estado: EstadoValidacion;
  puedeReenviar: boolean;
  reenviosRestantes: number;
  esperaSegundos: number;
}

export interface EstadoIdentidad {
  solicitudId: string;
  /** Todas las partes obligadas tienen la identidad aprobada. */
  completa: boolean;
  partes: ParteIdentidad[];
}

export interface ResultadoParte {
  parte: string;
  ordinal: number;
  estado: EstadoValidacion;
}

// ── Cliente ──────────────────────────────────────────────────────────────────────────────────────────────────────

export interface OpcionesCliente {
  base?: string;
  fetchImpl?: typeof fetch;
}

export interface OpcionesLlamada {
  /** Token del captcha: lo piden los endpoints públicos (X-Captcha-Token). */
  captcha?: string;
  /** Llave de idempotencia: la piden radicar, subsanar y enviar correo. */
  idempotencyKey?: string;
  /** Solo para GET /tipos-tramite y su definición: acota a lo disponible en ese organismo. */
  organismoId?: string;
  signal?: AbortSignal;
}

function consultaOrganismo(o?: OpcionesLlamada): string {
  return o?.organismoId ? `?organismoId=${encodeURIComponent(o.organismoId)}` : "";
}

export function crearCliente({ base = BASE_BFF, fetchImpl }: OpcionesCliente = {}) {
  async function solicitar<T>(metodo: string, ruta: string, cuerpo?: unknown, opciones: OpcionesLlamada = {}): Promise<T> {
    const cabeceras: Record<string, string> = { accept: "application/json" };
    // Un FormData (archivos) viaja tal cual: el navegador pone el content-type con su boundary; a mano se rompería.
    const esFormulario = typeof FormData !== "undefined" && cuerpo instanceof FormData;
    if (cuerpo !== undefined && !esFormulario) cabeceras["content-type"] = "application/json";
    if (opciones.captcha) cabeceras["x-captcha-token"] = opciones.captcha;
    if (opciones.idempotencyKey) cabeceras["idempotency-key"] = opciones.idempotencyKey;

    // Una sola petición: ante un 429 o un 503 NO se reintenta; el error trae `reintentarEnSegundos` y la pantalla espera.
    const respuesta = await (fetchImpl ?? fetch)(`${base}${ruta}`, {
      method: metodo,
      headers: cabeceras,
      body: cuerpo === undefined ? undefined : esFormulario ? (cuerpo as FormData) : JSON.stringify(cuerpo),
      credentials: "same-origin",
      cache: "no-store",
      signal: opciones.signal,
    });
    if (!respuesta.ok) throw await errorDeRespuesta(respuesta);
    return respuesta.status === 204 ? (undefined as T) : ((await respuesta.json()) as T);
  }

  return {
    catalogos: {
      organismos: (o?: OpcionesLlamada) => solicitar<Organismo[]>("GET", "/organismos", undefined, o),
      tiposTramite: (o?: OpcionesLlamada) => solicitar<TipoTramite[]>("GET", `/tipos-tramite${consultaOrganismo(o)}`, undefined, o),
      definicion: (code: string, o?: OpcionesLlamada) =>
        solicitar<DefinicionTipo>("GET", `/tipos-tramite/${encodeURIComponent(code)}/definicion${consultaOrganismo(o)}`, undefined, o),
    },
    cotizaciones: {
      cotizar: (datos: CotizacionRequest, o?: OpcionesLlamada) => solicitar<Cotizacion>("POST", "/cotizaciones", datos, o),
      /** Pide `idempotencyKey` y `captcha` en las opciones. */
      enviarCorreo: (datos: EnviarCotizacionRequest, o?: OpcionesLlamada) => solicitar<EnvioCotizacion>("POST", "/cotizaciones/enviar-correo", datos, o),
    },
    consultas: {
      vehiculo: (datos: ConsultaVehiculoRequest, o?: OpcionesLlamada) => solicitar<VehiculoMinimo>("POST", "/consultas/vehiculo", datos, o),
      persona: (datos: ConsultaPersonaRequest, o?: OpcionesLlamada) => solicitar<ConductorMinimo>("POST", "/consultas/persona", datos, o),
      /** Con la sesión de la solicitud y sin captcha: devuelve los nombres para rellenar al actor. */
      personaDetalle: (solicitudId: string, datos: ConsultaPersonaDetalleRequest, o?: OpcionesLlamada) =>
        solicitar<PersonaDetalle>("POST", `/solicitudes/${encodeURIComponent(solicitudId)}/consultas/persona-detalle`, datos, o),
    },
    radicados: {
      consultar: (datos: ConsultaRadicadoRequest, o?: OpcionesLlamada) => solicitar<EstadoRadicado>("POST", "/radicados/consulta", datos, o),
    },
    sesiones: {
      iniciar: (datos: IniciarSesionRequest, o?: OpcionesLlamada) => solicitar<SesionTramite>("POST", "/sesiones", datos, o),
      reanudar: (datos: { radicado: string; cedula: string }, o?: OpcionesLlamada) =>
        solicitar<SesionTramite>("POST", "/sesiones/reanudar", datos, o),
    },
    solicitudes: {
      obtener: (id: string, o?: OpcionesLlamada) => solicitar<Solicitud>("GET", `/solicitudes/${encodeURIComponent(id)}`, undefined, o),
      estado: (id: string, o?: OpcionesLlamada) => solicitar<EstadoSolicitud>("GET", `/solicitudes/${encodeURIComponent(id)}/estado`, undefined, o),
      /** Preflight de requisitos del tipo (POST /solicitudes/{id}/diagnostico): lo guarda en el borrador y devuelve los resultados. */
      diagnosticar: (id: string, datos?: DiagnosticoRequest, o?: OpcionesLlamada) =>
        solicitar<Diagnostico>("POST", `/solicitudes/${encodeURIComponent(id)}/diagnostico`, datos, o),
      /** Escrituras por paso: cada una lleva la `version` leída y devuelve el borrador completo (409 `conflicto_version` si ya cambió). */
      guardarActores: (id: string, datos: { version: number; actores: ActorRequest[] }, o?: OpcionesLlamada) =>
        solicitar<Solicitud>("PATCH", `/solicitudes/${encodeURIComponent(id)}/actores`, datos, o),
      guardarCampos: (id: string, datos: { version: number; valores: Record<string, string | number | boolean | null> }, o?: OpcionesLlamada) =>
        solicitar<Solicitud>("PUT", `/solicitudes/${encodeURIComponent(id)}/campos`, datos, o),
      guardarComercial: (
        id: string,
        datos: { version: number; valorComercial?: number; prenda?: { tienePrenda: boolean; acreedor?: string; nitAcreedor?: string } },
        o?: OpcionesLlamada,
      ) => solicitar<Solicitud>("PUT", `/solicitudes/${encodeURIComponent(id)}/comercial`, datos, o),
      guardarRequisitos: (id: string, datos: { version: number; transformaciones?: string[]; observaciones?: string }, o?: OpcionesLlamada) =>
        solicitar<Solicitud>("PUT", `/solicitudes/${encodeURIComponent(id)}/requisitos`, datos, o),
      radicar: (id: string, idempotencyKey: string, o?: OpcionesLlamada) =>
        solicitar<ResultadoRadicar>("POST", `/solicitudes/${encodeURIComponent(id)}/radicar`, undefined, { ...o, idempotencyKey }),
      cancelar: (id: string, o?: OpcionesLlamada) => solicitar<void>("DELETE", `/solicitudes/${encodeURIComponent(id)}`, undefined, o),
    },
    adjuntos: {
      /** Sube un archivo (multipart: `archivo` y `tipo` = `docCode` del documento de la definición). Devuelve solo sus metadatos. */
      subir: (id: string, tipo: string, archivo: File, o?: OpcionesLlamada) => {
        const formulario = new FormData();
        formulario.append("tipo", tipo);
        formulario.append("archivo", archivo, archivo.name);
        return solicitar<Adjunto>("POST", `/solicitudes/${encodeURIComponent(id)}/adjuntos`, formulario, o);
      },
      eliminar: (id: string, adjuntoId: string, o?: OpcionesLlamada) =>
        solicitar<void>("DELETE", `/solicitudes/${encodeURIComponent(id)}/adjuntos/${encodeURIComponent(adjuntoId)}`, undefined, o),
    },
    identidad: {
      /** Estado de la validación de cada parte que el tipo obliga a validar (se sondea). */
      estado: (id: string, o?: OpcionesLlamada) => solicitar<EstadoIdentidad>("GET", `/solicitudes/${encodeURIComponent(id)}/identidad`, undefined, o),
      /** Pide la validación de la parte (rol) en la posición `ordinal`; el actor debe estar guardado. 202 = FLIT 2 sin respuesta (`pendiente_envio`). */
      solicitar: (id: string, parte: string, ordinal: number, o?: OpcionesLlamada) =>
        solicitar<ResultadoParte>("POST", `/solicitudes/${encodeURIComponent(id)}/identidad/${encodeURIComponent(parte)}`, { ordinal }, o),
      reenviar: (id: string, parte: string, ordinal: number, o?: OpcionesLlamada) =>
        solicitar<ResultadoParte>("POST", `/solicitudes/${encodeURIComponent(id)}/identidad/${encodeURIComponent(parte)}/reenviar`, { ordinal }, o),
    },
  };
}

export type ClientePortal = ReturnType<typeof crearCliente>;

/** Cliente por defecto del navegador. */
export const portal = crearCliente();
