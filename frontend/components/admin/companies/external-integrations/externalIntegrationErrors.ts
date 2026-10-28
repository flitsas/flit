// HU #14068 — Mensajes de los errores de la API de integraciones externas de la compañía (HU #14064).
// El backend responde { error, message }; se traduce el código a un texto de pantalla estable.
import { ApiError } from "@/lib/api/types";

const MESSAGES: Record<string, string> = {
  integracion_ya_activa:
    "La compañía ya tiene una integración activa de Persona Natural. Desactívela si quiere crear otra.",
  integracion_inactiva: "La integración está desactivada: no se puede modificar. Cree una nueva.",
  integracion_bloqueada: "La integración está bloqueada. Desbloquéela antes de rotar el secreto.",
  integration_not_found: "La integración ya no existe. Recargue el listado.",
  company_not_found: "La compañía no existe.",
  invalid_integration_type: "Ese tipo de integración no está disponible.",
  invalid_display_name: "El nombre o la finalidad están vacíos o son demasiado largos.",
};

export function externalIntegrationErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    const code = (err.body as { error?: string } | undefined)?.error;
    if (code && MESSAGES[code]) return MESSAGES[code];
    if (err.status === 403) return "Solo un superadministrador puede administrar las integraciones de la compañía.";
    if (err.status === 400) return MESSAGES.invalid_display_name;
  }
  return "No se pudo completar la operación. Inténtelo de nuevo.";
}
