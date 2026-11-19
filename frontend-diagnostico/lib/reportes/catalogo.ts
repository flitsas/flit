// Estado de filtros del módulo de reportes y su reflejo en la URL (diseño §c, SP-18): la URL lleva solo reporte, desde,
// hasta, empresa y los enums del catálogo; la placa nunca sale del estado del componente. Nada va a localStorage ni a
// sessionStorage.
import { leerRango, rangoPorDefecto } from "@/lib/estadisticas/periodo";
import type { FiltrosReporteValor, ReporteCatalogo, TipoReporte } from "./tipos";

/** Tamaños de página del módulo (spec-ui §5.4, Q3) y el tope de desplazamiento del contrato (pagina × tamano ≤ 10 000). */
export const TAMANOS_REPORTE: readonly number[] = [25, 50, 100];
export const TAMANO_REPORTE = 25;
export const TOPE_DESPLAZAMIENTO = 10_000;

export function filtrosPorDefecto(hoy: string): FiltrosReporteValor {
  return { rango: rangoPorDefecto(hoy), empresa: "", filtros: {}, placa: "" };
}

/** Enums del catálogo que el reporte admite (los de tipo `enum`). */
export function filtrosEnum(reporte: ReporteCatalogo | undefined) {
  return (reporte?.filtros ?? []).filter((f) => f.tipo === "enum");
}

export function admitePlaca(reporte: ReporteCatalogo | undefined): boolean {
  return (reporte?.filtros ?? []).some((f) => f.tipo === "prefijo" && f.clave === "placa");
}

export function hayFiltros(valor: FiltrosReporteValor, hoy: string): boolean {
  const base = filtrosPorDefecto(hoy);
  return (
    valor.rango.desde !== base.rango.desde ||
    valor.rango.hasta !== base.rango.hasta ||
    valor.empresa !== "" ||
    valor.placa.trim() !== "" ||
    Object.values(valor.filtros).some((v) => v.length > 0)
  );
}

/** Solo los filtros propios (enums y placa), no el periodo: distingue «vacío del periodo» de «vacío por filtro». */
export function hayFiltrosPropios(valor: FiltrosReporteValor): boolean {
  return valor.placa.trim() !== "" || Object.values(valor.filtros).some((v) => v.length > 0);
}

/** Lee de la URL lo que puede venir en ella: periodo, empresa (solo vista global) y enums válidos del reporte. */
export function leerFiltrosDeUrl(params: URLSearchParams, hoy: string, reporte: ReporteCatalogo | undefined, vistaGlobal: boolean): FiltrosReporteValor {
  const filtros: Record<string, string[]> = {};
  for (const f of filtrosEnum(reporte)) {
    const valores = params.getAll(f.clave).filter((v) => f.valores.includes(v));
    const unicos = [...new Set(valores)];
    if (unicos.length > 0) filtros[f.clave] = f.repetible ? unicos : unicos.slice(0, 1);
  }
  return { rango: leerRango(params, hoy), empresa: vistaGlobal ? (params.get("empresa") ?? "").trim() : "", filtros, placa: "" };
}

/** URL de la pantalla: nunca lleva la placa (SP-18). */
export function urlPantalla(base: string, tipo: TipoReporte, valor: FiltrosReporteValor, vistaGlobal: boolean): string {
  const q = new URLSearchParams({ reporte: tipo, desde: valor.rango.desde, hasta: valor.rango.hasta });
  if (vistaGlobal && valor.empresa) q.set("empresa", valor.empresa);
  for (const [clave, valores] of Object.entries(valor.filtros)) for (const v of valores) q.append(clave, v);
  return `${base}?${q}`;
}
