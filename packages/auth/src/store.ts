// Lectura y escritura de la sesión cifrada en las cookies de la petición.
import type { AuthConfig } from "./config";
import { appOrigin } from "./config";
import { SESSION_COOKIE, chunkedCookies, clearChunkedCookies, parseCookies, readChunked } from "./cookies";
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
  const sealed = readChunked(parseCookies(request.headers.get("cookie")), SESSION_COOKIE);
  return sealed ? unsealSession(sealed, config.sessionSecret) : null;
}

export async function sessionCookies(session: StoredSession, request: Request, config: AuthConfig): Promise<string[]> {
  return chunkedCookies(SESSION_COOKIE, await seal(pack(session), config.sessionSecret), {
    secure: isSecure(request, config),
    maxAgeSeconds: SESSION_MAX_AGE,
  });
}

export function clearSessionCookies(request: Request, config: AuthConfig): string[] {
  return clearChunkedCookies(SESSION_COOKIE, isSecure(request, config));
}

export interface FreshSession {
  session: StoredSession | null;
  /** Set-Cookie a agregar a la respuesta (sesión renovada o borrada). */
  setCookies: string[];
}

/**
 * La sesión de la petición con el access token vigente: si está por vencer se renueva; si el refresh ya no sirve
 * (revocado, usuario suspendido, sin acceso al producto) se borra y queda sin sesión.
 */
export async function freshSession(request: Request, config: AuthConfig, now = Math.floor(Date.now() / 1000)): Promise<FreshSession> {
  const session = await readSession(request, config);
  if (!session) return { session: null, setCookies: [] };
  if (session.expiresAt - now > REFRESH_MARGIN_SECONDS) return { session, setCookies: [] };

  try {
    const renewed = await refreshSession(config, session);
    return { session: renewed, setCookies: await sessionCookies(renewed, request, config) };
  } catch {
    return { session: null, setCookies: clearSessionCookies(request, config) };
  }
}
