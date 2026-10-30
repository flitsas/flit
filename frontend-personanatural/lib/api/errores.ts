// Errores del BFF: application/problem+json (RFC 7807) con un `code` estable. El error tipado guarda SOLO código, título
// y estado HTTP: nunca el cuerpo de la petición ni datos personales, para que pueda mostrarse o reportarse sin riesgo.

/** Parte obligada sin identidad aprobada (409 `identity_pending`): rol, posición y estado de su validación. Nunca datos personales. */
export interface ParteSinIdentidad {
  parte: string;
  ordinal: number;
  estado: string;
}

export interface PendienteDeRadicacion {
  codigo: string;
  referencia: string | null;
  paso: string | null;
}

export interface FallasDeRadicacion {
  pendientes?: PendienteDeRadicacion[];
  /** `identidad_pendiente` o `identidad_rechazada`. */
  motivo?: string;
  partes?: ParteSinIdentidad[];
}

export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly title: string,
    readonly status: number,
    /** Segundos que indicó `Retry-After` (429 y 503), si vino. */
    readonly reintentarEnSegundos?: number,
    /** Errores por campo de un 422 `datos_invalidos` (`actores[0].correo` → mensajes). Son textos fijos del back, nunca el valor escrito. */
    readonly errores?: Readonly<Record<string, readonly string[]>>,
    /** Estado que informa el back en algunos conflictos (`state`), p. ej. el de la validación de identidad. */
    readonly estado?: string,
    /** Lo que informa el back al rechazar radicar: los pendientes (422 `requisitos_incompletos`) o las partes sin identidad (409 `identity_pending`). */
    readonly fallas?: FallasDeRadicacion,
    /** Campo que señala un 422 (`field`), por ejemplo `celular` o `correo`. */
    readonly campo?: string,
  ) {
    super(title);
    this.name = "ApiError";
  }
}

/** Mensaje en español para el ciudadano. Sin detalles técnicos. */
const MENSAJES_POR_CODIGO: Record<string, string> = {
  captcha_required: "Resuelve la verificación de seguridad para continuar.",
  session_expired: "Tu sesión venció. Puedes reanudar el trámite con tu radicado y tu cédula.",
  session_invalid: "Necesitamos que inicies o reanudes tu trámite para continuar.",
  session_scope_mismatch: "Esta sesión no corresponde a ese trámite.",
  reanudar_bloqueado: "Demasiados intentos. Vuelve a intentarlo en unos minutos.",
  solicitud_no_encontrada: "No encontramos un trámite con esos datos.",
  rate_limited: "Demasiados intentos. Vuelve a intentarlo en unos minutos.",
  cedula_rate_limited: "Alcanzaste el máximo de consultas por hora para este documento. Intenta más tarde.",
  consultas_no_disponibles: "Las consultas están pausadas por hoy. Intenta de nuevo mañana.",
  detalle_agotado: "Alcanzaste el máximo de consultas para este trámite; escribe los datos manualmente.",
  rol_invalido: "Este trámite no admite a esta persona en ese rol.",
  tipo_no_habilitado: "Este trámite no está habilitado en el organismo elegido.",
  tarifa_no_vigente: "Este trámite aún no tiene tarifa en el organismo elegido.",
  tipo_no_disponible: "Este tipo de trámite ya no está disponible en el portal.",
  organismo_no_habilitado: "El organismo de tránsito no está habilitado.",
  organismo_requerido: "Elige el organismo de tránsito antes de continuar.",
  consentimiento_requerido: "Acepta el tratamiento de tus datos personales para continuar.",
  consentimiento_version_desconocida: "El texto de consentimiento cambió. Recarga la página y vuelve a aceptarlo.",
  correo_rate_limited: "Ese correo ya recibió varias cotizaciones. Intenta más tarde.",
  correo_invalido: "Escribe un correo válido.",
  envio_fallido: "No pudimos enviar el correo. Intenta de nuevo.",
  persona_juridica_no_soportada: "Este trámite en línea es solo para personas naturales: no admite NIT ni personas jurídicas.",
  contacto_invalido: "Revisa el celular: debe tener 10 dígitos y empezar por 3. Si el correo tampoco es válido, corrígelo.",
  reenvio_muy_pronto: "Espera un momento antes de reenviar el enlace de nuevo.",
  reenvios_agotados: "Ya se reenvió el enlace el máximo de veces.",
  validacion_en_curso: "Ya hay una validación en curso para esta persona.",
  identidad_aprobada: "La identidad de esta persona ya fue validada.",
  validacion_no_solicitada: "Primero solicita la validación de esta persona.",
  actor_no_registrado: "Guarda primero a esta persona en el trámite.",
  rol_sin_validacion: "Este rol no necesita validar su identidad.",
  rol_desconocido: "Ese rol no existe en este trámite.",
  solicitud_no_editable: "Este trámite ya no admite cambios.",
  datos_invalidos: "Revisa los datos marcados e inténtalo de nuevo.",
  adjunto_no_permitido: "Solo se aceptan archivos PDF, JPG, PNG o WEBP de hasta 20 MB.",
  documento_no_pertenece_al_tipo: "Ese documento no hace parte de este trámite.",
  limite_de_adjuntos: "Alcanzaste el máximo de archivos del trámite.",
  almacenamiento_no_disponible: "No pudimos guardar el archivo. Intenta de nuevo.",
  adjunto_no_encontrado: "No encontramos ese archivo.",
  requisitos_incompletos: "Falta información para enviar el trámite.",
  identity_pending: "Todas las personas del trámite deben validar su identidad antes de enviarlo.",
  idempotency_conflicto: "No pudimos procesar el envío. Vuelve a intentarlo.",
  idempotency_key_requerida: "No pudimos procesar el envío. Vuelve a intentarlo.",
  estado_no_permite_cambio: "Este trámite ya fue enviado o cancelado y no admite cambios.",
  proveedor_no_disponible: "No pudimos consultar el RUNT. Inténtalo de nuevo.",
};

