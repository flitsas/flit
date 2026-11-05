// Nombre autogenerado del Diagnóstico Express (HU #13752, F01-C10; sin fecha desde HU #14087): «Express <identificador>», con
// el identificador normalizado. El usuario lo puede editar; sigue siendo obligatorio. La normalización es la de
// lib/validation/fieldRules (la misma regla de Trámites que aplica el servidor al autogenerar el nombre, HU #13755); se llama
// con un identificador ya validado, así que el tope de 17/8 de esa regla no recorta nada. Los nombres ya creados (con fecha)
// no cambian.
import { validarIdentificador } from "@/lib/validation/fieldRules";

export function nombreExpress(identificador: string): string {
  return `Express ${validarIdentificador(identificador).normalizado}`;
}
