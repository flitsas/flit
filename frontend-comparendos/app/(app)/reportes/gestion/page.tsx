"use client";

import { EstadoVacio } from "@flit/ui";
import { Encabezado } from "@/components/Pantalla";
import { RequiereReportes } from "@/components/reportes/RequiereReportes";
import { P } from "@/lib/nav";

// Reportes 2.0 — armazón de F0; lo reemplaza la sesión S3 (ver .trees/gdc-reportes-CONTRATO.md).
export default function Page() {
  return (
    <RequiereReportes permiso={P.reportesVer}>
      <section className="flex flex-col gap-6">
        <Encabezado titulo="Gestión" />
        <EstadoVacio titulo="En construcción" mensaje="Esta sección de reportes está en preparación." />
      </section>
    </RequiereReportes>
  );
}
