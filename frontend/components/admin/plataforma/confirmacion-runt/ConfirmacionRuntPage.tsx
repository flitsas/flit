"use client";

import { type ReactNode, useEffect, useState } from "react";
import { ArrowLeft } from "lucide-react";
import { notFound, useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, type JwtPayload } from "@/lib/auth/jwt";
import { ConfirmacionRuntTabs } from "./ConfirmacionRuntTabs";
import {
  canSeeConfirmacionRuntTab,
  type ConfirmacionRuntTabId,
  visibleConfirmacionRuntTabs,
} from "./confirmacion-runt-nav";

/**
 * Armazón compartida de las dos pestañas de Plataforma → Confirmación RUNT (HU #12313): título,
 * barra de pestañas gateada por permiso y el gate de la propia pestaña.
 *
 * Los claims se leen TRAS montar (patrón de `useNavigableModules`): en el render del servidor no
 * hay cookie y leerlos durante el render daría un desajuste de hidratación. Mientras no se
 * resuelven no se pinta nada — deny-by-default — y si el usuario no tiene el permiso de ESTA
 * pestaña se responde con el not-found del segmento, no con un redirect a la otra (que sería
 * adivinar por él).
 */
export function ConfirmacionRuntPage({
  tab,
  subtitle,
  children,
}: {
  tab: ConfirmacionRuntTabId;
  subtitle: string;
  children: ReactNode;
}) {
  const router = useRouter();
  const [claims, setClaims] = useState<JwtPayload | null | undefined>(undefined);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setClaims(decodeJwtPayload(getToken()));
  }, []);

  if (claims === undefined) return null;
  if (!canSeeConfirmacionRuntTab(claims, tab)) notFound();

  return (
    <ToastProvider>
      <div className="flex flex-col gap-4 px-4 pt-6 pb-24 md:px-6">
        <button
          type="button"
          onClick={() => router.push("/")}
          className="flex w-fit items-center gap-1.5 text-xs font-semibold"
          style={{ color: "#557EFF" }}
        >
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
          Volver al inicio
        </button>

        <ModuleTitle title="Confirmación RUNT" subtitle={subtitle} />

        {/* Sin tarjeta detrás: como /tramites, la tabla va directo sobre el fondo de la página (regla de UI/UX). */}
        <ConfirmacionRuntTabs tabs={visibleConfirmacionRuntTabs(claims)} activeId={tab} />
        <div className="flex flex-col">{children}</div>
      </div>
    </ToastProvider>
  );
}
