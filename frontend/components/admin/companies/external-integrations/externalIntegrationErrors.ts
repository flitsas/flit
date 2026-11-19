// HU #14068 — Mensajes de los errores de la API de integraciones externas de la compañía (HU #14064).
// El backend responde { error, message }; se traduce el código a un texto de pantalla estable.
import { ApiError } from "@/lib/api/types";

const MESSAGES: Record<string, string> = {
  integracion_ya_activa:
    "La compañía ya tiene una conexión activa de Persona Natural. Desactívala si quieres crear otra.",
  integracion_inactiva: "Esta conexión está desactivada y no se puede modificar. Crea una nueva.",
  integracion_bloqueada: "La conexión está bloqueada. Desbloquéala antes de generar una clave nueva.",
  integration_not_found: "Esta conexión ya no existe. Recarga el listado.",
  company_not_found: "La compañía no existe.",
  invalid_integration_type: "Ese tipo de conexión no está disponible.",
  invalid_display_name: "El nombre o la finalidad están vacíos o son demasiado largos. Acórtalos e inténtalo de nuevo.",
};

export function externalIntegrationErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    const code = (err.body as { error?: string } | undefined)?.error;
    if (code && MESSAGES[code]) return MESSAGES[code];
    if (err.status === 403) return "Solo un superadministrador puede administrar las conexiones de la compañía.";
    if (err.status === 400) return MESSAGES.invalid_display_name;
  }
  return "No se pudo completar la operación. Inténtalo de nuevo.";
}
