"use client";

import { Building2, Clock, KeyRound, Network, Settings, Users } from "lucide-react";
import { SuiteShell, type NavCatalog, type ShellUser } from "@flit/shell/SuiteShell";

export interface HubUser extends ShellUser {
  permissions: string[];
  roles: string[];
  isSuperAdmin: boolean;
}

/**
 * Catálogo del hub (B-10). La administración de plataforma sigue en Trámites hasta que B-12 la mueva aquí: por eso los
 * enlaces son absolutos a su host. Mismas reglas de acceso que el menú de Trámites.
 */
export function hubNav(tramitesUrl: string): NavCatalog {
  return {
    sections: [{ id: "administracion", label: "Administración", icon: Settings, side: "right" }],
    items: [
      { key: "empresa", label: "Mi empresa", href: `${tramitesUrl}/empresa`, section: "administracion", icon: Building2, roles: ["AdminCompany"] },
      { key: "usuarios", label: "Usuarios y roles", href: `${tramitesUrl}/?m=usuarios`, section: "administracion", icon: Users, permission: "usuarios.manage" },
      { key: "companias", label: "Compañías", href: `${tramitesUrl}/admin/companies`, section: "administracion", icon: Network, superAdminOnly: true },
      { key: "rbac", label: "Roles y permisos", href: `${tramitesUrl}/admin/rbac`, section: "administracion", icon: KeyRound, superAdminOnly: true },
      { key: "jobs", label: "Tareas programadas", href: `${tramitesUrl}/admin/jobs`, section: "administracion", icon: Clock, superAdminOnly: true },
    ],
  };
}

export function HubShell({ user, tramitesUrl, children }: { user: HubUser; tramitesUrl: string; children: React.ReactNode }) {
  return (
    <SuiteShell productCode="plataforma" productName="Inicio" nav={hubNav(tramitesUrl)} user={user} homeHref="/?inicio=1">
      {children}
    </SuiteShell>
  );
}
