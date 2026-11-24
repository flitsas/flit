import { dia } from "@/components/reportes/gestion/excel";
import { bogotaClock, type XlsxSheet } from "@/lib/xlsx";
import { etiquetaPlantilla, FUENTES, TIPOS_ERROR, type ReportePlataforma } from "./tipos";

const fuente = (f: string) => FUENTES[f] ?? f;

/** Hojas de Plataforma (solo SuperAdmin): aquí SÍ va la fuente (R3). */
export function hojasPlataforma(p: ReportePlataforma): XlsxSheet[] {
  const hojas: XlsxSheet[] = [
    {
      name: "Por fuente",
      columns: [{ header: "Fuente", width: 24 }, { header: "Consultas", width: 12 }, { header: "Con error", width: 12 }, { header: "Obtenidos", width: 12 }, { header: "Nuevos", width: 12 }],
      rows: p.fuentes.map((f) => [fuente(f.fuente), f.consultas, f.conError, f.obtenidos, f.nuevos]),
    },
    {
      name: "Por día y fuente",
      columns: [
        { header: "Día", width: 12 },
        { header: "Fuente", width: 24 },
        { header: "Consultas", width: 12 },
        { header: "Con error", width: 12 },
        { header: "Obtenidos", width: 12 },
        { header: "Nuevos", width: 12 },
      ],
      rows: p.porDia.map((d) => [dia(d.dia), fuente(d.fuente), d.consultas, d.conError, d.obtenidos, d.nuevos]),
    },
  ];
  if (p.erroresPorTipo)
    hojas.push({
      name: "Errores técnicos",
      columns: [{ header: "Fuente", width: 24 }, { header: "Tipo de error", width: 30 }, { header: "Cantidad", width: 12 }],
      rows: p.erroresPorTipo.map((e) => [fuente(e.fuente), TIPOS_ERROR[e.tipo] ?? e.tipo, e.cantidad]),
    });
  hojas.push(
    {
      name: "Empresas con más errores",
      columns: [{ header: "Empresa", width: 36 }, { header: "Consultas con error", width: 18 }, { header: "Correos fallidos o rebotados", width: 26 }],
      rows: p.empresasConMasErrores.map((e) => [e.empresa, e.consultasConError, e.correosFallidos]),
    },
    {
      name: "Motivos de correos",
      columns: [{ header: "Motivo", width: 70 }, { header: "Cantidad", width: 12 }],
      rows: p.correos.motivos.map((m) => [m.motivo, m.cantidad]),
      notes: [`${p.correos.fallidos} fallidos y ${p.correos.rebotados} rebotados en el periodo.`],
    },
    {
      name: "Correos recientes",
      columns: [
        { header: "Fecha", width: 18 },
        { header: "Empresa", width: 30 },
        { header: "Tipo de correo", width: 36 },
        { header: "Asunto", width: 50 },
        { header: "Estado", width: 12 },
        { header: "Motivo", width: 60 },
      ],
      rows: p.correos.recientes.map((c) => [bogotaClock(c.creadoEn), c.empresa, etiquetaPlantilla(c.plantilla), c.asunto, c.estado, c.motivo]),
      notes: ["Los 50 más recientes del periodo."],
    },
  );
  return hojas;
}
