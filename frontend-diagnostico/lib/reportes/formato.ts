// Formato de celdas y nombres visibles del módulo de reportes (contrato-api.md §3–§4): fechas dd/mm/aaaa [hh:mm] en hora
// Colombia, moneda COP entera, porcentaje 0..1, números con miles y los enums con su nombre de negocio. Un nulo sale «—».
import { nombreFuente, type Fuente } from "@/lib/configuracion";
import { formatoEntero, formatoPorcentaje } from "@/lib/estadisticas/formato";
import type { ColumnaReporte, ReporteCatalogo, TipoReporte, ValorCelda } from "./tipos";

export const VACIO = "—";

/** Orden de las pestañas (spec-ui §5.2). */
export const ORDEN_REPORTES: readonly TipoReporte[] = ["vehiculos", "consultas", "saneamientos", "derivaciones", "consumo"];

export const NOMBRE_REPORTE: Record<TipoReporte, string> = {
  vehiculos: "Vehículos diagnosticados",
  consultas: "Consultas a fuentes",
  saneamientos: "Saneamientos",
  derivaciones: "Derivaciones a trámite",
  consumo: "Consumo por compañía",
};

export function ordenarCatalogo(reportes: readonly ReporteCatalogo[]): ReporteCatalogo[] {
  const pos = (t: string) => {
    const i = ORDEN_REPORTES.indexOf(t as TipoReporte);
    return i < 0 ? ORDEN_REPORTES.length : i;
  };
  return [...reportes].sort((a, b) => pos(a.tipo) - pos(b.tipo));
}

const NOMBRES_FILTRO: Record<string, string> = {
  modalidad: "Modalidad",
  tipo_analisis: "Tipo de análisis",
  veredicto: "Veredicto",
  fuente: "Fuente",
  estado: "Estado",
  placa: "Placa",
};

export function etiquetaFiltro(clave: string): string {
  return NOMBRES_FILTRO[clave] ?? clave.charAt(0).toUpperCase() + clave.slice(1).replaceAll("_", " ");
}

const ESTADO_CONSULTA: Record<string, string> = { PENDIENTE: "Pendiente", EN_CURSO: "En curso", COMPLETADO: "Completada", ERROR: "Con error", NA: "No aplica" };
const ESTADO_SANEAMIENTO: Record<string, string> = { SOLICITADO: "Solicitado", EN_GESTION: "En gestión", SANEADO: "Saneado", NO_PROCEDE: "No procede", ANULADO: "Anulado" };
const ESTADO_DERIVACION: Record<string, string> = { PENDIENTE: "Pendiente", ENVIADO: "Enviado", CREADO: "Creado", OMITIDO: "Omitido", FALLIDO: "Fallido" };

/** Valores de enum por clave (independientes del tipo de reporte). */
const VALORES: Record<string, Record<string, string>> = {
  modalidad: { EXPRESS: "Express", MASIVO: "Masivo" },
  tipo_analisis: { SIMPLE: "Simple", CON_ANALISIS: "Con análisis" },
  veredicto: { VIABLE: "Viable", BLOQUEADO: "Bloqueado", SIN_DICTAMEN: "Sin dictamen" },
  locatario_origen: { RUNT: "RUNT", DECLARADO: "Declarado" },
  reutilizada: { SI: "Sí", NO: "No" },
  origen: { API: "Automática", MANUAL: "Manual" },
  tipo_operacion: { INDIVIDUAL: "Individual", MASIVA: "Masiva" },
};

const ESTADOS_POR_TIPO: Partial<Record<TipoReporte, Record<string, string>>> = {
  consultas: ESTADO_CONSULTA,
  saneamientos: ESTADO_SANEAMIENTO,
  derivaciones: ESTADO_DERIVACION,
};

/** Nombre de negocio de un valor de enum; sin traducción conocida devuelve null. */
function nombreDeValor(tipo: TipoReporte, clave: string, valor: string): string | null {
  if (clave === "fuente") return nombreFuente(valor as Fuente);
  if (clave === "estado") return ESTADOS_POR_TIPO[tipo]?.[valor] ?? null;
  if (clave === "saneamiento_estado") return ESTADO_SANEAMIENTO[valor] ?? null;
  return VALORES[clave]?.[valor] ?? null;
}

/** Rótulo de un valor de filtro; un valor sin traducción se humaniza (nunca se muestra con guiones bajos). */
export function etiquetaValor(tipo: TipoReporte, clave: string, valor: string): string {
  const nombre = nombreDeValor(tipo, clave, valor);
  if (nombre) return nombre;
  const texto = valor.toLowerCase().replaceAll("_", " ");
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

const FECHA_HORA = new Intl.DateTimeFormat("es-CO", {
  timeZone: "America/Bogota",
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

function fechaHora(valor: string): string | null {
  const d = new Date(valor);
  if (Number.isNaN(d.getTime())) return null;
  const p = Object.fromEntries(FECHA_HORA.formatToParts(d).map((x) => [x.type, x.value]));
  return `${p.day}/${p.month}/${p.year} ${p.hour}:${p.minute}`;
}

function fecha(valor: string): string | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(valor);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : null;
}

/** Texto de una celda según el tipo de su columna. Un valor que no es del tipo declarado se muestra tal cual. */
export function formatoCelda(valor: ValorCelda | undefined, columna: ColumnaReporte, tipo: TipoReporte): string {
  if (valor === null || valor === undefined || valor === "") return VACIO;
  switch (columna.tipo) {
    case "fecha":
      return typeof valor === "string" ? (fecha(valor) ?? valor) : String(valor);
    case "fecha_hora":
      return typeof valor === "string" ? (fechaHora(valor) ?? valor) : String(valor);
    case "numero":
      return typeof valor === "number" ? formatoEntero(valor) : String(valor);
    case "moneda":
      return typeof valor === "number" ? `$ ${formatoEntero(valor)}` : String(valor);
    case "porcentaje":
      return typeof valor === "number" ? formatoPorcentaje(valor) : String(valor);
    default:
      return typeof valor === "string" ? (nombreDeValor(tipo, columna.clave, valor) ?? valor) : String(valor);
  }
}
