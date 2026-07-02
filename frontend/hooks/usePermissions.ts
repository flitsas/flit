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
    return {
      permissions: Array.isArray(payload?.permissions) ? (payload.permissions as string[]) : [],
      isSuperAdmin: checkSuperAdmin(payload),
      isAdminCompany: checkAdminCompany(payload),
      isOtAdmin: checkOtAdmin(payload),
      tenantId: (payload?.tenant_id as string) ?? null,
      userId: (payload?.sub as string) ?? null,
      roleId: (payload?.role_id as string) ?? null,
      roleCode: (payload?.role_code as string) ?? (payload?.role as string) ?? null,
    };
  }, []);
}
