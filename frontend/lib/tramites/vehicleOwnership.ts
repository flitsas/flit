// FEATURE 02 — validación en cliente de "solo vehículos propios" en el paso de consulta del traspaso.
// Cuando el tenant activa la política only_own_vehicles, el gestor solo puede consultar vehículos cuyo
// propietario sea la compañía jurídica, es decir un documento tipo NIT igual al NIT del tenant.

/** Deja solo los dígitos de un documento (ignora puntos, guiones y espacios). */
export function normalizeNitDigits(value: string | null | undefined): string {
  return (value ?? "").replace(/\D/g, "");
}

/**
 * Indica si el documento del propietario capturado corresponde a la compañía jurídica del tenant:
 * tipo NIT y número igual al NIT del tenant. Tolera el dígito de verificación y los separadores
 * (uno puede traerlo y el otro no, p. ej. `900123456-7` vs `900123456`).
 */
export function isTenantOwnDocument(
  ownerDocType: string | null | undefined,
  ownerDocNumber: string | null | undefined,
  tenantNit: string | null | undefined,
): boolean {
  if ((ownerDocType ?? "").trim().toUpperCase() !== "NIT") return false;

  const owner = normalizeNitDigits(ownerDocNumber);
  const tenant = normalizeNitDigits(tenantNit);
  if (!owner || !tenant) return false;

  if (owner === tenant) return true;

  // Tolerancia al dígito de verificación: uno lo trae y el otro no.
  return (
    (owner.length === tenant.length + 1 && owner.startsWith(tenant)) ||
    (tenant.length === owner.length + 1 && tenant.startsWith(owner))
  );
}

/** Mensaje al gestor cuando intenta consultar un vehículo cuyo propietario no es la compañía. */
export const OWNER_NOT_TENANT_MESSAGE =
  "Con «solo vehículos propios» activo, solo puedes consultar vehículos cuyo propietario sea la " +
  "compañía. El documento debe ser el NIT de la compañía.";
