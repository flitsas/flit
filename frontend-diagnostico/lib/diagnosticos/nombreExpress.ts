// Nombre autogenerado del Diagnóstico Express (HU #13752, F01-C10): «Express <identificador> <dd/mm/aaaa>», con el
// identificador normalizado y el día en la hora de Colombia, que es la del producto. El usuario lo puede editar; sigue
// siendo obligatorio. La normalización es la de lib/validation/fieldRules (la misma regla de Trámites que aplica el
// servidor al autogenerar el nombre, HU #13755); se llama con un identificador ya validado, así que el tope de 17/8 de
// esa regla no recorta nada.
import { validarIdentificador } from "@/lib/validation/fieldRules";

const FORMATO_FECHA = new Intl.DateTimeFormat("es-CO", { timeZone: "America/Bogota", day: "2-digit", month: "2-digit", year: "numeric" });

export function nombreExpress(identificador: string, fecha: Date): string {
  const partes = FORMATO_FECHA.formatToParts(fecha);
  const parte = (tipo: Intl.DateTimeFormatPartTypes) => partes.find((p) => p.type === tipo)?.value ?? "";
  return `Express ${validarIdentificador(identificador).normalizado} ${parte("day")}/${parte("month")}/${parte("year")}`;
}
