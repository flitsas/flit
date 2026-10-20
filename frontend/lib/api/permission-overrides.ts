import { apiFetch } from "./client";
import { ApiError } from "./types";

// Permisos individuales por usuario (Épica #12753, ADR-0071). Contrato: core-api.v1.yaml
// (`/api/v1/security/users/{userId}/permission-overrides…`, `/permission-overrides/grantable`,
// `/permission-audit`). Solo administradores; el Super Admin puede declarar el tenant con `tenantId`.

export type OverrideEffect = "allow" | "deny";

export interface GrantablePermissionItem {
  id: string;
  slug: string;
  name: string;
  moduleCode: string;
  productCode: string;
  /**
   * HU #13653 (aditivo): ningún administrador del tenant destino tiene este permiso en su tope. Solo lo calcula la API
   * cuando el Super Admin envía `tenantId`; en cualquier otro caso llega `false` (o ausente en servidores anteriores).
   */
  superAdminOnly?: boolean;
}

/** Permiso que exige la confirmación Ley 1581 antes de otorgarse (HU #13653, FB-17). */
export const PII_REVEAL_SLUG = "ict.pii.reveal";

export interface UserPermissionOverride {
  permissionId: string;
  slug: string;
  productCode: string;
  effect: OverrideEffect;
  source: "INDIVIDUAL" | "MODULE_SNAPSHOT";
  grantedBy: string;
  /** HU #13655: nombre y módulo del permiso, para mostrarlo aunque esté fuera del tope de quien consulta. */
  name?: string | null;
  moduleCode?: string | null;
  /** HU #13647 (FD1-8): quién otorgó el permiso. */
  grantedByName: string | null;
  /** HU #13647 (FD1-8): instante UTC del otorgamiento. */
  grantedAt: string | null;
}

export interface ModuleOverrideResult {
  productCode: string;
  batchId: string;
  created: number;
  skipped: number;
  outOfCap: number;
  excluded: number;
}

export interface PermissionAuditRecord {
  id: string;
  tenantId: string;
  tenantName: string | null;
  actorUserId: string;
  actorName: string | null;
  actorEmail: string | null;
  targetUserId: string;
  targetName: string | null;
  targetEmail: string | null;
  productCode: string | null;
  moduleCode: string | null;
  permissionSlug: string | null;
  /** GRANT | DENY | REVOKE | ROLE_CHANGE_PURGE | PII_REVEAL_CONFIRMED */
  action: string;
  /** OK | REJECTED */
  outcome: string;
  reasonCode: string | null;
  valueBefore: string | null;
  valueAfter: string | null;
  batchId: string | null;
  createdAt: string;
}

export interface PermissionAuditPage {
  items: PermissionAuditRecord[];
  total: number;
  page: number;
  pageSize: number;
}

