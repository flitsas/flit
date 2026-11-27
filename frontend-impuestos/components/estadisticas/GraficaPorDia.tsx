import { rotuloDia } from "@/lib/estadisticas/formato";
import type { DashboardImpuestos } from "@/lib/estadisticas/tipos";
import { BarrasSerie, type SerieBarras } from "./BarrasSerie";

/** `Dashboard.serie[].x-flit-etiquetas` (impuestos.v1.yaml 1.1.0, delta 10). */
const ETIQUETA_INDIVIDUALES = "Individuales";
const ETIQUETA_MASIVAS = "Masivas";

type Serie = NonNullable<DashboardImpuestos["serie"]>;

/** ¿El back separa la serie (delta 10)? Con algún día sin desglose se pinta solo el total, para no inventar ceros. */
function conDesglose(serie: Serie): boolean {
  return serie.length > 0 && serie.every((p) => typeof p.individuales === "number" && typeof p.masivas === "number");
}

/**
 * «Liquidaciones por día» (spec-visual §3.1) con la forma de BarrasSerie: dos series agrupadas por día, individuales en
 * `--chart-1` y masivas en `--chart-2`, con las etiquetas del contrato en la leyenda y en «Ver datos». Un back anterior a
 * la 1.1.0 (sin `individuales`/`masivas`) se pinta con una sola serie del total.
 */
export function GraficaPorDia({ serie }: { serie: Serie }) {
  const series: SerieBarras[] = conDesglose(serie)
    ? [
        { nombre: ETIQUETA_INDIVIDUALES, color: "var(--chart-1)", valores: serie.map((p) => p.individuales ?? 0) },
        { nombre: ETIQUETA_MASIVAS, color: "var(--chart-2)", valores: serie.map((p) => p.masivas ?? 0) },
      ]
    : [{ nombre: "Liquidaciones", color: "var(--chart-1)", valores: serie.map((p) => p.total) }];
  return (
    <BarrasSerie
      nivel={2}
      titulo="Liquidaciones por día"
      subtitulo="Liquidaciones creadas cada día del periodo"
      etiquetas={serie.map((p) => rotuloDia(p.fecha))}
      series={series}
    />
  );
}
