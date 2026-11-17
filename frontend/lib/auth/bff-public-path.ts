/**
 * HU #14546 — el BFF de Trámites (OIDC) exige sesión salvo para `/api/v1/public/*`, donde el token del enlace
 * (captura manual, biometría, portal, etc.) es la credencial y la API ya es AllowAnonymous.
 */
export function isTramitesPublicApiPath(path: string[]): boolean {
  return path[0] === "public";
}
