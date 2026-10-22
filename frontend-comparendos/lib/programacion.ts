// Programación de los trabajos con días y horas (HU #13814, hallazgo B14-6). La base sigue guardando una expresión cron
// porque el programador del backend no cambia; esta pantalla la traduce en los dos sentidos para que nadie la escriba.
// Las horas son de Colombia.

export type ModoProgramacion = "minutos" | "diario" | "semana" | "ultimoDia" | "diaMes";

export interface Programacion {
  modo: ModoProgramacion;
  /** «minutos»: cada cuántos minutos. */
  cada: number;
  /** «HH:mm». En cron todas comparten los minutos (7:00 y 13:00 sí; 7:00 y 13:30 no). */
  horas: string[];
  /** «semana»: 0 = domingo … 6 = sábado. */
  dias: number[];
  /** «diaMes»: día del mes, de 1 a 28 para que exista en todos los meses. */
  diaMes: number;
}

export const MODOS: { valor: ModoProgramacion; texto: string }[] = [
  { valor: "diario", texto: "Todos los días" },
  { valor: "semana", texto: "Algunos días de la semana" },
  { valor: "minutos", texto: "Cada cierto número de minutos" },
  { valor: "ultimoDia", texto: "El último día de cada mes" },
  { valor: "diaMes", texto: "Un día fijo de cada mes" },
];

export const CADA_MINUTOS = [5, 10, 15, 20, 30];

/** Lunes primero, como se lee una semana de trabajo. */
export const DIAS_SEMANA: { valor: number; corto: string }[] = [
  { valor: 1, corto: "Lun" },
  { valor: 2, corto: "Mar" },
  { valor: 3, corto: "Mié" },
  { valor: 4, corto: "Jue" },
  { valor: 5, corto: "Vie" },
  { valor: 6, corto: "Sáb" },
  { valor: 0, corto: "Dom" },
];

const NOMBRES = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

export const PROGRAMACION_INICIAL: Programacion = { modo: "diario", cada: 15, horas: ["06:00"], dias: [1, 2, 3, 4, 5], diaMes: 1 };

const entero = (s: string, min: number, max: number): number | null => {
  if (!/^\d{1,2}$/.test(s)) return null;
  const n = Number(s);
  return n >= min && n <= max ? n : null;
};

const dosDigitos = (n: number) => String(n).padStart(2, "0");

/** «1-5» o «1,3,5» (7 también es domingo) → días; null si trae pasos u otra cosa. */
function leerDias(campo: string): number[] | null {
  const dias = new Set<number>();
  for (const parte of campo.split(",")) {
    const rango = parte.split("-");
    const desde = entero(rango[0] ?? "", 0, 7);
    const hasta = rango.length === 2 ? entero(rango[1] ?? "", 0, 7) : desde;
    if (rango.length > 2 || desde === null || hasta === null || desde > hasta) return null;
    for (let d = desde; d <= hasta; d++) dias.add(d % 7);
  }
  return [...dias];
}

/** La expresión cron como días y horas; null si el selector no sabe representarla (se ofrece reemplazarla). */
export function leerCron(cron: string): Programacion | null {
  const c = cron.trim().split(/\s+/);
  if (c.length !== 5 || c[3] !== "*") return null;
  const [min = "", hora = "", dia = "", , semana = ""] = c;
  const base = { ...PROGRAMACION_INICIAL };

  const paso = /^\*\/(\d{1,2})$/.exec(min);
  if (paso) {
    const cada = Number(paso[1]);
    return hora === "*" && dia === "*" && semana === "*" && cada >= 1 && cada <= 59 ? { ...base, modo: "minutos", cada } : null;
  }

  const minuto = entero(min, 0, 59);
  const horas = hora.split(",").map((h) => entero(h, 0, 23));
  if (minuto === null || horas.some((h) => h === null)) return null;
  const textoHoras = [...new Set(horas as number[])].sort((a, b) => a - b).map((h) => `${dosDigitos(h)}:${dosDigitos(minuto)}`);
  const con = { ...base, horas: textoHoras };

  if (semana === "*") {
    if (dia === "*") return { ...con, modo: "diario" };
    if (dia === "L") return { ...con, modo: "ultimoDia" };
    const d = entero(dia, 1, 28);
    return d === null ? null : { ...con, modo: "diaMes", diaMes: d };
  }
  if (dia !== "*") return null;
  const dias = leerDias(semana);
  if (!dias) return null;
  return dias.length === 7 ? { ...con, modo: "diario" } : { ...con, modo: "semana", dias: ordenarDias(dias) };
}

/** Lunes a domingo. */
export function ordenarDias(dias: number[]): number[] {
  return [...new Set(dias)].sort((a, b) => ((a + 6) % 7) - ((b + 6) % 7));
}

