// Lógica pura de la edición de un organismo de tránsito (HU #13463 y #14611). La API vuelve a validar todo.
import type { Organismo } from "@/lib/tipos";

/** «a@x.co, b@x.co» o una por línea → lista limpia. */
export function leerCorreos(texto: string): string[] {
  return texto.split(/[\s,;]+/).filter(Boolean);
}

export interface FormOrganismo {
  correos: string;
  permiteCambioContraventor: boolean;
  admiteDpPorCorreo: boolean;
}

/** Solo lo que cambió: los campos que no se mandan, la API no los toca. */
export function cambiosOrganismo(ot: Organismo, f: FormOrganismo): { correosRadicacion?: string[]; permiteCambioContraventor?: boolean; admiteDpPorCorreo?: boolean } {
  const correos = leerCorreos(f.correos);
  const cambios: ReturnType<typeof cambiosOrganismo> = {};
  if (correos.join("\n") !== ot.correosRadicacion.join("\n")) cambios.correosRadicacion = correos;
  if (f.permiteCambioContraventor !== ot.permiteCambioContraventor) cambios.permiteCambioContraventor = f.permiteCambioContraventor;
  if (f.admiteDpPorCorreo !== (ot.admiteDpPorCorreo ?? false)) cambios.admiteDpPorCorreo = f.admiteDpPorCorreo;
  return cambios;
}

/** Aviso cuando se marca que admite DP por correo pero no hay a dónde mandarlos; null si todo está en orden. */
export function avisoAdmiteSinCorreo(f: Pick<FormOrganismo, "correos" | "admiteDpPorCorreo">): string | null {
  return f.admiteDpPorCorreo && leerCorreos(f.correos).length === 0
    ? "Esta secretaría no tiene correo de radicación: mientras no lo agregues, el envío automático la va a omitir."
    : null;
}
