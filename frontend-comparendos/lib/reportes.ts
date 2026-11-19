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
  return METRICAS.map((m) => m.clave as string).filter((c) => acciones?.includes(c)).map((c) => ACCION[c] ?? c).join(" · ") || "—";
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
  const f = (x: number) => String(x).replace(".", ",");
  const r = (x: number) => Math.round(x * 10) / 10;
  // Se sube de escala cuando el redondeo en la escala menor llega a 1.000: 999.950.000 da «1 mil M», no «1000 M»,
  // y 960.000.000 sigue siendo «960 M».
  const a = Math.abs(n);
  if (a >= 1e9 || r(a / 1e6) >= 1000) return `${f(r(n / 1e9))} mil M`;
  if (a >= 1e6 || r(a / 1e3) >= 1000) return `${f(r(n / 1e6))} M`;
  if (a >= 1e3 || r(a) >= 1000) return `${f(r(n / 1e3))} mil`;
  return f(r(n));
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

// ── Lente de dinero (rediseño de Comparendos, S2: GET /reportes/dinero) ──────────────────────────────────────────────

export interface Monto {
  cantidad: number;
  valor: number;
}

export interface DineroEstado extends Monto {
  estado: "SinNotificar" | "EnTiempo" | "Vencido" | "Caducado" | "Prescrito";
}

export interface DineroOrganismo extends Monto {
  organismoId: string | null;
  nombre: string;
  /** La parte que todavía se puede cobrar (sin notificar, en tiempo o vencido). */
  exigible: number;
}

export interface Frentes {
  proximosAVencer: Monto;
  sinContraventor: Monto;
  rechazadosOt: Monto;
  sinGestion: Monto;
  vencidos: Monto;
  caducidadInmediata: Monto;
}

export interface Dinero {
  hoy: string;
  /** Deuda (activo, sin pagar y sin DP favorable) repartida por estado legal. */
  estados: DineroEstado[];
  exigible: Monto;
  noExigible: Monto;
  cobroCoactivo: Monto;
  descuento: { d50: Monto & { ahorroEstimado: number }; d25: Monto & { ahorroEstimado: number } };
  organismos: DineroOrganismo[];
  pagado: { cantidad: number; total: number; cc: number; pc: number; bc: number };
  frentes: Frentes;
  ahorroLogrado: number;
  mensual: { periodo: string; valorIngresado: number; pagado: number }[];
}

export interface RespuestaDinero {
  alcance: { empresa: string | null; nit: string | null; desde: string; hasta: string };
  dinero: Dinero;
}

/** Pesos abreviados para las cifras grandes: 3.900.000.000 → «$ 3,9 mil M»; 126.200.000 → «$ 126,2 M». */
export function pesosCorto(valor: number | null | undefined): string {
  if (valor === null || valor === undefined) return "—";
  if (Math.abs(valor) < 1e6) return `$ ${Math.round(valor).toLocaleString("es-CO")}`;
  return `$ ${numeroCorto(valor)}`;
}

/** Un frente de «Para hoy»: lo que se cuenta, a dónde lleva en la bandeja y si es plata que urge o que se recupera. */
export interface DefFrente {
  clave: keyof Frentes;
  titulo: string;
  /** Qué hacer, en una línea. */
  accion: string;
  /** Filtro de la bandeja con las mismas claves de `aQuery`. */
  query: Record<string, string>;
  pendiente?: boolean;
}

export const FRENTES: DefFrente[] = [
  { clave: "proximosAVencer", titulo: "Próximos a vencer", accion: "Vencen en 2 días hábiles o menos", query: { rapido: "ProximosAVencer" }, pendiente: true },
  { clave: "caducidadInmediata", titulo: "Caducidad inmediata", accion: "Caducados o por caducar en 30 días, sin DP", query: { juridico: "CaducidadInmediata" } },
  { clave: "sinContraventor", titulo: "Sin contraventor", accion: "Falta el conductor responsable", query: { rapido: "SinContraventor" } },
  { clave: "rechazadosOt", titulo: "Rechazados por el organismo", accion: "El cambio de contraventor no fue aceptado", query: { rapido: "RechazadosOt" }, pendiente: true },
  { clave: "sinGestion", titulo: "Sin gestión", accion: "Nadie ha hecho nada todavía", query: { sinGestion: "true" } },
  { clave: "vencidos", titulo: "Vencidos", accion: "Se pasó el plazo de gestión", query: { estadoLegal: "Vencido" } },
];

/** Enlace a la bandeja con el filtro del frente (y la empresa del SuperAdmin o el NIT, si los hay). */
export function hrefBandeja(query: Record<string, string>, extra: { empresa?: string | null; nit?: string | null } = {}): string {
  const q = new URLSearchParams(query);
  if (extra.empresa) q.set("empresa", extra.empresa);
  if (extra.nit) q.set("nit", extra.nit);
  return `/bandeja?${q.toString()}`;
}

/** «noviembre de 2026» o «1 al 19 de noviembre de 2026» según el rango; es el periodo visible de «Pagado» y «Ahorro». */
export function textoPeriodo(desde: string, hasta: string): string {
  const mes = (p: string) => new Intl.DateTimeFormat("es-CO", { month: "long", timeZone: "UTC" }).format(new Date(`${p}T00:00:00Z`));
  const [ad, md, dd] = desde.split("-");
  const [ah, mh, dh] = hasta.split("-");
  if (ad === ah && md === mh) return `${Number(dd)} al ${Number(dh)} de ${mes(hasta)} de ${ah}`;
  return `${Number(dd)} de ${mes(desde)} de ${ad} al ${Number(dh)} de ${mes(hasta)} de ${ah}`;
}

/** `GET /reportes/dinero?partes=estados`: solo el reparto por estado (el indicador «Deuda sin pagar» de Reportes). */
export interface RespuestaEstados {
  alcance: RespuestaDinero["alcance"];
  dinero: Pick<Dinero, "hoy" | "estados" | "exigible" | "noExigible">;
}
