// Tipos y utilidades puras del dashboard ejecutivo y la productividad (HU #13500 sobre la API de la HU #13498, §6.12 y §6.16).

export interface Kpis {
  deudaActivaTotal: number;
  comparendosConDeuda: number;
  dpFavorables: number;
  dpDesfavorables: number;
  /** 0 a 1; null cuando no hubo respuestas en el periodo. */
  tasaExitoDp: number | null;
  dpSinRespuesta: number;
  ahorroAcumulado: number;
  radarCantidad: number;
  radarValor: number;
  radarHasta: string;
}

export interface PuntoMensual {
  periodo: string;
  ingresados: number;
  valorIngresado: number;
  dpEnviados: number;
  dpFavorables: number;
  ahorro: number;
  pagosCc: number;
  pagosPc: number;
  pagosBc: number;
}

export interface TotalNit {
  nitId: string | null;
  nit: string | null;
  titular: string | null;
  total: number;
  activos: number;
  deudaActiva: number;
  conDp: number;
  pagados: number;
}

export interface Dashboard {
  alcance: { empresa: string | null; nit: string | null; desde: string; hasta: string };
  kpis: Kpis;
  mensual: PuntoMensual[];
  anual: { actual: (PuntoMensual | null)[]; anterior: (PuntoMensual | null)[] };
  porNit: TotalNit[];
}

export interface ComparendoRadar {
  id: string;
  tenantId: string;
  numero: string;
  placa: string | null;
  valorTotal: number | null;
  fechaLimiteGestion: string;
  secretariaTexto: string | null;
}

export interface FilaProductividad {
  usuarioId: string;
  usuario: string;
  gestionados: number;
  dpEnviados: number;
  contraventoresAsignados: number;
  pagos: number;
  avisosManuales: number;
  ocrRegistrados: number;
  respuestasOt: number;
}

export interface ComparendoMetrica {
  id: string;
  tenantId: string;
  numero: string;
  placa: string | null;
  valorTotal: number | null;
  fechaImposicion: string | null;
  pagado: boolean;
  tieneDp: boolean;
  /** Lo que hizo el usuario con el comparendo en el periodo (claves de métrica, sin «gestionados»). */
  acciones: string[];
}

const ACCION: Record<string, string> = {
  dpEnviados: "Envió DP",
  contraventoresAsignados: "Asignó contraventor",
  pagos: "Registró pago",
  avisosManuales: "Envió aviso",
  ocrRegistrados: "Registró por OCR",
  respuestasOt: "Registró respuesta del OT",
};

/** «Registró pago · Envió aviso», en el orden de las columnas; «—» si no hay nada. */
export function accionesProductividad(acciones: string[] | undefined): string {
  const orden = METRICAS.map((m) => m.clave as string).filter((c) => acciones?.includes(c));
  return orden.map((c) => ACCION[c] ?? c).join(" · ") || "—";
}

export type Metrica = Exclude<keyof FilaProductividad, "usuarioId" | "usuario">;

/** Las 7 métricas de §6.12 en el orden de la tabla; la primera es la base si el reporte se usa para facturar (§9.3-Q11). */
export const METRICAS: { clave: Metrica; titulo: string }[] = [
  { clave: "gestionados", titulo: "Comparendos gestionados" },
  { clave: "dpEnviados", titulo: "DPs enviados" },
  { clave: "contraventoresAsignados", titulo: "Contraventores asignados" },
  { clave: "pagos", titulo: "Pagos registrados" },
  { clave: "avisosManuales", titulo: "Avisos manuales" },
  { clave: "ocrRegistrados", titulo: "Registrados por OCR" },
  { clave: "respuestasOt", titulo: "Respuestas de OT" },
];

/** Series que se pueden graficar mes a mes. Los pagos se apilan por tipo. */
export const SERIES = [
  { clave: "ingresados", titulo: "Comparendos ingresados", pesos: false },
  { clave: "valorIngresado", titulo: "Valor ingresado", pesos: true },
  { clave: "dpEnviados", titulo: "DPs enviados", pesos: false },
  { clave: "dpFavorables", titulo: "DPs favorables", pesos: false },
  { clave: "ahorro", titulo: "Ahorro", pesos: true },
  { clave: "pagos", titulo: "Pagos registrados por tipo", pesos: false },
] as const;

export type Serie = (typeof SERIES)[number]["clave"];

const MESES = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

/** «2026-10» → «oct 26»; con `conAnio = false`, solo «oct». */
export function etiquetaMes(periodo: string, conAnio = true): string {
  const [anio, mes] = periodo.split("-");
  const m = MESES[Number(mes) - 1] ?? periodo;
  return conAnio ? `${m} ${anio?.slice(2) ?? ""}`.trim() : m;
}

export const nombreMes = (indice: number): string => MESES[indice] ?? "";

/** Valor de una serie en un punto; «pagos» es la suma de CC, PC y BC. Un mes sin datos vale 0. */
export function valorSerie(p: PuntoMensual | null | undefined, serie: Serie): number {
  if (!p) return 0;
  if (serie === "pagos") return p.pagosCc + p.pagosPc + p.pagosBc;
  return p[serie];
}

/** Tasa de éxito como «66,7 %»; sin respuestas, «—». */
export function formatoTasa(tasa: number | null | undefined): string {
  if (tasa === null || tasa === undefined) return "—";
  const v = Math.round(tasa * 1000) / 10;
  return `${String(v).replace(".", ",")} %`;
}

/** Tope «redondo» del eje Y (1, 2, 2,5 o 5 por potencia de 10) para que las marcas sean legibles. */
export function topeEje(maximo: number): number {
  if (!(maximo > 0)) return 1;
  const potencia = 10 ** Math.floor(Math.log10(maximo));
  for (const paso of [1, 2, 2.5, 5, 10]) if (paso * potencia >= maximo) return paso * potencia;
  return 10 * potencia;
}

/** Número corto para el eje: 1.500.000 → «1,5 M»; 12.000 → «12 mil». */
export function numeroCorto(n: number): string {
  const f = (x: number) => String(Math.round(x * 10) / 10).replace(".", ",");
  if (Math.abs(n) >= 1e9) return `${f(n / 1e9)} mil M`;
  if (Math.abs(n) >= 1e6) return `${f(n / 1e6)} M`;
  if (Math.abs(n) >= 1e3) return `${f(n / 1e3)} mil`;
  return f(n);
}

/** Primer día del mes de una fecha «aaaa-mm-dd» (rango por defecto: el mes en curso, como la API). */
export function inicioDeMes(dia: string): string {
  return `${dia.slice(0, 7)}-01`;
}

/** El rango que acepta la API: desde ≤ hasta y como mucho tres años. Devuelve el mensaje de error o null. */
export function errorRango(desde: string, hasta: string): string | null {
  if (!desde || !hasta) return "Elige las dos fechas del periodo.";
  if (desde > hasta) return "La fecha inicial no puede ser posterior a la final.";
  const dias = (Date.parse(`${hasta}T00:00:00Z`) - Date.parse(`${desde}T00:00:00Z`)) / 86_400_000;
  if (dias > 3 * 366) return "El rango máximo es de tres años.";
  return null;
}

/** Totales de la tabla de productividad por métrica. */
export function totalesProductividad(filas: FilaProductividad[]): Record<Metrica, number> {
  const t = Object.fromEntries(METRICAS.map((m) => [m.clave, 0])) as Record<Metrica, number>;
  for (const f of filas) for (const m of METRICAS) t[m.clave] += f[m.clave];
  return t;
}
