// Productos que el usuario puede abrir (GET /api/v1/platform/me/apps, contrato §6) y su icono.
import { Box, FileText, FlaskConical, Gauge, LayoutGrid, Ticket } from "lucide-react";
import type { NavIcon } from "./nav";

export interface SuiteApp {
  code: string;
  name: string;
  icon: string;
  url: string;
  current: boolean;
}

// Nombres de platform.products.icon (DDL 119). Un icono desconocido cae a Box, nunca rompe el menú.
const ICONS: Record<string, NavIcon> = {
  "layout-grid": LayoutGrid,
  "file-text": FileText,
  ticket: Ticket,
  gauge: Gauge,
  "flask-conical": FlaskConical,
};

export function appIcon(name: string): NavIcon {
  return ICONS[name] ?? Box;
}

/** Lee los productos por el proxy /api/v1 de la app (el Bearer lo pone su servidor). Sin sesión, lista vacía. */
export async function fetchMyApps(signal?: AbortSignal): Promise<SuiteApp[]> {
  const response = await fetch("/api/v1/platform/me/apps", { credentials: "same-origin", cache: "no-store", signal });
  if (!response.ok) return [];
  return (await response.json()) as SuiteApp[];
}
