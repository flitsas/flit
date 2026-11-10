"use client";

// HU #14394 (Feature #14392) — contenido de la subpestaña Cobro del hub del OT: los medios de pago y, debajo, la
// sección de Precios tal como estaba. El acceso «Ir a Precios» lleva el foco a su título.
import { useCallback, useMemo } from "react";
import { usePermissions } from "@/hooks/usePermissions";
import { crearCobroApiOt } from "@/lib/api/admin-ot-portal-cobro";
import { OtCobroMediosSection } from "./OtCobroMediosSection";
import { OtPreciosSection } from "./OtPreciosSection";

const ANCLA_PRECIOS = "portal-pn-precios";

export function OtCobroTab({ transitOfficeId }: { transitOfficeId: string }) {
  const { isSuperAdmin } = usePermissions();
  // El ot_admin no envía el organismo: lo toma el servidor de su token.
  const api = useMemo(
    () => crearCobroApiOt(isSuperAdmin ? { transitOfficeId } : undefined),
    [isSuperAdmin, transitOfficeId],
  );

  const irAPrecios = useCallback(() => {
    const seccion = document.getElementById(ANCLA_PRECIOS);
    if (!seccion) return;
    seccion.scrollIntoView?.({ block: "start" });
    seccion.focus({ preventScroll: true });
  }, []);

  return (
    <div className="flex flex-col gap-8">
      <OtCobroMediosSection api={api} onIrAPrecios={irAPrecios} tarjeta={false} />
      <div id={ANCLA_PRECIOS} tabIndex={-1} className="focus:outline-none">
        <OtPreciosSection transitOfficeId={transitOfficeId} />
      </div>
    </div>
  );
}
