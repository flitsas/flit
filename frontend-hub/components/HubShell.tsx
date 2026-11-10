"use client";

import { Building2, Clock, KeyRound, Network, Settings, Users } from "lucide-react";
import { SuiteShell, type NavCatalog, type ShellUser } from "@flit/shell/SuiteShell";
import { rutaIlustracion } from "@flit/ui/ilustraciones";

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
    sections: [{ id: "administracion", label: "Administración", icon: Settings, side: "right", ilustracion: rutaIlustracion("configuracion") }],
    items: [
      { key: "empresa", label: "Mi empresa", href: `${tramitesUrl}/admin/companies`, section: "administracion", icon: Building2, ilustracion: rutaIlustracion("empresas"), roles: ["AdminCompany"] },
      { key: "usuarios", label: "Usuarios y roles", href: `${tramitesUrl}/?m=usuarios`, section: "administracion", icon: Users, ilustracion: rutaIlustracion("colaboradores"), permission: "usuarios.manage" },
      { key: "companias", label: "Compañías", href: `${tramitesUrl}/admin/companies`, section: "administracion", icon: Network, ilustracion: rutaIlustracion("organismos"), superAdminOnly: true },
      { key: "rbac", label: "Roles y permisos", href: `${tramitesUrl}/admin/rbac`, section: "administracion", icon: KeyRound, ilustracion: rutaIlustracion("roles"), superAdminOnly: true },
      { key: "jobs", label: "Tareas programadas", href: `${tramitesUrl}/admin/jobs`, section: "administracion", icon: Clock, ilustracion: rutaIlustracion("procesos"), superAdminOnly: true },
    ],
  };
}

export function HubShell({ user, tramitesUrl, children }: { user: HubUser; tramitesUrl: string; children: React.ReactNode }) {
  return (
    <SuiteShell
      productCode="plataforma"
      productName="Inicio"
      nav={hubNav(tramitesUrl)}
      user={user}
      homeHref="/?inicio=1"
      accountUrl={tramitesUrl}
      layout="app"
    >
      {/* Como en Comparendos y Diagnóstico (HU #14487): el fondo de la propuesta del shell y el colchón inferior para
          que el dock no tape la última fila. */}
      <div className="app-bg flex min-h-full flex-col px-4 pt-6 pb-28 md:px-6">{children}</div>
    </SuiteShell>
  );
}
