"use client";

// HU #14394 (Feature #14392) — contenido de la subpestaña Cobro del hub del OT, en dos pasos: 1) Precios y 2) Medios de
// pago (mejora sobre la marcha: antes eran dos tablas apiladas). «Definir precio» y «Ir a Precios» llevan al paso 1.
import { useMemo } from "react";
import { usePermissions } from "@/hooks/usePermissions";
import { CobroEnPasos } from "@/components/admin/plataforma/persona-natural/CobroEnPasos";
import { crearCobroApiOt } from "@/lib/api/admin-ot-portal-cobro";
import { OtCobroMediosSection } from "./OtCobroMediosSection";
import { OtPreciosSection } from "./OtPreciosSection";

export function OtCobroTab({ transitOfficeId }: { transitOfficeId: string }) {
  const { isSuperAdmin } = usePermissions();
  // El ot_admin no envía el organismo: lo toma el servidor de su token.
  const api = useMemo(
    () => crearCobroApiOt(isSuperAdmin ? { transitOfficeId } : undefined),
    [isSuperAdmin, transitOfficeId],
  );

  return (
    <CobroEnPasos
      nombrePrecios="Precios"
      precios={(p) => <OtPreciosSection transitOfficeId={transitOfficeId} {...p} />}
      medios={(m) => <OtCobroMediosSection api={api} {...m} />}
    />
  );
}
