"use client";

import { Network } from "lucide-react";
import { iconoComo } from "@flit/ui/Icono";
import { SuiteShell, type NavCatalog, type ShellUser } from "@flit/shell/SuiteShell";
import { rutaKit } from "@flit/ui/iconos-mapas";

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
    sections: [{ id: "administracion", label: "Administración", icon: iconoComo("ajustes"), side: "right", ilustracion: rutaKit("modulo/configuracion") }],
    items: [
      { key: "empresa", label: "Mi empresa", href: `${tramitesUrl}/admin/companies`, section: "administracion", icon: iconoComo("empresas"), ilustracion: rutaKit("modulo/empresas"), roles: ["AdminCompany"] },
      { key: "usuarios", label: "Usuarios y roles", href: `${tramitesUrl}/?m=usuarios`, section: "administracion", icon: iconoComo("usuarios"), ilustracion: rutaKit("modulo/colaboradores"), permission: "usuarios.manage" },
      { key: "companias", label: "Compañías", href: `${tramitesUrl}/admin/companies`, section: "administracion", icon: Network, ilustracion: rutaKit("modulo/organismos"), superAdminOnly: true },
      { key: "rbac", label: "Roles y permisos", href: `${tramitesUrl}/admin/rbac`, section: "administracion", icon: iconoComo("rol"), ilustracion: rutaKit("modulo/roles"), superAdminOnly: true },
      { key: "jobs", label: "Tareas programadas", href: `${tramitesUrl}/admin/jobs`, section: "administracion", icon: iconoComo("reloj"), ilustracion: rutaKit("modulo/procesos"), superAdminOnly: true },
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
