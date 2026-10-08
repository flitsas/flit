// Lectura y escritura de la sesión cifrada en las cookies de la petición.
import type { AuthConfig } from "./config";
import { appOrigin, forRequest } from "./config";
import { sessionCookie, chunkedCookies, clearChunkedCookies, parseCookies, readChunked } from "./cookies";
import { base64UrlDecode, base64UrlEncode, seal, unseal } from "./crypto";
import { refreshSession } from "./tokens";
import type { StoredSession } from "./types";

/** Vida de la cookie de sesión: la del refresh token del hub (14 días); el access token vence a los 15 minutos. */
export const SESSION_MAX_AGE = 14 * 24 * 3600;

/** Se renueva el access token si le queda menos de esto. */
const REFRESH_MARGIN_SECONDS = 60;

export function isSecure(request: Request, config: AuthConfig): boolean {
  return appOrigin(request, config).startsWith("https://");
}

/**
 * Forma guardada en la cookie: el payload del JWT va como texto JSON (comprime mucho mejor que su base64) y se
 * reconstruye byte a byte al leerlo, así la firma sigue valiendo.
 */
interface PackedSession {
  h: string;
  p: string;
  s: string;
  rt: string | null;
  exp: number;
}

const utf8 = new TextEncoder();
const fromUtf8 = new TextDecoder();

export function pack(session: StoredSession): PackedSession {
  const [h, p, s] = session.accessToken.split(".");
  return { h, p: fromUtf8.decode(base64UrlDecode(p)), s, rt: session.refreshToken, exp: session.expiresAt };
}

export function unpack(packed: PackedSession): StoredSession {
  return { accessToken: `${packed.h}.${base64UrlEncode(utf8.encode(packed.p))}.${packed.s}`, refreshToken: packed.rt, expiresAt: packed.exp };
}

export async function unsealSession(sealed: string, secret: string): Promise<StoredSession | null> {
  const packed = await unseal<PackedSession>(sealed, secret);
  return packed ? unpack(packed) : null;
}

export async function readSession(request: Request, config: AuthConfig): Promise<StoredSession | null> {
  const sealed = readChunked(parseCookies(request.headers.get("cookie")), sessionCookie(config.productCode));
  return sealed ? unsealSession(sealed, config.sessionSecret) : null;
}

export async function sessionCookies(session: StoredSession, request: Request, config: AuthConfig): Promise<string[]> {
  return chunkedCookies(sessionCookie(config.productCode), await seal(pack(session), config.sessionSecret), {
    secure: isSecure(request, config),
    maxAgeSeconds: SESSION_MAX_AGE,
  });
}

export function clearSessionCookies(request: Request, config: AuthConfig): string[] {
  return clearChunkedCookies(sessionCookie(config.productCode), isSecure(request, config));
}

export interface FreshSession {
  session: StoredSession | null;
  /** Set-Cookie a agregar a la respuesta (sesión renovada o borrada). */
  setCookies: string[];
}

/**
 * El servidor rota el refresh en cada uso y, si alguien vuelve a usar uno ya canjeado, revoca toda la sesión (señal de
 * robo, `RefreshTokenReuseLeewaySeconds` = 0). Una página dispara varias llamadas a la vez: si el token está por vencer,
 * cada una intentaría renovar con el mismo refresh y la segunda tumbaría la sesión («Tu sesión expiró» al azar).
 * Por eso hay una sola renovación por refresh token en este proceso: las peticiones simultáneas esperan la misma, y su
 * resultado se reusa un rato para las que llegan todavía con la cookie vieja (salieron antes de recibir la nueva).
 */
const RENEWAL_REUSE_MS = 30_000;
type Renewals = Map<string, { result: Promise<StoredSession>; until: number }>;
// En `globalThis` y no en el módulo: Next empaqueta cada ruta (el BFF, /auth/claims, /auth/session) por separado y cada
// paquete tendría su propia copia; el registro tiene que ser uno por proceso.
const renewals: Renewals = ((globalThis as { __flitAuthRenewals?: Renewals }).__flitAuthRenewals ??= new Map());

function renewOnce(config: AuthConfig, session: StoredSession): Promise<StoredSession> {
  const now = Date.now();
  for (const [key, entry] of renewals) if (entry.until <= now) renewals.delete(key);

  const key = `${config.productCode}:${session.refreshToken}`;
  const current = renewals.get(key);
  if (current) return current.result;

  const result = refreshSession(config, session);
  renewals.set(key, { result, until: now + RENEWAL_REUSE_MS });
  // Un fallo no se recuerda: las que ya esperaban lo comparten, la siguiente vuelve a preguntar.
  result.catch(() => renewals.delete(key));
  return result;
}

/** Solo para pruebas: olvida las renovaciones recordadas. */
export function forgetRenewals(): void {
  renewals.clear();
}

/**
 * La sesión de la petición con el access token vigente: si está por vencer se renueva; si el refresh ya no sirve
 * (revocado, usuario suspendido, sin acceso al producto) se borra y queda sin sesión.
 */
export async function freshSession(request: Request, requestConfig: AuthConfig, now = Math.floor(Date.now() / 1000)): Promise<FreshSession> {
  // La renovación se sella con el hub de la raíz de la petición: es el emisor con que se obtuvo el refresh token.
  const config = forRequest(request, requestConfig);
  const session = await readSession(request, config);
  if (!session) return { session: null, setCookies: [] };
  if (session.expiresAt - now > REFRESH_MARGIN_SECONDS) return { session, setCookies: [] };

  try {
    const renewed = await renewOnce(config, session);
    return { session: renewed, setCookies: await sessionCookies(renewed, request, config) };
  } catch {
    return { session: null, setCookies: clearSessionCookies(request, config) };
  }
}
