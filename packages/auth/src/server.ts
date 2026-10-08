// @flit/auth del lado del servidor (contrato de plataforma v1, §8). Uso en una app Next.js:
//
//   // app/auth/[action]/route.ts
//   const routes = createAuthRoutes({ productCode: "tramites" });
//   export const GET = (req, { params }) => ...routes[action](req)
//
//   // app/api/v1/[...path]/route.ts
//   const proxy = createApiProxy({ productCode: "tramites" });
//
//   // Server Component
//   const user = await getSession();
import { cookies } from "next/headers";
import { sessionUser } from "./claims";
import { authConfig } from "./config";
import { readChunked, sessionCookie } from "./cookies";
import { unsealSession } from "./store";
import type { SessionUser } from "./types";

export { claimsToken, createAuthRoutes, safeReturnTo, type AuthRoutesOptions, type RouteHandler } from "./routes";
export { createApiProxy, type ApiProxyOptions } from "./proxy";
export { authConfig, forRequest, hubUrlFor, type AuthConfig } from "./config";
export type { SessionUser } from "./types";

/**
 * Usuario de la sesión en un Server Component, o `null`. No renueva el token (un Server Component no puede escribir
 * cookies): lo hace el proxy en la siguiente llamada a la API. Por eso aquí vale aunque el access token haya vencido,
 * mientras haya refresh.
 */
export async function getSession(productCode: string): Promise<SessionUser | null> {
  const config = authConfig(productCode);
  const sealed = readChunked(await cookies(), sessionCookie(productCode));
  const session = sealed ? await unsealSession(sealed, config.sessionSecret) : null;
  if (!session) return null;
  if (session.expiresAt <= Math.floor(Date.now() / 1000) && !session.refreshToken) return null;
  return sessionUser(session.accessToken);
}

/**
 * Access token vigente de la sesión, para que un Server Component llame a la API antes de pintar (p. ej. el inicio del
 * hub, B-11). `null` si venció o falta menos de un minuto: en ese caso la página pide los datos por el proxy desde el
 * navegador, que sí renueva. Nunca se entrega al navegador.
 */
export async function getAccessToken(productCode: string): Promise<string | null> {
  const config = authConfig(productCode);
  const sealed = readChunked(await cookies(), sessionCookie(productCode));
  const session = sealed ? await unsealSession(sealed, config.sessionSecret) : null;
  if (!session || session.expiresAt - Math.floor(Date.now() / 1000) < 60) return null;
  return session.accessToken;
}
