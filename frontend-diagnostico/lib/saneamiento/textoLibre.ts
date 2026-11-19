// Textos libres del saneamiento que escribe FLIT y ve la compañía (nota, observación, motivo, descripción de OTRO).
// SP-13 con la decisión del usuario E3 (2026-11-14): largo en puntos de código y sin caracteres de control ni de
// dirección bidireccional; los dígitos seguidos se permiten. Es una ayuda del cliente: el servidor sigue mandando.
// Los mensajes son fijos y nunca repiten lo digitado.
import { largoTexto } from "@/lib/backoffice/ajusteDictamen";

export interface LimiteTexto {
  /** 0: opcional (vacío se manda como null). */
  min: number;
  max: number;
}

/** Límites del contrato (plan §3.2, §6.2; schema): nota 500, observación 1000, motivo 10..500, descripción 3..200. */
export const LIMITES_TEXTO = {
  nota: { min: 0, max: 500 },
  observacion: { min: 0, max: 1000 },
  motivo: { min: 10, max: 500 },
  descripcion: { min: 3, max: 200 },
} as const satisfies Record<string, LimiteTexto>;

// Controles C0 y C1 salvo el salto de línea, el retorno y el tabulador (una nota puede tener varias líneas), y los de
// dirección: U+202A a U+202E y U+2066 a U+2069.
const NO_PERMITIDOS = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F\u202A-\u202E\u2066-\u2069]/;

/** null si el texto sirve (ya recortado); si no, el mensaje fijo del problema. */
export function validarTextoLibre(texto: string, limite: LimiteTexto): string | null {
  const recortado = texto.trim();
  if (NO_PERMITIDOS.test(recortado)) return "El texto tiene caracteres no permitidos.";
  const largo = largoTexto(recortado);
  if (largo < limite.min) return `Escribe al menos ${limite.min} caracteres.`;
  if (largo > limite.max) return `Admite máximo ${limite.max} caracteres.`;
  return null;
}
