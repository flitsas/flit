"use client";

import { type ReactNode, useEffect, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { notFound, useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { getToken } from "@/lib/api/client";
import { canManageRuntContingency, decodeJwtPayload, type JwtPayload } from "@/lib/auth/jwt";

/**
 * Armazón de Plataforma → Contingencia de proveedor RUNT (HU #14649): volver, título y la guarda del
 * permiso `runt_contingency.manage`. Los claims se leen tras montar (como Confirmación RUNT) para no
 * desajustar la hidratación; sin el permiso se responde con el not-found del segmento (AC6).
 */
export function ContingenciaRuntPage({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [claims, setClaims] = useState<JwtPayload | null | undefined>(undefined);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setClaims(decodeJwtPayload(getToken()));
  }, []);

  if (claims === undefined) return null;
  if (!canManageRuntContingency(claims)) notFound();

  return (
    <ToastProvider>
      <div className="flex flex-col gap-4 px-4 pt-6 pb-24 md:px-6">
        <button
          type="button"
          onClick={() => router.push("/")}
          className="flex w-fit items-center gap-1.5 text-xs font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
          style={{ color: "var(--color-flit-brand)" }}
        >
          <Icono nombre="izquierda" className="h-3.5 w-3.5" />
          Volver al inicio
        </button>

        <ModuleTitle
          title="Contingencia de proveedor RUNT"
          subtitle="Si un proveedor del RUNT se cae, elige con cuál consultar mientras tanto. Aplica a toda la plataforma."
        />

        {/* Sin tarjeta detrás: como /tramites, la tabla va directo sobre el fondo de la página. */}
        <div className="flex flex-col">{children}</div>
      </div>
    </ToastProvider>
  );
}