const MENSAJES_POR_ESTADO: Record<number, string> = {
  400: "Revisa los datos e inténtalo de nuevo.",
  401: "Necesitamos que inicies o reanudes tu trámite para continuar.",
  403: "No tienes acceso a este trámite.",
  404: "No encontramos lo que buscas.",
  409: "Este trámite cambió mientras lo editabas. Recarga e inténtalo de nuevo.",
  413: "El archivo supera los 20 MB permitidos.",
  415: "Solo se aceptan archivos PDF, JPG, PNG o WEBP.",
  422: "Algunos datos no son válidos. Revísalos e inténtalo de nuevo.",
  429: "Demasiados intentos. Vuelve a intentarlo en unos minutos.",
  503: "El servicio no está disponible en este momento. Intenta de nuevo en unos minutos.",
};

export const MENSAJE_GENERICO = "No pudimos completar la acción. Intenta de nuevo en unos minutos.";

export function mensajeDe(error: unknown): string {
  if (!(error instanceof ApiError)) return MENSAJE_GENERICO;
  return MENSAJES_POR_CODIGO[error.code] ?? MENSAJES_POR_ESTADO[error.status] ?? MENSAJE_GENERICO;
}

/**
 * Mensaje del canal del punto: si el BFF mandó `message` (y no hay texto propio del código), se muestra tal cual.
 * Los códigos conocidos siguen usando su frase fija.
 */
export function mensajeDeFallo(error: unknown): string {
  if (!(error instanceof ApiError)) return MENSAJE_GENERICO;
  if (MENSAJES_POR_CODIGO[error.code]) return MENSAJES_POR_CODIGO[error.code];
  const titulo = error.title.trim();
  if (titulo && titulo !== "Error") return titulo;
  return MENSAJES_POR_ESTADO[error.status] ?? MENSAJE_GENERICO;
}

/** Texto de «vuelve a intentarlo» con el tiempo exacto cuando el back lo indicó. */
export function esperaDe(error: unknown): number | undefined {
  return error instanceof ApiError && (error.status === 429 || error.status === 503) ? error.reintentarEnSegundos : undefined;
}

export async function errorDeRespuesta(respuesta: Response): Promise<ApiError> {
  let code = `http_${respuesta.status}`;
  let title = respuesta.statusText || "Error";
  let errores: Record<string, string[]> | undefined;
  let estado: string | undefined;
  let fallas: FallasDeRadicacion | undefined;
  let campo: string | undefined;
  try {
    const datos = (await respuesta.json()) as { code?: unknown; title?: unknown; message?: unknown; errors?: unknown; state?: unknown; field?: unknown; pendientes?: unknown; motivo?: unknown; partes?: unknown };
    if (typeof datos.code === "string") code = datos.code;
    if (typeof datos.title === "string") title = datos.title;
    else if (typeof datos.message === "string" && datos.message.trim()) title = datos.message.trim();
    if (typeof datos.state === "string") estado = datos.state;
    errores = erroresPorCampo(datos.errors);
    fallas = fallasDe(datos);
    campo = typeof datos.field === "string" ? datos.field : undefined;
  } catch {
    // Cuerpo vacío o que no es JSON: queda el código por estado.
  }
  const espera = Number(respuesta.headers.get("retry-after"));
  return new ApiError(code, title, respuesta.status, Number.isFinite(espera) && espera > 0 ? espera : undefined, errores, estado, fallas, campo);
}

const esTexto = (v: unknown): v is string => typeof v === "string";
const esObjeto = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null;

/** `pendientes`, `motivo` y `partes` de un rechazo de radicación. Solo se aceptan cadenas y números: cualquier otra forma se ignora. */
function fallasDe(datos: { pendientes?: unknown; motivo?: unknown; partes?: unknown }): FallasDeRadicacion | undefined {
  const fallas: FallasDeRadicacion = {};
  if (Array.isArray(datos.pendientes)) {
    fallas.pendientes = datos.pendientes
      .filter((p): p is Record<string, unknown> => esObjeto(p) && esTexto(p.codigo))
      .map((p) => ({ codigo: p.codigo as string, referencia: esTexto(p.referencia) ? p.referencia : null, paso: esTexto(p.paso) ? p.paso : null }));
  }
  if (esTexto(datos.motivo)) fallas.motivo = datos.motivo;
  if (Array.isArray(datos.partes)) {
    fallas.partes = datos.partes
      .filter((p): p is Record<string, unknown> => esObjeto(p) && esTexto(p.parte))
      .map((p) => ({ parte: p.parte as string, ordinal: typeof p.ordinal === "number" ? p.ordinal : 1, estado: esTexto(p.estado) ? p.estado : "sin_solicitar" }));
  }
  return Object.keys(fallas).length > 0 ? fallas : undefined;
}

/** `errors` de un 422: campo → mensajes (cadenas). Cualquier otra forma se ignora. */
function erroresPorCampo(valor: unknown): Record<string, string[]> | undefined {
  if (typeof valor !== "object" || valor === null || Array.isArray(valor)) return undefined;
  const salida: Record<string, string[]> = {};
  for (const [campo, mensajes] of Object.entries(valor)) {
    if (Array.isArray(mensajes)) salida[campo] = mensajes.filter((m): m is string => typeof m === "string");
  }
  return Object.keys(salida).length > 0 ? salida : undefined;
}
