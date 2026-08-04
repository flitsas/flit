"use client";

import { useMemo, useState, type ReactNode } from "react";
import { Search } from "lucide-react";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { ProfileRoleCell } from "./ProfileRoleCell";
import {
  USER_PROFILE_ORDER,
  profileShortLabel,
  resolveProfile,
  type UserProfileKind,
} from "@/lib/users/profiles";

/**
 * Fila normalizada de usuario. Los tres listados (módulo Usuarios, ficha de compañía y hub OT)
 * hablan APIs distintas (`TenantUser` vs `OtUserItem`); cada uno mapea a este tipo y comparte
 * de ahí en adelante la misma tabla, los mismos filtros y la misma columna de acciones.
 */
export interface UserRow {
  id: string;
  fullName: string;
  email: string;
  role: string | null;
  roleCode: string | null;
  profile?: string | null;
  tenantType?: string | null;
  tenantName?: string | null;
  status: "active" | "inactive" | "pending";
  isSuspended: boolean;
  createdAt: string | null;
  /** Clave de React: un usuario con N roles produce N filas con el mismo id. */
  rowKey: string;
}

export type UserStatusFilter = "" | "active" | "pending" | "inactive" | "blocked";

/**
 * Adapta cualquiera de las formas que devuelven las APIs (`TenantUser`, `OtUserItem`) a
 * `UserRow`. La clave compone id + roleId porque el listado hace JOIN por asignación de rol:
 * un usuario con N roles produce N filas con el mismo id.
 */
export function toUserRow(
  user: {
    id: string;
    fullName: string;
    email: string;
    role: string | null;
    roleCode: string | null;
    roleId?: string | null;
    status: "active" | "inactive" | "pending";
    isSuspended: boolean;
    createdAt: string | null;
    profile?: string | null;
    tenantType?: string | null;
    tenantName?: string | null;
  },
  overrides?: Partial<Pick<UserRow, "profile" | "tenantType" | "tenantName">>,
): UserRow {
  return {
    id: user.id,
    fullName: user.fullName,
    email: user.email,
    role: user.role,
    roleCode: user.roleCode,
    profile: overrides?.profile ?? user.profile ?? null,
    tenantType: overrides?.tenantType ?? user.tenantType ?? null,
    tenantName: overrides?.tenantName ?? user.tenantName ?? null,
    status: user.status,
    isSuspended: user.isSuspended,
    createdAt: user.createdAt,
    rowKey: `${user.id}-${user.roleId ?? "sin-rol"}`,
  };
}

const STATUS_BADGE: Record<UserRow["status"], { label: string; tone: StatusTone }> = {
  active: { label: "Activo", tone: "success" },
  inactive: { label: "Inactivo", tone: "danger" },
  pending: { label: "Pendiente", tone: "warning" },
};

const SUSPENDED_BADGE: { label: string; tone: StatusTone } = { label: "Bloqueado", tone: "danger" };

const STATUS_OPTIONS: { value: Exclude<UserStatusFilter, "">; label: string }[] = [
  { value: "active", label: "Activo" },
  { value: "pending", label: "Pendiente" },
  { value: "inactive", label: "Inactivo" },
  { value: "blocked", label: "Bloqueado" },
];

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short" }).format(parsed);
}

export interface UsersTableProps {
  rows: UserRow[];
  loading?: boolean;
  error?: string | null;
  onRetry?: () => void;
  /** Columna "Empresa" — solo tiene sentido en listados cross-tenant. */
  showTenantColumn?: boolean;
  /** Acciones de la fila. Se renderizan con RowActions, que ya trae el área de clic correcta. */
  actionsFor: (row: UserRow) => RowAction[];
  /**
   * Acciones con UI propia que no encajan en un RowAction simple (p. ej. el botón de reenviar
   * invitación, que lleva cooldown y mensaje inline). Se renderizan en la misma celda.
   */
  extraActionsFor?: (row: UserRow) => ReactNode;
  /** Mensaje cuando no hay NINGÚN usuario (distinto de "los filtros no encontraron nada"). */
  emptyMessage?: string;
  /** Contenido extra a la derecha de la barra de filtros (p. ej. el botón de invitar). */
  toolbar?: ReactNode;
}

