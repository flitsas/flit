"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Radar } from "lucide-react";
import { usePermissions } from "@/hooks/usePermissions";
import { LOG_QX_READ_PERMISSION } from "@/lib/auth/jwt";
import { fetchLogQx } from "@/lib/api/admin-log-qx";

/**
 * Enlace "Ver LOG QX" del detalle de un trámite (HU #10796). Correlaciona el trámite con su
 * trazabilidad Quipux: navega a `/?m=log-qx&instanceId={id}` (el módulo LOG QX auto-filtra por
 * ese trámite al montar).
 *
 * Se muestra SOLO si (AC3):
 *  1. el usuario puede leer el LOG QX (`logqx.read` o SuperAdmin) — si no, no se dispara fetch, y
 *  2. el trámite tiene realmente una radicación Quipux — se comprueba reutilizando el endpoint ya
 *     existente `GET /api/v1/admin/log-qx?instanceId=…` (`totalCount > 0`), sin backend nuevo.
 *
 * Mientras carga o si no hay radicación (o el probe falla) → no renderiza nada: nunca un enlace roto.
 */
export function LogQxLink({ instanceId }: { instanceId: string }) {
  const { permissions, isSuperAdmin } = usePermissions();
  const canRead = isSuperAdmin || permissions.includes(LOG_QX_READ_PERMISSION);

  const [hasRadicacion, setHasRadicacion] = useState(false);

  useEffect(() => {
    if (!canRead) return;
    const controller = new AbortController();
    let active = true;
    fetchLogQx({ instanceId, pageSize: 1 }, controller.signal)
      .then((page) => {
        if (active) setHasRadicacion(page.totalCount > 0);
      })
      .catch(() => {
        // Probe fallido (red / abort): se trata como "sin radicación" — no se ofrece enlace.
        if (active) setHasRadicacion(false);
      });
    return () => {
      active = false;
      controller.abort();
    };
  }, [canRead, instanceId]);

  if (!canRead || !hasRadicacion) {
    return null;
  }

  return (
    <Link
      href={`/?m=log-qx&instanceId=${instanceId}`}
      aria-label="Ver el LOG QX de este trámite"
      // `border` a secas, no `border-[#DFE5ED]`: el token `--border` ya ES ese gris de marca en
      // claro y pasa a white/10 en oscuro (HU #10492). Fijar el hex dejaba una línea clara
      // flotando en modo oscuro, que es justo lo que esa HU vino a arreglar.
      className="inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-[13px] font-semibold text-[#557EFF] hover:bg-[#557EFF]/[0.08] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
    >
      <Radar className="h-4 w-4" aria-hidden="true" /> Ver LOG QX
    </Link>
  );
}