export interface PermissionAuditFilters {
  userId?: string;
  tenantId?: string;
  actorId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

const base = (userId: string) => `/api/v1/security/users/${userId}/permission-overrides`;

export function listGrantablePermissions(tenantId?: string): Promise<GrantablePermissionItem[]> {
  return apiFetch<GrantablePermissionItem[]>("/api/v1/security/permission-overrides/grantable", {
    query: { tenantId },
  });
}

export function listUserOverrides(userId: string, tenantId?: string): Promise<UserPermissionOverride[]> {
  return apiFetch<UserPermissionOverride[]>(base(userId), { query: { tenantId } });
}

export function setUserOverride(
  userId: string,
  permissionId: string,
  effect: OverrideEffect,
  tenantId?: string,
  /** HU #13653: confirmación Ley 1581; la API la exige para `allow` sobre `ict.pii.reveal` (400 PII_CONFIRMATION_REQUIRED). */
  piiConfirmed?: boolean,
): Promise<{ permissionId: string; effect: OverrideEffect; changed: boolean }> {
  return apiFetch(`${base(userId)}/${permissionId}`, {
    method: "PUT",
    body: piiConfirmed ? { effect, piiConfirmed: true } : { effect },
    query: { tenantId },
  });
}

/** «Volver a lo que dicta el rol»: 204 (404 OVERRIDE_NOT_FOUND si ya no había override). */
export function removeUserOverride(userId: string, permissionId: string, tenantId?: string): Promise<void> {
  return apiFetch<void>(`${base(userId)}/${permissionId}`, { method: "DELETE", query: { tenantId } });
}

export function setModuleOverrides(
  userId: string,
  moduleCode: string,
  effect: OverrideEffect,
  tenantId?: string,
): Promise<ModuleOverrideResult> {
  return apiFetch<ModuleOverrideResult>(`${base(userId)}/modules/${encodeURIComponent(moduleCode)}`, {
    method: "POST",
    body: { effect },
    query: { tenantId },
  });
}

/**
 * Permisos individuales activos del usuario. Con `roleId`, los que se purgarían al asignarle ese rol: el producto del rol
 * nuevo y los de los roles que el reemplazo cierra (`productCode` se ignora).
 */
export function countUserOverrides(
  userId: string,
  productCode?: string,
  tenantId?: string,
  roleId?: string,
): Promise<{ count: number }> {
  return apiFetch<{ count: number }>(`${base(userId)}/count`, { query: { productCode, tenantId, roleId } });
}

export function listPermissionAudit(filters: PermissionAuditFilters = {}): Promise<PermissionAuditPage> {
  return apiFetch<PermissionAuditPage>("/api/v1/security/permission-audit", {
    query: {
      userId: filters.userId,
      tenantId: filters.tenantId,
      actorId: filters.actorId,
      from: filters.from,
      to: filters.to,
      page: filters.page,
      pageSize: filters.pageSize,
    },
  });
}

/** Código de rechazo de la API (`{ code, message }`), si lo trae. */
export function overrideErrorCode(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null;
  const body = error.body as { code?: unknown } | null | undefined;
  return typeof body?.code === "string" ? body.code : null;
}

export const TARGET_SUPERADMIN_MESSAGE = "Los Super Admin no tienen permisos individuales.";
export const INVITATION_PENDING_MESSAGE =
  "Los permisos individuales se podrán configurar cuando el usuario acepte la invitación.";

const REJECTION_MESSAGES: Record<string, string> = {
  OUT_OF_CAP: "Ese permiso queda fuera de lo que puedes otorgar.",
  NOT_ENFORCED: "Ese permiso aún no se controla por usuario, por eso no se puede otorgar.",
  EXCLUDED: "Ese permiso no se configura individualmente.",
  SELF: "No puedes modificar tus propios permisos.",
  TARGET_IS_ADMIN: "Solo el Super Admin modifica los permisos de otro administrador.",
  TARGET_SUPERADMIN: TARGET_SUPERADMIN_MESSAGE,
  OUT_OF_SCOPE: "El usuario no pertenece a la compañía sobre la que operas.",
  USER_NOT_FOUND: "El usuario ya no existe.",
  OVERRIDE_NOT_FOUND: "Ese permiso ya estaba como lo dicta el rol. Actualiza la lista.",
  PERMISSION_NOT_FOUND: "Ese permiso ya no existe en el catálogo.",
  MODULE_NOT_FOUND: "Ese módulo ya no existe en el catálogo.",
  INVITATION_PENDING: INVITATION_PENDING_MESSAGE,
  INVALID_EFFECT: "La acción solicitada no es válida.",
  FAMILY_CREATE_REQUIRES_READ: "Para habilitar «Crear» primero debe estar habilitado «Ver» en ese tipo de servicio.",
  PII_CONFIRMATION_REQUIRED: "Para otorgar este permiso debes confirmar el acceso a datos personales.",
  OVERRIDE_CONFLICT: "Otra persona modificó este permiso al mismo tiempo. Actualiza la lista e inténtalo de nuevo.",
};

/** Texto en español de un código de rechazo (también el `reasonCode` de la auditoría). `null` si no se conoce. */
export function rejectionReasonMessage(code: string | null | undefined): string | null {
  return (code && REJECTION_MESSAGES[code]) || null;
}

/** Mensaje claro en español para un rechazo de la API de permisos individuales. */
export function overrideErrorMessage(
  error: unknown,
  fallback = "No se pudo completar el cambio. Inténtalo de nuevo.",
): string {
  const code = overrideErrorCode(error);
  if (code && REJECTION_MESSAGES[code]) return REJECTION_MESSAGES[code];
  return fallback;
}
