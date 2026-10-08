// URL de Trámites para la raíz de una petición (raíces alternativas de la suite, Suite:Hosts:AlternateRoots). En PDN el
// hub se sirve en flitsas.online y en app.flitsas.com: quien entra por uno sigue en su dominio al ir a Trámites
// (tramites.flitsas.online o tramites.flitsas.com). Función pura: la usan el middleware y las páginas, y sus pruebas.
//
// TRAMITES_URL es la de la raíz principal; TRAMITES_URLS, las de las raíces alternativas (lista cerrada, separada por
// comas). Se elige la de la misma raíz que el host (los dos últimos nombres); si ninguna coincide, TRAMITES_URL.

const DEFAULT_TRAMITES_URL = "http://127.0.0.1:3000";

export function tramitesUrls(env: NodeJS.ProcessEnv = process.env): string[] {
  const urls = [trimSlash(env.TRAMITES_URL || DEFAULT_TRAMITES_URL)];
  for (const url of (env.TRAMITES_URLS ?? "").split(",")) {
    const trimmed = trimSlash(url.trim());
    if (trimmed && !urls.includes(trimmed)) urls.push(trimmed);
  }
  return urls;
}

export function tramitesUrlFor(host: string | null | undefined, env: NodeJS.ProcessEnv = process.env): string {
  const urls = tramitesUrls(env);
  if (!host || urls.length < 2) return urls[0];
  const root = rootOf(host);
  return urls.find((url) => rootOf(new URL(url).hostname) === root) ?? urls[0];
}

function rootOf(host: string): string {
  return host.split(":")[0].toLowerCase().replace(/\.$/, "").split(".").slice(-2).join(".");
}

function trimSlash(url: string): string {
  return url.replace(/\/+$/, "");
}
