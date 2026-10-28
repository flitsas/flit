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

export interface Solicitud {
  solicitudId: string;
  codigoPublico: string;
  estado: string;
  tipoTramiteCodigo: string;
}

export interface ResultadoRadicar {
  solicitudId: string;
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

export interface ConductorMinimo {
  existe: boolean;
  estadoLicencia: string | null;
  vigencia: string | null;
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
    if (cuerpo !== undefined) cabeceras["content-type"] = "application/json";
    if (opciones.captcha) cabeceras["x-captcha-token"] = opciones.captcha;
    if (opciones.idempotencyKey) cabeceras["idempotency-key"] = opciones.idempotencyKey;

    // Una sola petición: ante un 429 o un 503 NO se reintenta; el error trae `reintentarEnSegundos` y la pantalla espera.
    const respuesta = await (fetchImpl ?? fetch)(`${base}${ruta}`, {
      method: metodo,
      headers: cabeceras,
      body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
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
      estado: (id: string, o?: OpcionesLlamada) => solicitar<Solicitud>("GET", `/solicitudes/${encodeURIComponent(id)}/estado`, undefined, o),
      radicar: (id: string, idempotencyKey: string, o?: OpcionesLlamada) =>
        solicitar<ResultadoRadicar>("POST", `/solicitudes/${encodeURIComponent(id)}/radicar`, undefined, { ...o, idempotencyKey }),
      cancelar: (id: string, o?: OpcionesLlamada) => solicitar<void>("DELETE", `/solicitudes/${encodeURIComponent(id)}`, undefined, o),
    },
  };
}

export type ClientePortal = ReturnType<typeof crearCliente>;

/** Cliente por defecto del navegador. */
export const portal = crearCliente();
