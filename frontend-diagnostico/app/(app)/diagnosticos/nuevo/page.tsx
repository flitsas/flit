"use client";

import { Suspense } from "react";
import { VehicleLoader } from "@flit/ui/Loader";
import { NuevoDiagnostico } from "@/components/nuevo/NuevoDiagnostico";

/**
 * Nuevo diagnóstico (HU #13752; pantalla única Express | Masivo, HU #14085): el modo y la placa precargada llegan en la
 * URL (?modo=express|masivo&placa=…), que se lee con useSearchParams bajo Suspense. /diagnosticos/{id}/carga se conserva
 * para retomar un lote CREADO desde el listado.
 */
export default function PaginaNuevoDiagnostico() {
  return (
    <Suspense fallback={<VehicleLoader label="Cargando el formulario…" />}>
      <NuevoDiagnostico />
    </Suspense>
  );
}
