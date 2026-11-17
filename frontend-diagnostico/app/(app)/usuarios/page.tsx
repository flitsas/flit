"use client";

import { Suspense } from "react";
import { VehicleLoader } from "@flit/ui/Loader";
import { PaginaUsuarios } from "@/components/usuarios/PaginaUsuarios";

/**
 * /usuarios (HU #14588, spec-ui §10): una sola ruta para el módulo Usuarios de Diagnóstico. Sin `?empresa`, el admin de
 * compañía ve sus usuarios y el SuperAdmin la tabla de compañías; con `?empresa=<id>` el SuperAdmin entra a esa compañía.
 * Sin gestionarUsuarios, la 403. useSearchParams va bajo Suspense.
 */
export default function Usuarios() {
  return (
    <Suspense fallback={<VehicleLoader label="Cargando usuarios…" />}>
      <PaginaUsuarios />
    </Suspense>
  );
}
