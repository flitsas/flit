import { etiquetaMes, PASOS_GESTION } from "@/lib/reportes";
import type { XlsxSheet } from "@/lib/xlsx";
import type { RespuestaGestion } from "./tipos";

/** Hojas de Gestión: las mismas cifras de la pantalla, una hoja por bloque. */
export function hojasGestion(r: RespuestaGestion, ventana: string): XlsxSheet[] {
  const o = r.gestion.operacion;
  const res = o.resumen;
  return [
    {
      name: "Resumen",
      columns: [{ header: "Indicador", width: 40 }, { header: "Cantidad", width: 14 }, { header: "Valor", width: 18 }],
      rows: [
        ["Pendientes (activos y sin pagar)", res.pendientes, res.valorPendiente],
        ["Vehículos con al menos un pendiente", res.vehiculos, null],
        [`Comparendos que entraron ${ventana}`, res.nuevos30, null],
        [`Dejaron de estar pendientes ${ventana}`, res.resueltos30, null],
      ],
    },
    {
      name: "Gestión",
      columns: [{ header: "Paso", width: 32 }, { header: "Comparendos", width: 14 }],
      rows: o.gestion.pasos.map((p) => [PASOS_GESTION[p.estadoProceso] ?? p.estadoProceso, p.cantidad]),
    },
    {
      name: "Vehículos que más repiten",
      columns: [{ header: "Placa", width: 12 }, { header: "Pendientes", width: 12 }, { header: "Organismos", width: 12 }, { header: "Valor", width: 18 }],
      rows: o.vehiculos.top.map((v) => [v.placa, v.cantidad, v.organismos, v.valor]),
      notes: [`${o.vehiculos.conTresOMas} vehículos con tres o más pendientes.`],
    },
    {
      name: "Infracciones frecuentes",
      columns: [{ header: "Código", width: 10 }, { header: "Infracción", width: 60 }, { header: "Pendientes", width: 12 }, { header: "Valor", width: 18 }],
      rows: o.infracciones.map((i) => [i.codigo, i.descripcion, i.cantidad, i.valor]),
    },
    {
      name: "Lo que se pudo evitar",
      columns: [{ header: "Causa", width: 32 }, { header: "Pendientes", width: 12 }, { header: "Valor", width: 18 }],
      rows: [
        ["SOAT vencido (D02)", o.evitables.soat.cantidad, o.evitables.soat.valor],
        ["Técnico-mecánica vencida (C35)", o.evitables.tecnicoMecanica.cantidad, o.evitables.tecnicoMecanica.valor],
      ],
    },
    {
      name: "Calendario legal",
      columns: [{ header: "Mes", width: 14 }, { header: "Caducan", width: 12 }, { header: "Prescriben", width: 12 }],
      rows: o.calendario.map((m) => [etiquetaMes(m.periodo), m.caducan, m.prescriben]),
    },
    {
      name: "Antigüedad",
      columns: [{ header: "Hace cuánto se impuso", width: 28 }, { header: "Pendientes", width: 12 }, { header: "Valor", width: 18 }],
      rows: o.antiguedad.map((t) => [t.tramo, t.cantidad, t.valor]),
    },
    {
      name: "DP por organismo",
      columns: [
        { header: "Organismo", width: 44 },
        { header: "Enviados", width: 12 },
        { header: "Favorables", width: 12 },
        { header: "Con respuesta", width: 14 },
        { header: "Días hasta la respuesta", width: 22 },
      ],
      rows: o.dpPorOrganismo.map((d) => [d.nombre, d.enviados, d.favorables, d.respondidos, d.diasPromedio]),
      notes: ["Agrupado por el organismo del comparendo."],
    },
    {
      name: "Organismos con más",
      columns: [{ header: "Organismo", width: 44 }, { header: "Pendientes", width: 12 }, { header: "Valor", width: 18 }],
      rows: r.gestion.topOrganismos.map((t) => [t.nombre, t.cantidad, t.valor]),
    },
  ];
}
