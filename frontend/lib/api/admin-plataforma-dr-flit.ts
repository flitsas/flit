import { apiFetch } from "./client";
import { ApiError } from "./types";

/** HU #14771 / #14772 — SuperAdmin → Plataforma → Asistente DR. FLIT. */
const RUTA = "/api/v1/admin/plataforma/dr-flit";
const UI_RUTA = "/api/v1/dr-flit/ui";

export type DrFlitPlatformSettings = {
  uiEnabled: boolean;
  rowVersion: number;
  updatedAt: string | null;
  updatedBy: string | null;
};

export type DrFlitUiVisibility = { uiEnabled: boolean };

export function getDrFlitPlatformSettings(signal?: AbortSignal) {
  return apiFetch<DrFlitPlatformSettings>(RUTA, { signal });
}

export function putDrFlitPlatformSettings(
  body: { uiEnabled: boolean; rowVersion: number | null },
  signal?: AbortSignal,
) {
  return apiFetch<DrFlitPlatformSettings>(RUTA, {
    method: "PUT",
    body,
    signal,
  });
}

/** Lectura para el Shell (cualquier autenticado). */
export function getDrFlitUiVisibility(signal?: AbortSignal) {
  return apiFetch<DrFlitUiVisibility>(UI_RUTA, { signal });
}

export function interpretarErrorDrFlitSettings(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return "Solo el SuperAdmin puede cambiar esta configuración.";
    if (err.status === 409) return "Otro administrador cambió el valor. Recarga e inténtalo de nuevo.";
    if (err.status === 401) return "Sesión expirada. Vuelve a iniciar sesión.";
    return err.message || "No se pudo guardar la configuración.";
  }
  return "No se pudo guardar la configuración.";
}
