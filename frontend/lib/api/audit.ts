// Cliente tipado del rastro unificado de auditoría administrativa/seguridad (HU #10680).
// Un único endpoint del contrato (`contracts/openapi/core-api.v1.yaml`, HU #10679).
import { apiFetch } from "./client";
import type { AdminAuditLogPageResponse, AdminAuditLogQuery } from "./types";

const base = "/api/v1/superadmin/audit";

/**
 * GET /api/v1/superadmin/audit — consulta global (SuperAdmin) del rastro de auditoría,
 * paginada y ordenada por `changedAt` descendente. Los filtros vacíos/undefined no se
 * envían (el backend los trata como "sin filtrar").
 */
export function fetchAdminAuditLog(
  query: AdminAuditLogQuery = {},
  signal?: AbortSignal,
): Promise<AdminAuditLogPageResponse> {
  return apiFetch<AdminAuditLogPageResponse>(base, { query: { ...query }, signal });
}