/** Qué falta para guardar; null si está completa. */
export function errorProgramacion(p: Programacion): string | null {
  if (p.modo === "minutos") return p.cada >= 1 && p.cada <= 59 ? null : "Elige cada cuántos minutos.";
  if (p.horas.length === 0) return "Agrega al menos una hora.";
  if (p.horas.some((h) => !/^\d{2}:\d{2}$/.test(h))) return "Completa todas las horas.";
  if (new Set(p.horas.map((h) => h.slice(3))).size > 1) return "Todas las horas deben tener los mismos minutos (por ejemplo 7:00 y 13:00, no 7:00 y 13:30).";
  if (p.modo === "semana" && p.dias.length === 0) return "Marca al menos un día de la semana.";
  if (p.modo === "diaMes" && !(p.diaMes >= 1 && p.diaMes <= 28)) return "El día del mes va de 1 a 28.";
  return null;
}

/** Días seguidos de lunes a sábado como rango («1-5»); lo demás como lista, lunes primero («1,3,0»). */
function campoDias(dias: number[]): string {
  const orden = ordenarDias(dias);
  const seguidos = !orden.includes(0) && orden.length >= 3 && orden.every((d, i) => i === 0 || d === (orden[i - 1] ?? 0) + 1);
  return seguidos ? `${orden[0]}-${orden[orden.length - 1]}` : orden.join(",");
}

/** Días y horas → expresión cron. Asume una programación sin errores (ver errorProgramacion). */
export function construirCron(p: Programacion): string {
  if (p.modo === "minutos") return `*/${p.cada} * * * *`;
  const minuto = Number((p.horas[0] ?? "00:00").slice(3));
  const horas = [...new Set(p.horas.map((h) => Number(h.slice(0, 2))))].sort((a, b) => a - b).join(",");
  const dia = p.modo === "ultimoDia" ? "L" : p.modo === "diaMes" ? String(p.diaMes) : "*";
  const semana = p.modo === "semana" && p.dias.length < 7 ? campoDias(p.dias) : "*";
  return `${minuto} ${horas} ${dia} * ${semana}`;
}

function lista(xs: string[]): string {
  return xs.length === 1 ? (xs[0] ?? "") : `${xs.slice(0, -1).join(", ")} y ${xs[xs.length - 1]}`;
}

/** La programación en palabras, igual que la tabla («De lunes a viernes a las 17:00»). */
export function describirProgramacion(p: Programacion): string {
  if (p.modo === "minutos") return p.cada === 1 ? "Cada minuto" : `Cada ${p.cada} minutos`;
  const horas = [...new Set(p.horas)].sort().map((h) => `${Number(h.slice(0, 2))}:${h.slice(3)}`);
  const cuando = `${horas.length === 1 && horas[0]?.startsWith("1:") ? "a la" : "a las"} ${lista(horas)}`;
  if (p.modo === "diario") return `Todos los días ${cuando}`;
  if (p.modo === "ultimoDia") return `El último día de cada mes ${cuando}`;
  if (p.modo === "diaMes") return `El día ${p.diaMes} de cada mes ${cuando}`;
  const orden = ordenarDias(p.dias);
  if (orden.length === 7) return `Todos los días ${cuando}`;
  const campo = campoDias(orden);
  if (campo.includes("-")) return `De ${NOMBRES[orden[0] ?? 1]} a ${NOMBRES[orden[orden.length - 1] ?? 5]} ${cuando}`;
  const plurales = orden.map((d) => {
    const n = NOMBRES[d] ?? "";
    return n.endsWith("s") ? n : `${n}s`;
  });
  return `Los ${lista(plurales)} ${cuando}`;
}

/** La hora que propone «Otra hora»: una después de la última, con los mismos minutos (el cron los comparte). */
export function siguienteHora(horas: string[]): string {
  const ultima = horas[horas.length - 1] ?? "06:00";
  return `${dosDigitos((Number(ultima.slice(0, 2)) + 1) % 24)}:${ultima.slice(3) || "00"}`;
}

/** Qué hace un trabajo cuando se lanza a mano (HU #13815) y si manda correos reales. */
export function ejecucionManual(tipo: string): { texto: string; correos: boolean; porEmpresa: boolean } {
  switch (tipo) {
    case "AvisosConductor":
      return { texto: "Envía ya los avisos que les tocan hoy a los conductores, sin esperar su hora. Ningún comparendo recibe dos veces el mismo aviso.", correos: true, porEmpresa: true };
    case "ReporteSemanal":
    case "ReporteMensual":
      return { texto: "Envía el cierre a los destinatarios de reportes de cada empresa. Una empresa no lo recibe dos veces el mismo día.", correos: true, porEmpresa: true };
    case "ReintentoContraventor":
      return { texto: "Vuelve a consultar el conductor de los comparendos pendientes o con error (hasta 3 intentos por comparendo).", correos: false, porEmpresa: true };
    case "SincronizacionOrganismos":
      return { texto: "Trae de la plataforma el catálogo de organismos de tránsito.", correos: false, porEmpresa: false };
    default:
      return { texto: "Lanza el trabajo ahora, sin esperar su próxima hora.", correos: false, porEmpresa: false };
  }
}

/** Trabajo de ingesta → canal del diálogo de ingesta («IngestaSimitNit» → «SimitNit»); null si no es de ingesta. */
export function canalDeIngesta(tipo: string): string | null {
  return tipo.startsWith("Ingesta") ? tipo.slice("Ingesta".length) : null;
}
