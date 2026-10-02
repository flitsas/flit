// HU #13200 — Mensajes de los errores de la API de administración de clientes externos (HU #13088).
// El backend responde { error, message }; se traduce el código a un texto de pantalla estable.
import { ApiError } from "@/lib/api/types";

const MESSAGES: Record<string, string> = {
  invalid_client_id: "El identificador solo admite minúsculas, dígitos y guiones, de 3 a 64 caracteres (p. ej. flito-pdn).",
  client_id_taken: "Ese identificador ya existe o existió. Los identificadores no se reutilizan: use otro.",
  invalid_scopes: "Permisos inválidos: la lectura de trámites es obligatoria.",
  invalid_display_name: "El nombre es obligatorio y admite hasta 120 caracteres.",
  invalid_purpose: "La finalidad es obligatoria y admite hasta 300 caracteres.",
  not_found: "El cliente de integración ya no existe. Recargue el listado.",
};

export function externalClientErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    const code = (err.body as { error?: string } | undefined)?.error;
    if (code && MESSAGES[code]) {
      return MESSAGES[code];
    }
    if (err.status === 403) {
      return "Solo un superadministrador puede administrar clientes de integración.";
    }
    return "No se pudo completar la operación. Inténtelo de nuevo.";
  }
  return "No se pudo completar la operación. Inténtelo de nuevo.";
}
