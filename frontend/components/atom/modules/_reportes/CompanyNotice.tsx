"use client";

// Aviso para SuperAdmin sin compañía elegida (Reportes 2.0, HU-C): los endpoints
// nuevos de métricas EXIGEN tenantId para SuperAdmin (§4), así que en lugar de
// llamar a la API se muestra esta indicación.
import { Icono } from "@flit/ui/Icono";

export function CompanyNotice({ message }: { message?: string }) {
  return (
    <div
      className="flex flex-col items-center justify-center gap-3 rounded-2xl border p-8 text-center bg-card"
      data-testid="aviso-selecciona-compania"
    >
      <Icono nombre="empresas" className="h-8 w-8" style={{ color: "var(--color-flit-brand)" }} />
      <p className="text-sm font-medium">Selecciona una compañía</p>
      <p className="text-xs opacity-70 max-w-md">
        {message ??
          "Como SuperAdmin debes elegir una compañía en el filtro superior para consultar estas métricas."}
      </p>
    </div>
  );
}
