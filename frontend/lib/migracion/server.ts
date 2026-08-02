// BFF de la consola de migración. SOLO servidor: este módulo lee la llave del host de migración
// del entorno, y en un componente de cliente eso terminaría horneado en el JS que descarga el
// navegador.
//
// Es la razón de que la consola tenga un BFF y no llame al gateway directamente como el resto del
// frontend: `X-Migration-Key` no es un token de sesión del usuario, es la llave MAESTRA del host
// que reescribe trámites, sin tenant y sin dueño. Si llega al navegador, cualquiera con las
// herramientas de desarrollo abiertas puede migrar lo que quiera desde su casa.
import "server-only";

import { cookies } from "next/headers";
import { decodeJwtPayload, isSuperAdmin, TOKEN_COOKIE } from "@/lib/auth/jwt";

/**
 * El host de migración, por la red interna de Docker (`http://gateway:4002`). NO se pasa por el
 * dominio público: ese salto atraviesa nginx, cuyo `proxy_read_timeout` de 60 s corta migraciones
 * que sí se completan.
 *
 * Se lee en cada llamada y no en una constante de módulo: así encender el migrador en un ambiente
 * y reiniciar el contenedor basta, sin depender de en qué momento se evaluó este módulo.
 */
function upstream(): string {
  return process.env.MIGRACION_API_URL ?? "http://localhost:4002";
}

/** Sin `NEXT_PUBLIC_`, deliberadamente: ver la cabecera de este archivo. */
function apiKey(): string {
  return process.env.MIGRACION_API_KEY ?? "";
}

/** Respuesta ya lista para devolver desde un route handler. */
export interface RespuestaBff {
  cuerpo: unknown;
  estado: number;
}

function problema(estado: number, titulo: string, detalle: string): RespuestaBff {
  return { estado, cuerpo: { title: titulo, detail: detalle, status: estado } };
}

/**
 * Exige SuperAdmin. Devuelve `null` si puede pasar, o la respuesta de rechazo si no.
 *
 * El middleware NO cubre estas rutas: su `matcher` es `/admin/:path*`, y estos handlers viven bajo
 * `/api/migracion/*`. Ampliar el matcher sería frágil —la protección real quedaría a un `matcher`
 * de distancia—, así que cada handler pide el permiso explícitamente.
 *
 * Y sí, el JWT se decodifica sin verificar la firma, igual que en el resto del frontend. No es la
 * única cerradura: la llave del host de migración nunca sale de este proceso, así que forjar un
 * token solo abre el proxy, no el migrador. Aun así es una puerta que conviene cerrar.
 */
export async function exigirSuperAdmin(): Promise<RespuestaBff | null> {
  const token = (await cookies()).get(TOKEN_COOKIE)?.value;
  const payload = decodeJwtPayload(token);

  if (!payload || !isSuperAdmin(payload)) {
    return problema(
      403,
      "migracion.no_autorizado",
      "La consola de migración es exclusiva de SuperAdmin.",
    );
  }

  if (typeof payload.exp === "number" && payload.exp * 1000 <= Date.now()) {
    return problema(401, "migracion.sesion_expirada", "Tu sesión expiró; vuelve a entrar.");
  }

  return null;
}

/**
 * Llama al host de migración añadiendo la llave.
 *
 * **No se propaga el `AbortSignal` del navegador, y es deliberado.** Una migración de instancia 3
 * puede tardar más de un minuto, y si el navegador se rinde —nginx corta a los 60 s, o quien opera
 * cierra la pestaña— propagar la cancelación abortaría una migración a medio escribir. Sin
 * propagarla, el peor caso es que se pierda el reporte: la migración termina en el servidor y la
 * consola la recupera consultando el estado. Perder un reporte se arregla recargando; un trámite a
 * medio migrar, no.
 */
export async function llamarMigracion(
  ruta: string,
  init: { method: "GET" | "POST" },
): Promise<RespuestaBff> {
  const llave = apiKey();
  if (!llave) {
    return problema(
      503,
      "migracion.sin_llave",
      "Este frontend arrancó sin MIGRACION_API_KEY, así que no puede hablar con el host de migración.",
    );
  }

  let response: Response;
  try {
    response = await fetch(`${upstream()}${ruta}`, {
      method: init.method,
      headers: { "X-Migration-Key": llave, Accept: "application/json" },
      cache: "no-store",
    });
  } catch (error) {
    // Un fallo de red aquí es "el host no está encendido en este ambiente", que es el caso normal:
    // `MigracionApi:Enabled` viene apagado por defecto. Se distingue de un 404 para que la consola
    // pueda decirlo con esas palabras en vez de "no encontrado".
    return problema(
      502,
      "migracion.host_inalcanzable",
      `No se pudo contactar al host de migración (${describirFallo(error)}). ` +
        "Puede que no esté encendido en este ambiente.",
    );
  }

  const texto = await response.text();

  /**
   * 404 sin cuerpo = las rutas de migración ni se registraron, o sea `MigracionApi:Enabled=false`.
   *
   * Es el fallo MÁS probable de todos —viene apagado por defecto en todos los ambientes y se
   * enciende por ola— y sin esto era el que peor se explicaba: el host devuelve el 404 pelado de
   * ASP.NET, así que la consola caía en su mensaje genérico y decía «La migración no se pudo
   * lanzar. El servidor respondió 404», que no le dice a nadie qué hacer.
   *
   * Se comprueba que el cuerpo esté vacío para no pisar un 404 que sí venga explicado.
   */
  if (response.status === 404 && !texto) {
    return problema(
      404,
      "migracion.apagado",
      "El host de migración está encendido pero con las rutas desactivadas: respondió 404 sin más.",
    );
  }

  if (!texto) {
    return { estado: response.status, cuerpo: null };
  }

  try {
    return { estado: response.status, cuerpo: JSON.parse(texto) as unknown };
  } catch {
    // El host siempre responde JSON; si llega otra cosa, la interceptó algo por el camino
    // (una página de error de nginx, por ejemplo). Se reporta como tal en vez de reventar.
    return problema(
      502,
      "migracion.respuesta_no_json",
      `El host respondió ${response.status} con algo que no es JSON.`,
    );
  }
}

function describirFallo(error: unknown): string {
  return error instanceof Error ? error.message : "causa desconocida";
}
