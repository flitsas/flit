"use client";

// HU #14399 (Feature #14396, Épica #14297) — «Reportes» del portal en el hub del OT: el mismo `PortalReportes` con el
// cliente del OT. El `ot_admin` no envía organismo (lo toma el servidor de su token) y no ve filtro de organismo; el
// SuperAdmin trabaja con el organismo del hub.
import { useMemo } from "react";
import { usePermissions } from "@/hooks/usePermissions";
import { crearReporteOtApi } from "@/lib/api/admin-portal-reportes";
import { fetchTiposTarifaOt } from "@/lib/api/admin-ot-portal-tarifas";
import { PortalReportes } from "./PortalReportes";

export function OtReportesSection({ transitOfficeId }: { transitOfficeId: string }) {
  const { isSuperAdmin } = usePermissions();

  const api = useMemo(() => {
    const scope = isSuperAdmin ? { transitOfficeId } : undefined;
    return crearReporteOtApi(scope, async (signal) =>
      (await fetchTiposTarifaOt(scope, signal)).map((t) => ({ id: t.id, nombre: t.nombre })),
    );
  }, [isSuperAdmin, transitOfficeId]);

  return <PortalReportes api={api} tarjeta={false} />;
}