/**
 * Tabla de usuarios compartida por los tres módulos. Antes cada uno tenía su propia
 * implementación —una con `<table>`, otras con grid, cada una con sus propios chips de estado
 * y solo una con filtros—, que es justo lo que hacía que "activar o crear usuarios de OT no
 * se pareciera a las otras".
 */
export function UsersTable({
  rows,
  loading = false,
  error = null,
  onRetry,
  showTenantColumn = false,
  actionsFor,
  extraActionsFor,
  emptyMessage = "No hay usuarios para mostrar.",
  toolbar,
}: UsersTableProps) {
  const [search, setSearch] = useState("");
  const [profileFilter, setProfileFilter] = useState<"" | UserProfileKind>("");
  const [roleFilter, setRoleFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState<UserStatusFilter>("");

  const gridTemplate = showTenantColumn
    ? "2.4fr 1.4fr 1.8fr 1.1fr 1.2fr 108px"
    : "2.6fr 1.8fr 1.2fr 1.4fr 108px";
  const minWidth = showTenantColumn ? "min-w-[880px]" : "min-w-[760px]";

  const roleOptions = useMemo(() => {
    const names = new Set<string>();
    for (const row of rows) {
      const name = row.role?.trim();
      if (name) names.add(name);
    }
    return Array.from(names).sort((a, b) => a.localeCompare(b, "es"));
  }, [rows]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return rows.filter((row) => {
      if (q) {
        const haystack = `${row.fullName} ${row.email} ${row.tenantName ?? ""}`.toLowerCase();
        if (!haystack.includes(q)) return false;
      }
      if (profileFilter && resolveProfile(row) !== profileFilter) return false;
      if (roleFilter && (row.role ?? "") !== roleFilter) return false;
      if (statusFilter === "blocked") {
        if (!row.isSuspended) return false;
      } else if (statusFilter) {
        if (row.isSuspended || row.status !== statusFilter) return false;
      }
      return true;
    });
  }, [rows, search, profileFilter, roleFilter, statusFilter]);

  const hasActiveFilters = Boolean(search || profileFilter || roleFilter || statusFilter);

  function clearFilters() {
    setSearch("");
    setProfileFilter("");
    setRoleFilter("");
    setStatusFilter("");
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex min-w-[200px] flex-1 items-center gap-2 rounded-xl border bg-white px-3 py-1.5 dark:bg-[#0B0F14]">
          <Search className="h-4 w-4 opacity-60" aria-hidden />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Buscar por nombre o correo…"
            aria-label="Buscar usuarios"
            className="flex-1 bg-transparent text-xs outline-none"
          />
        </div>

        <label className="flex items-center gap-1.5 text-[11px]">
          <span className="font-semibold opacity-60">Perfil</span>
          <select
            value={profileFilter}
            onChange={(e) => setProfileFilter(e.target.value as "" | UserProfileKind)}
            aria-label="Filtrar por perfil"
            className="rounded-lg border bg-transparent px-2 py-1.5 text-[11px] outline-none"
          >
            <option value="">Todos</option>
            {USER_PROFILE_ORDER.map((p) => (
              <option key={p} value={p}>
                {profileShortLabel(p)}
              </option>
            ))}
          </select>
        </label>

        <label className="flex items-center gap-1.5 text-[11px]">
          <span className="font-semibold opacity-60">Rol</span>
          <select
            value={roleFilter}
            onChange={(e) => setRoleFilter(e.target.value)}
            aria-label="Filtrar por rol"
            className="max-w-[170px] rounded-lg border bg-transparent px-2 py-1.5 text-[11px] outline-none"
          >
            <option value="">Todos</option>
            {roleOptions.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
        </label>

        <label className="flex items-center gap-1.5 text-[11px]">
          <span className="font-semibold opacity-60">Estado</span>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as UserStatusFilter)}
            aria-label="Filtrar por estado"
            className="rounded-lg border bg-transparent px-2 py-1.5 text-[11px] outline-none"
          >
            <option value="">Todos</option>
            {STATUS_OPTIONS.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
        </label>

        {hasActiveFilters && (
          <button
            type="button"
            onClick={clearFilters}
            className="text-[11px] font-semibold"
            style={{ color: "#557EFF" }}
          >
            Limpiar filtros
          </button>
        )}

        {toolbar}
      </div>

      <div className="flex flex-col overflow-x-auto">
        <div
          className={`grid ${minWidth} shrink-0 rounded-t-xl px-4 py-2.5 text-[10px] font-semibold uppercase`}
          style={{ gridTemplateColumns: gridTemplate, background: "#DFE5ED", color: "#162744" }}
        >
          <div>Usuario</div>
          {showTenantColumn && <div>Empresa</div>}
          <div>Perfil / Rol</div>
          <div>Estado</div>
          <div>Fecha</div>
          <div className="text-right">Acciones</div>
        </div>

        {/* Los data-testid replican los de UiStateBoundary: esta tabla sustituyó a ese wrapper
            en el hub OT y los cuatro estados de UI deben seguir siendo observables igual. */}
        <div className="space-y-2 pt-2">
          {loading && (
            <div role="status" data-testid="ui-loading" className="py-12 text-center text-sm opacity-60">
              Cargando usuarios…
            </div>
          )}

          {!loading && error && (
            <div
              role="alert"
              data-testid="ui-error"
              className="space-y-3 py-12 text-center text-sm"
              style={{ color: "#FF4E00" }}
            >
              <p>{error}</p>
              {onRetry && (
                <button
                  type="button"
                  onClick={onRetry}
                  className="rounded-xl px-3 py-1.5 text-xs font-semibold text-white"
                  style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
                >
                  Reintentar
                </button>
              )}
            </div>
          )}

          {!loading && !error && rows.length === 0 && (
            <div data-testid="ui-empty" className="py-12 text-center text-sm opacity-60">
              {emptyMessage}
            </div>
          )}

          {!loading && !error && rows.length > 0 && filtered.length === 0 && (
            <div className="py-12 text-center text-sm opacity-60">
              Ningún usuario coincide con la búsqueda o los filtros.
            </div>
          )}

          {!loading &&
            !error &&
            filtered.map((row) => {
              const badge = row.isSuspended ? SUSPENDED_BADGE : STATUS_BADGE[row.status];
              return (
                <div
                  key={row.rowKey}
                  className={`grid ${minWidth} items-center rounded-xl border bg-white px-4 py-3 text-xs dark:bg-[#0B0F14]`}
                  style={{ gridTemplateColumns: gridTemplate }}
                >
                  <div className="min-w-0">
                    <p className="truncate font-semibold">{row.fullName}</p>
                    <p className="truncate text-[10px] opacity-60">{row.email}</p>
                  </div>
                  {showTenantColumn && (
                    <div className="truncate opacity-70" title={row.tenantName ?? undefined}>
                      {row.tenantName ?? "—"}
                    </div>
                  )}
                  <ProfileRoleCell
                    roleCode={row.roleCode}
                    roleName={row.role}
                    profile={row.profile}
                    tenantType={row.tenantType}
                  />
                  <div>
                    <StatusBadge label={badge.label} tone={badge.tone} />
                  </div>
                  <div className="opacity-70">{formatDateTime(row.createdAt)}</div>
                  <div className="flex items-center justify-end gap-1">
                    {extraActionsFor?.(row)}
                    <RowActions actions={actionsFor(row)} />
                  </div>
                </div>
              );
            })}
        </div>

        {!loading && !error && rows.length > 0 && (
          <p className="shrink-0 pt-2 text-right text-[10px] opacity-60">
            Mostrando {filtered.length} de {rows.length} usuario{rows.length !== 1 ? "s" : ""}
          </p>
        )}
      </div>
    </div>
  );
}
