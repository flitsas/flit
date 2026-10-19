"use client";

import { ShieldCheck } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ModulePermissionsSection } from "@/components/shared/permissions/ModulePermissionsSection";
import { ADMIN_READ_ONLY_MESSAGE, adminReadOnlyFor } from "@/lib/permissions/modulePermissions";

// HU #13652 — «Permisos por módulo» en la ficha de usuario del Panel SA (Super Admin). Envuelve la sección
// compartida; F-D2 (HU #13655/#13656) la monta en las fichas del Admin de Compañía y del Admin OT con `mode="admin"`.
export interface UserPermissionsModalProps {
  user: { id: string; fullName: string; email: string; tenantId?: string | null };
  /** Roles del usuario (una fila por rol en el listado) para calcular «Heredado del rol». */
  roleIds: string[];
  mode: "superadmin" | "admin";
  targetIsSuperAdmin?: boolean;
  invitationPending?: boolean;
  readOnly?: boolean;
  readOnlyReason?: string;
  /** HU #13656 (modo admin): códigos de rol del usuario destino; si alguno es de administrador, solo lectura. */
  targetRoleCodes?: Array<string | null | undefined>;
  /** HU #13656: la ficha es la del propio usuario en sesión (solo lectura). */
  isSelf?: boolean;
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
  targetRoleCodes,
  isSelf,
  onClose,
}: UserPermissionsModalProps) {
  const adminLocked = adminReadOnlyFor(mode, targetRoleCodes, isSelf);
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
        readOnly={readOnly || adminLocked}
        readOnlyReason={adminLocked ? ADMIN_READ_ONLY_MESSAGE : readOnlyReason}
      />
    </Modal>
  );
}
