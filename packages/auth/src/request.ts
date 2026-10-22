// Lectura de la sesión desde las cookies de una petición, sin next/headers: para el middleware de Next.js, que corre
// antes de renderizar (guardias de rutas) y no debe arrastrar módulos de servidor.
import { authConfig } from "./config";
import { parseCookies, readChunked, sessionCookie } from "./cookies";
import { claimsToken } from "./routes";
import { unsealSession } from "./store";

/** `true` si la app corre con la sesión de @flit/auth (FLIT_SESSION_MODE=oidc), leída en runtime. */
export function isOidcSessionMode(env: NodeJS.ProcessEnv = process.env): boolean {
  return env.FLIT_SESSION_MODE === "oidc";
}

/** Claims de la sesión (`header.payload.`, sin firma) o `null` si no hay sesión válida. */
export async function claimsTokenFromCookieHeader(cookieHeader: string | null, productCode: string): Promise<string | null> {
  const sealed = readChunked(parseCookies(cookieHeader), sessionCookie(productCode));
  if (!sealed) return null;
  const session = await unsealSession(sealed, authConfig(productCode).sessionSecret);
  return session ? claimsToken(session.accessToken) : null;
}
