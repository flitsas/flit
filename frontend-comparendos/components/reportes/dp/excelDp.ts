import { dia, porcentaje } from "@/components/reportes/gestion/excel";
import type { XlsxColumn, XlsxSheet } from "@/lib/xlsx";
import type { FilaDp, ReporteDp } from "./tipos";

const COLUMNAS_FILA = (primera: string): XlsxColumn[] => [
  { header: primera, width: 44 },
  { header: "Enviados", width: 12 },
  { header: "Favorables", width: 12 },
  { header: "Desfavorables", width: 14 },
  { header: "Sin respuesta", width: 14 },
  { header: "Tasa de éxito (%)", width: 16 },
  { header: "Días hasta la respuesta", width: 22 },
];
const fila = (f: FilaDp, nombre: string) => [nombre, f.enviados, f.favorables, f.desfavorables, f.sinRespuesta, porcentaje(f.tasaExito), f.diasPromedio];

/** Hojas de Derechos de petición: las mismas cifras de la pantalla (la tasa y el ahorro son los del Resumen). */
export function hojasDp(d: ReporteDp): XlsxSheet[] {
  const hojas: XlsxSheet[] = [
    {
      name: "Indicadores",
      columns: [{ header: "Indicador", width: 44 }, { header: "Valor", width: 18 }],
      rows: [
        ["Tasa de éxito (%)", porcentaje(d.tasaExito)],
        ["Favorables (respondidos en el periodo)", d.favorables],
        ["Desfavorables (respondidos en el periodo)", d.desfavorables],
        ["Ahorro (valor al radicar de los favorables)", d.ahorro],
        ["Enviados (radicados en el periodo)", d.enviados],
        ["Esperando respuesta a hoy", d.sinRespuesta],
        ["Vencidos sin respuesta a hoy", d.totalVencidos],
      ],
    },
    {
      name: "Enviados y respondidos",
      columns: [{ header: "Día", width: 14 }, { header: "Enviados (acumulado)", width: 20 }, { header: "Respondidos (acumulado)", width: 22 }],
      rows: d.acumulado.map((p) => [dia(p.dia), p.enviados, p.respondidos]),
    },
    { name: "Por organismo", columns: COLUMNAS_FILA("Organismo"), rows: d.porOrganismo.map((f) => fila(f, f.nombre)) },
    {
      name: "Por infracción",
      columns: COLUMNAS_FILA("Infracción"),
      rows: d.porInfraccion.map((f) => fila(f, f.clave ? `${f.clave} ${f.nombre}` : f.nombre)),
    },
    {
      name: "Organismo e infracción",
      columns: [
        { header: "Organismo", width: 44 },
        { header: "Infracción", width: 12 },
        { header: "Enviados", width: 12 },
        { header: "Favorables", width: 12 },
        { header: "Desfavorables", width: 14 },
        { header: "Tasa de éxito (%)", width: 16 },
      ],
      rows: d.cruce.map((c) => [c.organismo, c.infraccion, c.enviados, c.favorables, c.desfavorables, porcentaje(c.tasaExito)]),
    },
    {
      name: "Favorables",
      columns: [
        { header: "Comparendo", width: 22 },
        { header: "Placa", width: 10 },
        { header: "Radicado", width: 20 },
        { header: "Organismo", width: 40 },
        { header: "Radicación", width: 12 },
        { header: "Respuesta", width: 12 },
        { header: "Valor al radicar", width: 18 },
      ],
      rows: d.positivos.map((p) => [p.numero, p.placa, p.radicado, p.organismo, dia(p.fechaRadicacion), dia(p.fechaRespuesta), p.valorAlRadicar]),
      notes: d.totalPositivos > d.positivos.length ? [`Se incluyen los ${d.positivos.length} más recientes de ${d.totalPositivos}.`] : undefined,
    },
    {
      name: "Vencidos sin respuesta",
      columns: [
        { header: "Comparendo", width: 22 },
        { header: "Placa", width: 10 },
        { header: "Radicado", width: 20 },
        { header: "Organismo", width: 40 },
        { header: "Radicación", width: 12 },
        { header: "Plazo", width: 12 },
        { header: "Días vencido", width: 12 },
      ],
      rows: d.vencidos.map((v) => [v.numero, v.placa, v.radicado, v.organismo, dia(v.fechaRadicacion), dia(v.fechaLimite), v.diasVencido]),
      notes: d.totalVencidos > d.vencidos.length ? [`Se incluyen los ${d.vencidos.length} más antiguos de ${d.totalVencidos}.`] : undefined,
    },
  ];
  if (d.consolidado)
    hojas.push({
      name: "Consolidado por empresa",
      columns: [
        { header: "Empresa", width: 36 },
        { header: "Organismo", width: 40 },
        { header: "Enviados", width: 12 },
        { header: "Favorables", width: 12 },
        { header: "Desfavorables", width: 14 },
        { header: "Sin respuesta", width: 14 },
        { header: "Tasa de éxito (%)", width: 16 },
      ],
      rows: d.consolidado.map((c) => [c.empresa, c.organismo, c.enviados, c.favorables, c.desfavorables, c.sinRespuesta, porcentaje(c.tasaExito)]),
    });
  return hojas;
}
