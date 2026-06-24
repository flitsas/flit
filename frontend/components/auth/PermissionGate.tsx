"use client";

import { type ReactNode } from "react";
import { usePermissions } from "@/hooks/usePermissions";

interface PermissionGateProps {
  /** Permission slug required to render children. */
  permission: string;
  children: ReactNode;
  /** Rendered when the user lacks the permission. Defaults to null. */
  fallback?: ReactNode;
}

/**
 * Renders children only when the current user has the required permission slug
 * OR is SuperAdmin (which bypasses all permission checks).
 */
export function PermissionGate({ permission, children, fallback = null }: PermissionGateProps) {
  const { permissions, isSuperAdmin } = usePermissions();

  if (isSuperAdmin || permissions.includes(permission)) {
    return <>{children}</>;
  }

  return <>{fallback}</>;
}
