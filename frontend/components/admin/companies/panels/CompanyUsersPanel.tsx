"use client";

import { useCallback, useEffect, useState } from "react";
import { History, Pencil, UserPlus } from "lucide-react";
import {
  assignRole,
  getUsers,
  updateUser,
  type TenantRole,
  type TenantUser,
} from "@/lib/api/security";
import { EditUserModal } from "@/components/atom/modules/users/EditUserModal";
import { InviteUserModal } from "@/components/atom/modules/users/InviteUserModal";
import { UsersTable, toUserRow } from "@/components/atom/modules/users/UsersTable";
import { UserAuditHistoryDrawer } from "@/components/atom/modules/users/UserAuditHistoryDrawer";
import type { RowAction } from "@/components/atom/RowActions";
import { resolveProfile } from "@/lib/users/profiles";
import { superadminClient } from "@/lib/api/superadmin-client";
import { usePermissions } from "@/hooks/usePermissions";

export interface CompanyUsersPanelProps {
  tenantId: string;
}

/**
 * Pestaña "Usuarios" de la ficha de compañía: el SuperAdmin invita y administra los usuarios
 * del tenant sin salir de /admin/companies/[tenantId]. Comparte tabla, filtros, acciones y
 * modal de alta con el módulo Usuarios y con el hub OT.
 */
export function CompanyUsersPanel({ tenantId }: CompanyUsersPanelProps) {
  const { isSuperAdmin } = usePermissions();
  const [users, setUsers] = useState<TenantUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<TenantUser | null>(null);
  const [auditTarget, setAuditTarget] = useState<TenantUser | null>(null);
  const [editRoles, setEditRoles] = useState<TenantRole[]>([]);
  const [editRolesLoading, setEditRolesLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const all = await getUsers();
      setUsers(all.filter((u) => u.tenantId === tenantId));
    } catch {
      setError("No se pudieron cargar los usuarios de esta compañía.");
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  useEffect(() => {
    if (!editTarget || editTarget.status === "pending") {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setEditRoles([]);
      return;
    }
    if (resolveProfile(editTarget) === "FLIT") {
      setEditRoles([]);
      return;
    }
    let cancelled = false;
    setEditRolesLoading(true);
    void superadminClient
      .listRoles("COMPANY")
      .then((list) => {
        if (cancelled) return;
        setEditRoles(
          list
            // El rol de sistema SuperAdmin es el perfil FLIT: nunca se asigna desde la ficha
            // de una compañía.
            .filter((r) => r.isActive && r.code.toLowerCase() !== "superadmin")
            .map((r) => ({
              id: r.id,
              code: r.code,
              name: r.name,
              description: r.description,
              isSystem: r.isSystem,
              permissionCount: r.permissionCount,
              createdAt: r.createdAt,
            })),
        );
      })
      .catch(() => {
        if (!cancelled) setEditRoles([]);
      })
      .finally(() => {
        if (!cancelled) setEditRolesLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [editTarget]);

  function actionsFor(userId: string): RowAction[] {
    const u = users.find((x) => x.id === userId);
    if (!u || u.status === "pending") return [];
    return [
      {
        icon: History,
        label: `Ver historial de ${u.fullName}`,
        tone: "primary",
        onClick: () => setAuditTarget(u),
      },
      {
        icon: Pencil,
        label: `Editar usuario ${u.fullName}`,
        tone: "primary",
        onClick: () => setEditTarget(u),
      },
    ];
  }

  if (!isSuperAdmin) {
    return (
      <p className="py-10 text-center text-sm opacity-60">
        Solo el Super Admin puede gestionar usuarios desde la ficha de compañía.
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-sm font-bold">Usuarios de la compañía</h3>
          <p className="text-[11px] opacity-60">
            Invita usuarios de perfil Gestor y elige el rol con el que entran.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setInviteOpen(true)}
          className="inline-flex items-center gap-1.5 rounded-xl px-3 py-2 text-xs font-semibold text-white"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <UserPlus className="h-3.5 w-3.5" aria-hidden />
          Invitar usuario
        </button>
      </div>

      <UsersTable
        rows={users.map((u) => toUserRow(u, { tenantType: "COMPANY" }))}
        loading={loading}
        error={error}
        onRetry={load}
        emptyMessage="No hay usuarios en esta compañía. Invita al primero."
        actionsFor={(row) => actionsFor(row.id)}
      />

      {inviteOpen && (
        <InviteUserModal
          onClose={() => setInviteOpen(false)}
          onSuccess={() => {
            setInviteOpen(false);
            void load();
          }}
          roles={[]}
          rolesLoading={false}
          isSuperAdmin
          fixedTarget={{ tenantId, profile: "GESTOR", name: "Esta compañía — perfil Gestor" }}
        />
      )}

      {editTarget && (
        <EditUserModal
          user={{
            id: editTarget.id,
            fullName: editTarget.fullName,
            email: editTarget.email,
            rowVersion: editTarget.rowVersion,
          }}
          onClose={() => setEditTarget(null)}
          onSaved={() => {
            setEditTarget(null);
            void load();
          }}
          onUpdate={updateUser}
          roleSection={{
            currentRoleName: editTarget.role,
            currentRoleId: editTarget.roleId,
            roles: editRoles,
            rolesLoading: editRolesLoading,
            onAssignRole: async (roleId) => {
              await assignRole(editTarget.id, roleId);
              await load();
            },
          }}
        />
      )}

      {auditTarget && (
        <UserAuditHistoryDrawer
          userId={auditTarget.id}
          userLabel={auditTarget.fullName}
          onClose={() => setAuditTarget(null)}
        />
      )}
    </div>
  );
}
