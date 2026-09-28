"use client";

import { type ReactNode } from "react";
import { Shell } from "@/components/atom/Shell";
import { useAccessibleModules } from "@/hooks/useAccessibleModules";
import { useAuthGate } from "@/hooks/useAuthGate";

/**
 * Layout de /admin/banners/* (HU #12241, Feature #12236). Mismo patrón que
 * /admin/causales-rechazo e /admin/improntas: replica el chrome de la SPA (Shell + dock) para
 * que la navegación no desaparezca al entrar al módulo.
 */
export default function AdminBannersLayout({ children }: { children: ReactNode }) {
  const { authed, hydrated, logout } = useAuthGate();

  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);

  if (!hydrated || !authed) return null;

  return (
    <Shell
      onLogout={logout}
      visibleModuleCodes={modulesLoading ? [] : accessibleCodes}
    >
      <div className="app-bg min-h-screen w-full">{children}</div>
    </Shell>
  );
}
