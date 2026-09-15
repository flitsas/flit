// Resolución de marca en el SERVIDOR (HU #12419, ADR-0060 §D5). Solo se ejecuta en Server
// Components / generateMetadata — NUNCA en el navegador (evita el destello con los colores de
// FLIT: AC1). `React.cache` garantiza una sola resolución por render server, compartida entre
// `generateMetadata()` y el layout (misma petición).
//
// Uso de ejemplo:
//   const brand = await resolveBrand();
//   if (isFlitBrand(brand)) { ...camino de hoy, byte a byte (AC7)... }
import "server-only";

import { cache } from "react";
import { headers } from "next/headers";
import { isFlitHost } from "./hosts";
import { FLIT_BRAND, type Brand } from "./types";

/** Ventana máxima de espera al Gateway (delta-hechos-post-adr.md #7): no debe retrasar la TTFB. */
const FETCH_TIMEOUT_MS = 1500;

let warnedOnce = false;

/**
 * Registra el fallo UNA sola vez por proceso — AC5 exige que el respaldo "se registre sin
 * mostrarse como error al usuario"; repetir el warning en cada petición inundaría los logs sin
 * aportar nada nuevo (el estado ya es "se está usando FLIT por fallo").
 */
function warnOnce(message: string, error?: unknown): void {
  if (warnedOnce) return;
  warnedOnce = true;
  console.warn(`[brand] ${message}`, error);
}

/**
 * Base interna hacia el Gateway por la red Docker (mismo patrón que `MIGRACION_API_URL` /
 * `lib/migracion/server.ts`). Server-only: SIN prefijo `NEXT_PUBLIC_`, no se hornea en el bundle
 * del navegador. Delta-hechos-post-adr.md #7.
 */
function internalApiBase(): string {
  return process.env.BRANDING_INTERNAL_API_URL ?? "http://localhost:4002";
}

/** `X-Internal-Key` server-only (delta-hechos-post-adr.md #24) — el Gateway exige esta cabecera,
 * además de `X-Flit-Domain`, para confiar en el sello y no tratar la petición como pública. Sin
 * la variable configurada (p. ej. dev local sin secretos), simplemente no se envía: el Gateway
 * ignora `X-Flit-Domain` y el respaldo sigue siendo FLIT, nunca un error visible. */
function internalApiKey(): string | undefined {
  return process.env.FLIT_INTERNAL_API_KEY || undefined;
}

function isBrandShape(value: unknown): value is Brand {
  if (!value || typeof value !== "object") return false;
  const v = value as Record<string, unknown>;
  if (typeof v.platformName !== "string" || !v.platformName.trim()) return false;
  if (v.logoUrl !== null && typeof v.logoUrl !== "string") return false;
  if (typeof v.version !== "number" || !Number.isFinite(v.version)) return false;
  const colors = v.colors as Record<string, unknown> | null | undefined;
  if (!colors || typeof colors !== "object") return false;
  return (
    typeof colors.primary === "string" &&
    typeof colors.secondary === "string" &&
    typeof colors.onPrimary === "string"
  );
}

async function fetchBrandFor(host: string): Promise<Brand> {
  const url = `${internalApiBase()}/api/v1/public/branding`;
  const key = internalApiKey();

  let response: Response;
  try {
    response = await fetch(url, {
      headers: {
        "X-Flit-Domain": host,
        ...(key ? { "X-Internal-Key": key } : {}),
      },
      // Anti-destello con caché corta: el ADR fija 60s (mismo TTL que la caché del backend).
      next: { revalidate: 60 },
      signal: AbortSignal.timeout(FETCH_TIMEOUT_MS),
    });
  } catch (error) {
    warnOnce("fallo de red/timeout al resolver la marca; se usa FLIT", error);
    return FLIT_BRAND;
  }

  if (!response.ok) {
    warnOnce(`respuesta no-OK (${response.status}) de /public/branding; se usa FLIT`);
    return FLIT_BRAND;
  }

  let data: unknown;
  try {
    data = await response.json();
  } catch (error) {
    warnOnce("cuerpo no-JSON de /public/branding; se usa FLIT", error);
    return FLIT_BRAND;
  }

  if (!isBrandShape(data)) {
    warnOnce("forma de respuesta inválida en /public/branding; se usa FLIT");
    return FLIT_BRAND;
  }

  return data;
}

/**
 * Resuelve la marca de la petición actual. Host FLIT ⇒ `FLIT_BRAND` SIN fetch (AC7: cero
 * llamadas nuevas). Host de red ⇒ `GET /public/branding` con el sello `X-Flit-Domain` explícito;
 * cualquier fallo (red, timeout, forma inválida, no-200) ⇒ `FLIT_BRAND` (AC5, respaldo seguro).
 */
export const resolveBrand = cache(async (): Promise<Brand> => {
  const requestHeaders = await headers();
  const host = requestHeaders.get("host");

  if (isFlitHost(host)) {
    return FLIT_BRAND;
  }

  return fetchBrandFor(host as string);
});
