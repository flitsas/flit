"use client";

import { Suspense } from "react";
import { Cargador } from "@flit/ui/Loader";
import { DpReporte } from "@/components/reportes/dp/DpReporte";
import { RequiereReportes } from "@/components/reportes/RequiereReportes";
import { P } from "@/lib/nav";

/** Reportes 2.0 › Derechos de petición (S3). */
export default function Page() {
  return (
    <RequiereReportes permiso={P.reportesVer}>
      <Suspense fallback={<Cargador />}>
        <DpReporte />
      </Suspense>
    </RequiereReportes>
  );
}
