import type { StatusTone } from "./StatusBadge";

// HU #10844 — Mapeo estado → tone semántico por dominio. Fuente única para que el mismo
// estado se pinte igual en toda la app. El color de cada tone vive en globals.css.

/** Activo/Inactivo genérico (compañías, documentos): activo = success, inactivo = danger. */
export function activeTone(active: boolean): StatusTone {
  return active ? "success" : "danger";
}

/** Activo/Inactivo "suave" (catálogos, roles): inactivo = neutral en vez de danger. */
export function enabledTone(enabled: boolean): StatusTone {
  return enabled ? "success" : "neutral";
}

/** Tipos de trámite del configurador (superadmin). */
export function procedureTypeTone(status: "draft" | "published" | "archived"): StatusTone {
  switch (status) {
    case "published":
      return "success";
    case "draft":
      return "info";
    case "archived":
    default:
      return "neutral";
  }
}
