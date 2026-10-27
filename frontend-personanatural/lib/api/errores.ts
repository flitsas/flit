// Errores del BFF: application/problem+json (RFC 7807) con un `code` estable. El error tipado guarda SOLO código, título
// y estado HTTP: nunca el cuerpo de la petición ni datos personales, para que pueda mostrarse o reportarse sin riesgo.

export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly title: string,
    readonly status: number,
    /** Segundos que indicó `Retry-After` (429 y 503), si vino. */
    readonly reintentarEnSegundos?: number,
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
};

const MENSAJES_POR_ESTADO: Record<number, string> = {
  400: "Revisa los datos e inténtalo de nuevo.",
  401: "Necesitamos que inicies o reanudes tu trámite para continuar.",
  403: "No tienes acceso a este trámite.",
  404: "No encontramos lo que buscas.",
  409: "Este trámite cambió mientras lo editabas. Recarga e inténtalo de nuevo.",
  422: "Algunos datos no son válidos. Revísalos e inténtalo de nuevo.",
  429: "Demasiados intentos. Vuelve a intentarlo en unos minutos.",
  503: "El servicio no está disponible en este momento. Intenta de nuevo en unos minutos.",
};

export const MENSAJE_GENERICO = "No pudimos completar la acción. Intenta de nuevo en unos minutos.";

export function mensajeDe(error: unknown): string {
  if (!(error instanceof ApiError)) return MENSAJE_GENERICO;
  return MENSAJES_POR_CODIGO[error.code] ?? MENSAJES_POR_ESTADO[error.status] ?? MENSAJE_GENERICO;
}

/** Texto de «vuelve a intentarlo» con el tiempo exacto cuando el back lo indicó. */
export function esperaDe(error: unknown): number | undefined {
  return error instanceof ApiError && (error.status === 429 || error.status === 503) ? error.reintentarEnSegundos : undefined;
}

export async function errorDeRespuesta(respuesta: Response): Promise<ApiError> {
  let code = `http_${respuesta.status}`;
  let title = respuesta.statusText || "Error";
  try {
    const datos = (await respuesta.json()) as { code?: unknown; title?: unknown };
    if (typeof datos.code === "string") code = datos.code;
    if (typeof datos.title === "string") title = datos.title;
  } catch {
    // Cuerpo vacío o que no es JSON: queda el código por estado.
  }
  const espera = Number(respuesta.headers.get("retry-after"));
  return new ApiError(code, title, respuesta.status, Number.isFinite(espera) && espera > 0 ? espera : undefined);
}
