"use client";

import { Suspense } from "react";
import { Cargador } from "@flit/ui/Loader";
import { GestionReporte } from "@/components/reportes/gestion/GestionReporte";
import { RequiereReportes } from "@/components/reportes/RequiereReportes";
import { P } from "@/lib/nav";

/** Reportes 2.0 › Gestión (S3): la lente de operación con periodo y NIT, que salió del Inicio. */
export default function Page() {
  return (
    <RequiereReportes permiso={P.reportesVer}>
      <Suspense fallback={<Cargador />}>
        <GestionReporte />
      </Suspense>
    </RequiereReportes>
  );
}
