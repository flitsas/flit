"use client";

import { EstadoVacio } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";

// Reportes 2.0 — armazón de F0; lo reemplaza la sesión S3. Solo SuperAdmin (ver .trees/gdc-reportes-CONTRATO.md).
export default function Page() {
  return (
    <Requiere superAdmin>
      <section className="flex flex-col gap-6">
        <Encabezado titulo="Plataforma" />
        <EstadoVacio titulo="En construcción" mensaje="Esta sección de reportes está en preparación." />
      </section>
    </Requiere>
  );
}
