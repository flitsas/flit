import { hasPermission, isSuperAdmin, type JwtPayload } from '@/lib/auth/jwt';

/** Permiso de escritura del módulo Validación de Identidad (backend: `IdentityModuleAccess.Manage`). */
export const IDENTITY_MODULE_MANAGE_PERMISSION = 'validaciones.manage';

/**
 * Puede GESTIONAR el módulo de identidad: misma regla que `RequireIdentityModuleManage`
 * (`IdentityModuleAccessFilter`): SuperAdmin, o `validaciones.manage` en un usuario que NO sea de un
 * organismo de tránsito. Solo decide qué ofrece la interfaz; la autorización real la hace la API.
 */
export function puedeGestionarIdentidad(payload: JwtPayload | null): boolean {
  if (isSuperAdmin(payload)) return true;
  if (!payload || payload.entity_type === 'TRANSIT_OFFICE') return false;
  return hasPermission(payload, IDENTITY_MODULE_MANAGE_PERMISSION);
}
