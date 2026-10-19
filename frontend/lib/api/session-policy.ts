// Cliente de `GET /api/v1/auth/session-policy` (HU #13651, Feature #13102). Contrato fijo:
//   { "mode": "legacy" | "oidc", "maxMinutes": number }
// Lo consume la pantalla de permisos por usuario para decir cuándo entra en vigor un cambio (`mensajeVigencia`).
import type { SessionPolicy } from "@/lib/auth/vigencia";
import { apiFetch } from "./client";
import { ApiError } from "./types";

/** Valida la forma en el borde: un servidor que contesta otra cosa no debe llegar a la pantalla como texto falso. */
export function parseSessionPolicy(raw: unknown): SessionPolicy {
  const candidate = raw as Partial<Record<keyof SessionPolicy, unknown>> | null;
  const mode = candidate?.mode;
  const maxMinutes = candidate?.maxMinutes;
  if ((mode !== "legacy" && mode !== "oidc") || typeof maxMinutes !== "number" || !Number.isFinite(maxMinutes) || maxMinutes <= 0) {
    throw new ApiError(502, "La política de sesión del servidor no tiene la forma esperada.", raw);
  }
  return { mode, maxMinutes };
}

export async function fetchSessionPolicy(signal?: AbortSignal): Promise<SessionPolicy> {
  const raw = await apiFetch<unknown>("/api/v1/auth/session-policy", { signal });
  return parseSessionPolicy(raw);
}
