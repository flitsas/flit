"use client";

import { useEffect, useState, type ReactNode } from "react";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isOtAdmin, isSoloCaja, isSuperAdmin } from "@/lib/auth/jwt";
import { IconoAviso } from "@flit/ui/IconoAviso";

export const PORTAL_PN_NO_ACCESS_MESSAGE =
  "Solo el SuperAdmin o el administrador del organismo pueden configurar el portal Persona Natural. Pide acceso al administrador de tu organismo.";

type Access = "checking" | "allowed" | "denied";

/**
 * Entrada al Portal Persona Natural del hub: SuperAdmin y `ot_admin` (la autorización es por rol, sin permiso nuevo).
 * Cualquier otro usuario ve el motivo exacto y no entra; la API responde 403 aunque se salte esta pantalla.
 */
export function PortalPnAccess({ children }: { children: ReactNode }) {
  const [access, setAccess] = useState<Access>("checking");

  useEffect(() => {
    const payload = decodeJwtPayload(getToken());
    // La persona de caja (solo `ot_caja`) entra, pero la página solo le muestra la pestaña Caja.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- el token solo se lee en el navegador
    setAccess(isSuperAdmin(payload) || isOtAdmin(payload) || isSoloCaja(payload) ? "allowed" : "denied");
  }, []);

  if (access === "checking") return null;
  if (access === "denied") {
    return (
      <div
        role="alert"
        data-testid="portal-pn-sin-acceso"
        className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-card px-6 py-14 text-center"
      >
        <IconoAviso tipo="error" className="h-8 w-8" />
        <p className="max-w-md text-sm">{PORTAL_PN_NO_ACCESS_MESSAGE}</p>
      </div>
    );
  }
  return <>{children}</>;
}
