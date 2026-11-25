"use client";

import { ContingenciaRuntPage } from "@/components/admin/plataforma/contingencia-runt/ContingenciaRuntPage";
import { ContingenciaRuntPanel } from "@/components/admin/plataforma/contingencia-runt/ContingenciaRuntPanel";

/**
 * Plataforma → Contingencia de proveedor RUNT (Épica #14644, HU #14649).
 * Requiere `runt_contingency.manage` (sembrado solo para el Super Admin FLIT).
 */
export default function AdminContingenciaRuntPage() {
  return (
    <ContingenciaRuntPage>
      <ContingenciaRuntPanel />
    </ContingenciaRuntPage>
  );
}
