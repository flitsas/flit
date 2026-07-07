"use client";

import { useMemo } from "react";
import { getToken } from "@/lib/api/client";
import {
  decodeJwtPayload,
  isSuperAdmin as checkSuperAdmin,
  isAdminCompany as checkAdminCompany,
  isOtAdmin as checkOtAdmin,
} from "@/lib/auth/jwt";

export interface PermissionsState {
  permissions: string[];
  isSuperAdmin: boolean;
  isAdminCompany: boolean;
  isOtAdmin: boolean;
  tenantId: string | null;
  userId: string | null;
  roleId: string | null;
  roleCode: string | null;
}

export function usePermissions(): PermissionsState {
  return useMemo(() => {
    const token = getToken();
    const payload = decodeJwtPayload(token);
    // HU #10506 (multi-rol): con 2+ roles activos, el backend serializa role_code/role/
    // role_id como ARRAY (colapso de claims .NET), no como string — la fuente confiable
    // es siempre el claim `roles` (array explícito de {id, code}). roleId/roleCode aquí
    // quedan como "el primer rol" por compatibilidad con este hook (tipo singular); para
    // la lista completa de roles activos, usar `payload.roles` directamente.
    const firstRole = Array.isArray(payload?.roles) ? payload.roles[0] : undefined;
    const roleCode =
      typeof firstRole?.code === "string"
        ? firstRole.code
        : typeof payload?.role_code === "string"
          ? payload.role_code
          : typeof payload?.role === "string"
            ? payload.role
            : null;
    const roleId =
      typeof firstRole?.id === "string"
        ? firstRole.id
        : typeof payload?.role_id === "string"
          ? payload.role_id
          : null;
    return {
      permissions: Array.isArray(payload?.permissions) ? (payload.permissions as string[]) : [],
      isSuperAdmin: checkSuperAdmin(payload),
      isAdminCompany: checkAdminCompany(payload),
      isOtAdmin: checkOtAdmin(payload),
      tenantId: (payload?.tenant_id as string) ?? null,
      userId: (payload?.sub as string) ?? null,
      roleId,
      roleCode,
    };
  }, []);
}
