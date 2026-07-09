"use client";

// Aviso para SuperAdmin sin compañía elegida (Reportes 2.0, HU-C): los endpoints
// nuevos de métricas EXIGEN tenantId para SuperAdmin (§4), así que en lugar de
// llamar a la API se muestra esta indicación.
import { Building2 } from "lucide-react";

export function CompanyNotice({ message }: { message?: string }) {
  return (
    <div
      className="flex flex-col items-center justify-center gap-3 rounded-2xl border p-8 text-center bg-white dark:bg-[#0B0F14]"
      data-testid="aviso-selecciona-compania"
    >
      <Building2 className="h-8 w-8" style={{ color: "#557EFF" }} aria-hidden="true" />
      <p className="text-sm font-medium">Selecciona una compañía</p>
      <p className="text-xs opacity-70 max-w-md">
        {message ??
          "Como SuperAdmin debes elegir una compañía en el filtro superior para consultar estas métricas."}
      </p>
    </div>
  );
}
