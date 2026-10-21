// Nombre autogenerado del Diagnóstico Express (HU #13752, F01-C10): «Express <identificador> <dd/mm/aaaa>», con el
// identificador normalizado (mayúsculas, sin espacios) y el día en la hora de Colombia, que es la del producto. El
// usuario lo puede editar; sigue siendo obligatorio.
const FORMATO_FECHA = new Intl.DateTimeFormat("es-CO", { timeZone: "America/Bogota", day: "2-digit", month: "2-digit", year: "numeric" });

export function normalizarIdentificador(identificador: string): string {
  return identificador.replace(/\s+/g, "").toUpperCase();
}

export function nombreExpress(identificador: string, fecha: Date): string {
  const partes = FORMATO_FECHA.formatToParts(fecha);
  const parte = (tipo: Intl.DateTimeFormatPartTypes) => partes.find((p) => p.type === tipo)?.value ?? "";
  return `Express ${normalizarIdentificador(identificador)} ${parte("day")}/${parte("month")}/${parte("year")}`;
}
