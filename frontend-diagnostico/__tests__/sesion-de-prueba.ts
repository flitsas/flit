// Utilidades de las pruebas de sesión: un JWT sin firma (como el que entrega el hub) y las variables de entorno mínimas
// para que @flit/auth abra sesiones en el servidor. Datos ficticios.
import { vi } from "vitest";

export const APP = "https://dev.diagnostico.flitsas.online";
export const HUB = "https://dev.flitsas.online";
export const GATEWAY = "http://gateway:4002";
export const CLAVE_INTERNA = "clave-interna-de-prueba";

export function entornoDeServidor(): void {
  vi.stubEnv("FLIT_SESSION_SECRET", "s".repeat(40));
  vi.stubEnv("FLIT_HUB_URL", HUB);
  vi.stubEnv("FLIT_OIDC_INTERNAL_URL", GATEWAY);
  vi.stubEnv("CORE_API_ORIGIN", GATEWAY);
  vi.stubEnv("FLIT_INTERNAL_API_KEY", CLAVE_INTERNA);
  vi.stubEnv("FLIT_APP_URL", "");
  vi.stubEnv("FLIT_NETWORK_DOMAINS_ENABLED", "false");
}

const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString("base64url");

export function jwt(claims: Record<string, unknown>): string {
  return `${b64({ alg: "RS256" })}.${b64(claims)}.firma`;
}

export const ahora = () => Math.floor(Date.now() / 1000);

/** Cabecera Cookie con las cookies vivas de una lista de Set-Cookie. */
export function cabeceraCookie(setCookies: string[]): string {
  return setCookies
    .map((c) => c.split(";")[0])
    .filter((c) => !c.endsWith("="))
    .join("; ");
}
