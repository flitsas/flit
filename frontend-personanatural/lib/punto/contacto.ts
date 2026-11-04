// Contacto del radicante en el punto: su identidad es in-situ, así que correo y celular son opcionales.
// Un correo con caracteres fuera de ASCII imprimible (ñ, tildes, espacios) se señala, pero no se guarda ni bloquea.
import type { ErroresActor } from "@/lib/tramite/actores";

export const MENSAJE_CORREO_CARACTERES = "Revisa el correo: tiene caracteres no permitidos.";

export const correoConCaracteresNoPermitidos = (v: string): boolean => /[^\x21-\x7E]/.test(v.trim());

/** Correo apto para enviar/guardar: vacío o con caracteres no permitidos se omite. */
export const correoSeguro = (v: string): string | undefined => {
  const c = v.trim();
  return c === "" || correoConCaracteresNoPermitidos(c) ? undefined : c;
};

/** Quita los errores de contacto del radicante (opcionales) y deja solo el aviso del correo dañado. */
export function erroresRadicantePunto(errores: ErroresActor, correo: string): ErroresActor {
  const { correo: _c, celular: _t, ...resto } = errores;
  void _c;
  void _t;
  return correo.trim() !== "" && correoConCaracteresNoPermitidos(correo) ? { ...resto, correo: MENSAJE_CORREO_CARACTERES } : resto;
}

/** Correo técnico interno que el backend pone al radicante del punto: la persona nunca debe verlo. */
export const esCorreoTecnico = (v: string | null | undefined): boolean => (v ?? "").trim().toLowerCase().startsWith("validaciones+punto-");
