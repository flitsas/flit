import { RUNT_CONFIRMATION_BASE_PATH } from "@/lib/auth/guard";
import {
  canManageRuntConfirmation,
  canReadRuntConfirmationHistory,
  type JwtPayload,
} from "@/lib/auth/jwt";

/**
 * Navegación de Administración → Plataforma → Confirmación RUNT (Feature #12276, HU #12313).
 *
 * Dos vistas planas con ruta real cada una (misma receta que Improntas: barra de pestañas local,
 * no un hub layout). Cada pestaña se gatea por SU permiso y no por el del módulo: un rol con solo
 * `runt_confirmation.history.read` ve Historial y no Configuración, y viceversa.
 */
export type ConfirmacionRuntTabId = "configuracion" | "historial";

export interface ConfirmacionRuntTab {
  id: ConfirmacionRuntTabId;
  label: string;
}

export const CONFIRMACION_RUNT_TABS: ConfirmacionRuntTab[] = [
  { id: "configuracion", label: "Configuración" },
  { id: "historial", label: "Historial" },
];

export { RUNT_CONFIRMATION_BASE_PATH as CONFIRMACION_RUNT_BASE_PATH };

export function confirmacionRuntTabPath(tab: ConfirmacionRuntTabId): string {
  return `${RUNT_CONFIRMATION_BASE_PATH}/${tab}`;
}

/** Pestañas que ESTE usuario puede ver, en el orden de la barra. Vacío = no tiene el módulo. */
export function visibleConfirmacionRuntTabs(payload: JwtPayload | null): ConfirmacionRuntTab[] {
  return CONFIRMACION_RUNT_TABS.filter((tab) => canSeeConfirmacionRuntTab(payload, tab.id));
}

export function canSeeConfirmacionRuntTab(
  payload: JwtPayload | null,
  tab: ConfirmacionRuntTabId,
): boolean {
  return tab === "configuracion"
    ? canManageRuntConfirmation(payload)
    : canReadRuntConfirmationHistory(payload);
}

/**
 * Pestaña de aterrizaje al entrar por la raíz del submódulo: Configuración si puede administrarla,
 * si no Historial; `null` cuando no tiene ninguna (→ not-found del segmento).
 */
export function defaultConfirmacionRuntTab(payload: JwtPayload | null): ConfirmacionRuntTabId | null {
  return visibleConfirmacionRuntTabs(payload)[0]?.id ?? null;
}
