"use client";

import { ReactNode, useEffect, useMemo, useState } from "react";
import { usePathname } from "next/navigation";
import { SuiteShell, type SuiteApp } from "@flit/shell/SuiteShell";
import { useSuiteTheme } from "@flit/shell/theme";
import {
  decodeJwtPayload,
  isAdminCompany,
  isGroupParent,
  isOtAdmin,
  isOtUser,
  isSuperAdmin,
  TOKEN_STORAGE_KEY,
} from "@/lib/auth/jwt";
import { apiFetch } from "@/lib/api/client";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import { extractTransitOfficeIdFromPath, resolveOtTransitOfficeId } from "@/components/admin/transit-offices/ot-nav";
import { DrFlitAssistant } from "@/components/dr-flit";
import { tramitesNav } from "./dock/tramitesNav";

export type ModuleId =
  | "dashboard"
  | "tramites"
  | "reportes"
  | "reportes-detallados"
  | "validaciones"
  | "historial-placa"
  | "usuarios"
  | "ayuda"
  | "rbac"
  | "auditoria"
  | "log-qx"
  | "ict-logs"
  | "ict-reportes"
  | "ict-trazabilidad";

/** Productos del usuario por el cliente de la API (Bearer con la sesión antigua; BFF con la de @flit/auth). */
const loadApps = (signal: AbortSignal) => apiFetch<SuiteApp[]>("/api/v1/platform/me/apps", { signal });

function useCurrentUser() {
  const [user] = useState(() => {
    if (typeof window === "undefined") return null;
    const token = window.localStorage.getItem(TOKEN_STORAGE_KEY);
    const payload = decodeJwtPayload(token);
    if (!payload) return null;
    // HU #10506 (multi-rol): con 2+ roles, el backend serializa role_code/role como
    // ARRAY (colapso de claims .NET), no como string — por eso la fuente confiable es
    // siempre el claim `roles` (array explícito de {id, code}), con role_code/role como
    // fallback solo cuando de verdad vienen como string (usuario con un único rol).
    const roleCodes = Array.isArray(payload.roles)
      ? payload.roles
          .map((r) => r?.code)
          .filter((c): c is string => typeof c === "string")
      : [];
    if (roleCodes.length === 0) {
      if (typeof payload.role_code === "string") roleCodes.push(payload.role_code);
      else if (typeof payload.role === "string") roleCodes.push(payload.role);
    }
    return {
      displayName: (payload.display_name as string | undefined) ?? null,
      email: (payload.email as string | undefined) ?? "",
      roles: roleCodes,
      // El token compara permisos sin mayúsculas (`hasPermission`); el catálogo los declara en minúscula.
      permissions: Array.isArray(payload.permissions)
        ? payload.permissions.filter((p): p is string => typeof p === "string").map((p) => p.toLowerCase())
        : [],
      tenantName: payload.tenant_name ?? null,
      isSuperAdmin: isSuperAdmin(payload),
      isAdminCompany: isAdminCompany(payload),
      isGroupParent: isGroupParent(payload),
      isOtAdmin: isOtAdmin(payload),
      // Cualquier rol de un tenant OT (no solo ot_admin) opera la superficie del organismo.
      isOtUser: isOtUser(payload),
      tenantId: (payload.tenant_id as string) ?? null,
    };
  });
  return user;
}

