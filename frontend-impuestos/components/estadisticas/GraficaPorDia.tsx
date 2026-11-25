import { rotuloDia } from "@/lib/estadisticas/formato";
import type { DashboardImpuestos } from "@/lib/estadisticas/tipos";
import { BarrasSerie } from "./BarrasSerie";

/**
 * «Liquidaciones por día» (spec-visual §3.1) con la forma de BarrasSerie. El contrato trae una sola cifra por día
 * (`serie[].total`), sin separar por placa y masivas: va una serie en `--chart-1` (hueco reportado en el informe de M17).
 */
export function GraficaPorDia({ serie }: { serie: NonNullable<DashboardImpuestos["serie"]> }) {
  return (
    <BarrasSerie
      nivel={2}
      titulo="Liquidaciones por día"
      subtitulo="Liquidaciones creadas cada día del periodo"
      etiquetas={serie.map((p) => rotuloDia(p.fecha))}
      series={[{ nombre: "Liquidaciones", color: "var(--chart-1)", valores: serie.map((p) => p.total) }]}
    />
  );
}
