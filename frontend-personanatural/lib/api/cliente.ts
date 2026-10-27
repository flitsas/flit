// Cliente tipado del BFF del portal (contrato: docs/design/PERSONA-NATURAL-arquitectura-y-contratos.md §6.1). Una sola
// puerta de salida: todas las pantallas llaman a estas funciones y ninguna conoce URLs ni cabeceras. El navegador habla
// con el proxy del mismo host (/api/v1/persona-natural), que guarda la sesión del trámite en una cookie HttpOnly.
import { errorDeRespuesta } from "./errores";

export const BASE_BFF = "/api/v1/persona-natural";

// ── Tipos (se amplían con cada HU que agrega pantalla; los nombres siguen el contrato del BFF) ────────────────────

export interface Organismo {
  id: string;
  nombre: string;
}

export interface TipoTramite {
  code: string;
  name: string;
  family: string;
  version: number;
  disponible: boolean;
  motivoNoDisponible?: string;
}

export interface CotizacionRequest {
  organismoId: string;
  tipoTramite: string;
}

export interface LineaCotizacion {
  concepto: string;
  valor: number;
}

export interface Cotizacion {
  lineas: LineaCotizacion[];
  total: number;
}

export interface ConsultaRadicadoRequest {
  placa?: string;
  radicado: string;
  cedula: string;
}

export interface EstadoRadicado {
  radicado: string;
  estado: string;
  macroEstado: string;
  subsanacion: boolean;
  motivoSubsanacion?: string;
}

export interface IniciarSesionRequest {
  tipoTramite: string;
  organismoId: string;
  cedula: string;
  placa?: string;
  vin?: string;
  consentimiento: boolean;
}

export interface SesionTramite {
  solicitudId: string;
  codigoPublico: string;
  estado: string;
  tipoTramiteCodigo: string;
  expiraEn: string;
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
  signal?: AbortSignal;
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
      tiposTramite: (o?: OpcionesLlamada) => solicitar<TipoTramite[]>("GET", "/tipos-tramite", undefined, o),
    },
    cotizaciones: {
      cotizar: (datos: CotizacionRequest, o?: OpcionesLlamada) => solicitar<Cotizacion>("POST", "/cotizaciones", datos, o),
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
      estado: (id: string, o?: OpcionesLlamada) => solicitar<EstadoRadicado>("GET", `/solicitudes/${encodeURIComponent(id)}/estado`, undefined, o),
      radicar: (id: string, idempotencyKey: string, o?: OpcionesLlamada) =>
        solicitar<ResultadoRadicar>("POST", `/solicitudes/${encodeURIComponent(id)}/radicar`, undefined, { ...o, idempotencyKey }),
      cancelar: (id: string, o?: OpcionesLlamada) => solicitar<void>("DELETE", `/solicitudes/${encodeURIComponent(id)}`, undefined, o),
    },
  };
}

export type ClientePortal = ReturnType<typeof crearCliente>;

/** Cliente por defecto del navegador. */
export const portal = crearCliente();