/** Organismo del usuario OT: el de la ruta, el recordado en la sesión o el de su perfil. */
function useOtTransitOfficeId(enabled: boolean, pathname: string): string | null {
  const fromPath = enabled ? extractTransitOfficeIdFromPath(pathname) : null;
  const [resolved, setResolved] = useState<string | null>(null);
  useEffect(() => {
    if (!enabled || fromPath) return;
    let alive = true;
    resolveOtTransitOfficeId(async () => (await fetchOtProfile()).transitOfficeId)
      .then((id) => alive && setResolved(id))
      .catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [enabled, fromPath]);
  return fromPath ?? resolved;
}

/**
 * Barra de Trámites (B-13): la barra común de la suite (`@flit/shell`) con el catálogo de Trámites. Aquí queda solo lo
 * propio del producto: quién es el usuario (sesión antigua o claims de @flit/auth, las dos en `flit:jwt`), el tema,
 * Dr. FLIT y el pie. El dock ya no recibe el módulo activo ni un `onNav`: cada entrada es un enlace y la activa sale de
 * la URL.
 */
export function Shell({
  children,
  onLogout,
  visibleModuleCodes,
  search = "",
}: {
  children: ReactNode;
  onLogout?: () => void;
  /** Módulos RBAC accesibles (`useAccessibleModules`); sin la lista no se filtran los módulos de la SPA. */
  visibleModuleCodes?: string[];
  /** Consulta actual (`?m=…`) para marcar el módulo de la SPA; solo la página `/` la necesita. */
  search?: string;
}) {
  const { dark } = useSuiteTheme();
  const currentUser = useCurrentUser();
  const pathname = usePathname() ?? "";
  const otTransitOfficeId = useOtTransitOfficeId(Boolean(currentUser?.isOtUser), pathname);

  const nav = useMemo(
    () =>
      tramitesNav({
        isOtUser: Boolean(currentUser?.isOtUser),
        isOtAdmin: Boolean(currentUser?.isOtAdmin),
        isAdminCompany: Boolean(currentUser?.isAdminCompany),
        isGroupParent: Boolean(currentUser?.isGroupParent),
        tenantId: currentUser?.tenantId ?? null,
        otTransitOfficeId,
      }),
    [currentUser, otTransitOfficeId],
  );

  const user = useMemo(
    () => ({
      email: currentUser?.email ?? "",
      displayName: currentUser?.displayName ?? null,
      tenantName: currentUser?.tenantName ?? null,
      permissions: currentUser?.permissions ?? [],
      roles: currentUser?.roles ?? [],
      isSuperAdmin: Boolean(currentUser?.isSuperAdmin),
      modules: visibleModuleCodes,
    }),
    [currentUser, visibleModuleCodes],
  );

  const activeModule = pathname.startsWith("/tramites") ? "tramites" : (new URLSearchParams(search).get("m") ?? "dashboard");
  // Mismo criterio que el dock: sin filtro RBAC se ve todo; con filtro, solo si el módulo viene.
  const hasModule = (code: string) => (visibleModuleCodes ? visibleModuleCodes.includes(code) : true);

  return (
    <SuiteShell
      productCode="tramites"
      productName="Trámites"
      nav={nav}
      user={user}
      loadApps={loadApps}
      homeHref="/"
      search={search}
      onLogout={onLogout}
      layout="app"
      overlay={
        // DR. FLIT — asistente conversacional sobre APIs existentes (búsqueda por rol/alcance).
        <DrFlitAssistant
          displayName={currentUser?.displayName ?? currentUser?.email ?? null}
          routeScope={`${pathname}|${activeModule}`}
          historialPlacaEnabled={hasModule("historial-placa")}
          canSearchValidaciones={hasModule("validaciones")}
          // Épica #12718 — al caso de soporte solo va un nombre real: `displayName` es null si el token no lo trae
          // (no cae al correo); sin nombre, la persona lo escribe.
          supportContact={{ name: currentUser?.displayName ?? null, email: currentUser?.email || null }}
        />
      }
      footer={
        <footer
          className="shrink-0 px-4 md:px-6 py-2 border-t text-[10px] text-center"
          style={{
            borderColor: dark ? "rgba(255,255,255,0.08)" : "var(--color-flit-gray)",
            color: dark ? "rgba(255,255,255,0.55)" : "rgba(22,39,68,0.6)",
          }}
        >
          Políticas de Privacidad y Términos de Uso · © 2026 FLIT · Todos los derechos reservados · Protegido por cifrado TLS · Auditoría continua · ISO 27001
        </footer>
      }
    >
      {children}
    </SuiteShell>
  );
}
