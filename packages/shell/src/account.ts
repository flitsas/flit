// Lo que la barra de la suite muestra igual en todos los productos: las opciones del menú de usuario y el nombre del
// rol. Vive aquí para que un producto nuevo no lo copie (antes estaba repetido en Trámites y en el hub).
import { HelpCircle, KeyRound } from "lucide-react";
import type { NavIcon } from "./nav";

/** Enlace del menú de usuario (antes de «Salir de la plataforma»). */
export interface AccountLink {
  label: string;
  href: string;
  icon: NavIcon;
}

/**
 * Opciones de cuenta de toda la suite. Las pantallas viven hoy en Trámites (B-12 las traerá al hub): `accountUrl` es el
 * origen donde están, vacío si son de esta misma app.
 */
export function suiteAccountLinks(accountUrl = ""): AccountLink[] {
  const base = accountUrl.replace(/\/+$/, "");
  return [
    { label: "Ayuda", href: `${base}/manual`, icon: HelpCircle },
    { label: "Cambio de contraseña", href: `${base}/profile/change-password`, icon: KeyRound },
  ];
}

const ROLE_LABELS: Record<string, string> = {
  SuperAdmin: "Super Admin",
  AdminCompany: "Admin de Compañía",
  ot_admin: "Admin OT",
};

/** Nombre del rol en la barra: los de plataforma con su rótulo; cualquier otro, tal cual; sin roles, «Usuario». */
export function suiteRoleLabel(roles: readonly string[]): string {
  const known = Object.keys(ROLE_LABELS).find((code) => roles.includes(code));
  return known ? ROLE_LABELS[known] : (roles[0] ?? "Usuario");
}
