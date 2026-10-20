// Productos que el usuario puede abrir (GET /api/v1/platform/me/apps, contrato §6) y su icono.
import { Box, FileText, FlaskConical, Gauge, LayoutGrid, Ticket } from "lucide-react";
import type { NavIcon } from "./nav";

export interface SuiteApp {
  code: string;
  name: string;
  icon: string;
  url: string;
  current: boolean;
  /** Todavía no desplegado en este ambiente: `url` lleva a su pantalla «Próximamente» del hub. */
  comingSoon?: boolean;
}

// Nombres de platform.products.icon (DDL 119). Un icono desconocido cae a Box, nunca rompe el menú.
const ICONS: Record<string, NavIcon> = {
  "layout-grid": LayoutGrid,
  "file-text": FileText,
  ticket: Ticket,
  gauge: Gauge,
  "flask-conical": FlaskConical,
};

/** Una línea de qué hace cada producto, para el menú de productos (▦). Un código nuevo sin texto se muestra sin ella. */
const TAGLINES: Record<string, string> = {
  tramites: "Matrículas, traspasos y trámites vehiculares",
  comparendos: "Los comparendos de tu flota, a tiempo",
  diagnostico: "Estado legal y documental de tus vehículos",
};

export function appTagline(code: string): string | undefined {
  return Object.hasOwn(TAGLINES, code) ? TAGLINES[code] : undefined;
}

export function appIcon(name: string): NavIcon {
  return ICONS[name] ?? Box;
}

/** La API rechazó la sesión de la app (401): hay que pedir una nueva al hub, no mostrar «sin productos». */
export class AppsSessionError extends Error {
  constructor() {
    super("SESSION_EXPIRED");
    this.name = "AppsSessionError";
  }
}

/**
 * Lee los productos por el proxy /api/v1 de la app (el Bearer lo pone su servidor). Lanza `AppsSessionError` si la
 * sesión ya no sirve y `Error` si la API falló: una lista vacía significa de verdad que no hay productos.
 */
export async function fetchMyApps(signal?: AbortSignal): Promise<SuiteApp[]> {
  const response = await fetch("/api/v1/platform/me/apps", { credentials: "same-origin", cache: "no-store", signal });
  if (response.status === 401) throw new AppsSessionError();
  if (!response.ok) throw new Error(`me/apps respondió ${response.status}`);
  return (await response.json()) as SuiteApp[];
}
