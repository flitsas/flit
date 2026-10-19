"use client";

import { ShieldCheck } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ModulePermissionsSection } from "@/components/shared/permissions/ModulePermissionsSection";

// HU #13652 — «Permisos por módulo» en la ficha de usuario del Panel SA (Super Admin). Envuelve la sección
// compartida; F-D2 la monta en las fichas del Admin de Compañía y del Admin OT con `mode="admin"`.
export interface UserPermissionsModalProps {
  user: { id: string; fullName: string; email: string; tenantId?: string | null };
  /** Roles del usuario (una fila por rol en el listado) para calcular «Heredado del rol». */
  roleIds: string[];
  mode: "superadmin" | "admin";
  targetIsSuperAdmin?: boolean;
  invitationPending?: boolean;
  readOnly?: boolean;
  readOnlyReason?: string;
  onClose: () => void;
}

export function UserPermissionsModal({
  user,
  roleIds,
  mode,
  targetIsSuperAdmin,
  invitationPending,
  readOnly,
  readOnlyReason,
  onClose,
}: UserPermissionsModalProps) {
  return (
    <Modal
      open
      onClose={onClose}
      size="xl"
      icon={ShieldCheck}
      title={`Permisos por módulo — ${user.fullName}`}
      description={user.email}
    >
      <ModulePermissionsSection
        userId={user.id}
        tenantId={user.tenantId ?? undefined}
        mode={mode}
        roleIds={roleIds}
        targetIsSuperAdmin={targetIsSuperAdmin}
        invitationPending={invitationPending}
        readOnly={readOnly}
        readOnlyReason={readOnlyReason}
      />
    </Modal>
  );
}
