"use client";

import type { ReactNode } from "react";
import { Pestanas } from "@flit/ui/Pestanas";
import type { ReporteCatalogo, TipoReporte } from "@/lib/reportes/tipos";

/**
 * Pestañas de los reportes del catálogo (spec-ui §5.2, patrón `ReportesTabBar` de Trámites): se pinta lo que trae
 * GET /reportes o /backoffice/reportes y nada más, en el orden ya resuelto por el módulo. Solo el panel activo se monta.
 */
export function SelectorReporte({
  reportes,
  activo,
  onCambiar,
  contenido,
}: {
  reportes: ReporteCatalogo[];
  activo: TipoReporte;
  onCambiar: (tipo: TipoReporte) => void;
  contenido: ReactNode;
}) {
  return (
    <Pestanas<TipoReporte>
      ariaLabel="Reportes disponibles"
      active={activo}
      onChange={onCambiar}
      tabs={reportes.map((r) => ({ id: r.tipo, label: r.nombre, title: r.descripcion, content: r.tipo === activo ? contenido : null }))}
    />
  );
}
