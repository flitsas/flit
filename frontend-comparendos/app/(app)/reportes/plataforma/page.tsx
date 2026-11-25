"use client";

import { Suspense } from "react";
import { Cargador } from "@flit/ui/Loader";
import { Requiere } from "@/components/Pantalla";
import { PlataformaReporte } from "@/components/reportes/plataforma/PlataformaReporte";

/** Reportes 2.0 › Plataforma (S3): solo SuperAdmin. La API exige además `PoliticaSuperAdmin`. */
export default function Page() {
  return (
    <Requiere superAdmin>
      <Suspense fallback={<Cargador />}>
        <PlataformaReporte />
      </Suspense>
    </Requiere>
  );
}
