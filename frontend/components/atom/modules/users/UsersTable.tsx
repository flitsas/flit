"use client";

import { useMemo, useState, type ReactNode } from "react";
import { Search } from "lucide-react";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { ProfileCell, RoleCell } from "./ProfileRoleCell";
import {
  USER_PROFILE_ORDER,
  profileShortLabel,
  resolveProfile,
  type UserProfileKind,
} from "@/lib/users/profiles";

import { formatFechaHora } from "@/lib/format/date";
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
  /** HU #11552 / ADR-0048: "cancelled" es un cuarto valor — invitación cancelada, visible y
   *  reactivable. Ver `isInvitationRow` en lib/users/invitationRow.ts antes de asumir que
   *  `status !== "pending"` implica "esta fila es un usuario real". */
  status: "active" | "inactive" | "pending" | "cancelled";
  isSuspended: boolean;
  createdAt: string | null;
  /** Clave de React: un usuario con N roles produce N filas con el mismo id. */
  rowKey: string;
}

export type UserStatusFilter = "" | "active" | "pending" | "inactive" | "blocked" | "cancelled";

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
    status: "active" | "inactive" | "pending" | "cancelled";
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
  // HU #11552 / ADR-0048: invitación cancelada, visible y reactivable — tono neutral, no
  // "danger" (no es un error ni una baja definitiva, es un estado terminal-reversible).
  cancelled: { label: "Cancelada", tone: "neutral" },
};

const SUSPENDED_BADGE: { label: string; tone: StatusTone } = { label: "Bloqueado", tone: "danger" };

const STATUS_OPTIONS: { value: Exclude<UserStatusFilter, "">; label: string }[] = [
  { value: "active", label: "Activo" },
  { value: "pending", label: "Pendiente" },
  { value: "cancelled", label: "Cancelada" },
  { value: "inactive", label: "Inactivo" },
  { value: "blocked", label: "Bloqueado" },
];

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return formatFechaHora(parsed);
}

const CELDA = "border-y px-4 py-3";
const CELDA_STYLE = { borderColor: "#DFE5ED" };

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

  // Perfil y Rol viven en columnas separadas (HU #11551): antes se apilaban en una sola celda
  // y el chip de Perfil (siempre "Gestor" para cualquier usuario de compañía) eclipsaba el rol
  // real, dando la impresión de que todos los usuarios tenían el mismo rol.
  //
  // Bug #13055 — tabla homologada con el modelo de trámites: `<table>` semántica (antes una grilla de
  // div). La columna Acciones se ajusta al contenido (`w-px whitespace-nowrap`), así que los botones
  // ya no se desbordan hacia la celda Fecha como pasaba con el ancho fijo por acción.
  const pg = usePaginacion();
  const { setPage } = pg;

  const columnas = showTenantColumn
    ? ["Usuario", "Empresa", "Perfil", "Rol", "Estado", "Fecha", "Acciones"]
    : ["Usuario", "Perfil", "Rol", "Estado", "Fecha", "Acciones"];

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

  // Los filtros vuelven a la página 1 (cada `onChange` llama a `setPage(1)`) y la paginación se aplica
  // DESPUÉS de filtrar.
  const visibles = pg.paginar(filtered);

  const hasActiveFilters = Boolean(search || profileFilter || roleFilter || statusFilter);

  function clearFilters() {
    setSearch("");
    setProfileFilter("");
    setRoleFilter("");
    setStatusFilter("");
    setPage(1);
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex min-w-[200px] flex-1 items-center gap-2 rounded-xl border bg-white px-3 py-1.5 dark:bg-[#0B0F14]">
          <Search className="h-4 w-4 opacity-60" aria-hidden />
          <input
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            placeholder="Buscar por nombre o correo…"
            aria-label="Buscar usuarios"
            className="flex-1 bg-transparent text-xs outline-none"
          />
        </div>

        <label className="flex items-center gap-1.5 text-[11px]">
          <span className="font-semibold opacity-60">Perfil</span>
          <select
            value={profileFilter}
            onChange={(e) => { setProfileFilter(e.target.value as "" | UserProfileKind); setPage(1); }}
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
            onChange={(e) => { setRoleFilter(e.target.value); setPage(1); }}
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
            onChange={(e) => { setStatusFilter(e.target.value as UserStatusFilter); setPage(1); }}
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

      {loading && <CarLoaderModal label="Cargando usuarios…" />}

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

      {!loading && !error && filtered.length > 0 && (
        <div className="overflow-x-auto">
          <table
            aria-label="Usuarios"
            className="text-xs"
            style={{
              width: "100%",
              borderCollapse: "separate",
              borderSpacing: "0 8px",
              minWidth: showTenantColumn ? 980 : 860,
            }}
          >
            <thead>
              <tr>
                {columnas.map((col, i) => (
                  <th
                    key={col}
                    scope="col"
                    className={`${TABLA_HEADER_CELL_CLS} ${i === 0 ? "rounded-l-xl" : ""} ${
                      i === columnas.length - 1 ? "rounded-r-xl text-right" : ""
                    }`}
                    style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                  >
                    {col}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {visibles.map((row) => {
                const badge = row.isSuspended ? SUSPENDED_BADGE : STATUS_BADGE[row.status];
                return (
                  <tr key={row.rowKey} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                    <td className={`${CELDA} rounded-l-xl border-l`} style={CELDA_STYLE}>
                      <p className="truncate font-semibold">{row.fullName}</p>
                      <p className="truncate text-[10px] opacity-60">{row.email}</p>
                    </td>
                    {showTenantColumn && (
                      <td className={CELDA} style={CELDA_STYLE}>
                        <span className="block truncate opacity-70" title={row.tenantName ?? undefined}>
                          {row.tenantName ?? "—"}
                        </span>
                      </td>
                    )}
                    <td className={CELDA} style={CELDA_STYLE}>
                      <ProfileCell
                        roleCode={row.roleCode}
                        profile={row.profile}
                        tenantType={row.tenantType}
                      />
                    </td>
                    <td className={CELDA} style={CELDA_STYLE}>
                      <RoleCell roleCode={row.roleCode} roleName={row.role} />
                    </td>
                    <td className={CELDA} style={CELDA_STYLE}>
                      <StatusBadge label={badge.label} tone={badge.tone} />
                    </td>
                    <td className={CELDA} style={CELDA_STYLE}>
                      <span className="opacity-70">{formatDateTime(row.createdAt)}</span>
                    </td>
                    <td
                      className={`${CELDA} w-px whitespace-nowrap rounded-r-xl border-r`}
                      style={CELDA_STYLE}
                    >
                      <div className="flex items-center justify-end gap-1">
                        {extraActionsFor?.(row)}
                        <RowActions actions={actionsFor(row)} />
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {!loading && !error && filtered.length > 0 && (
        <Pagination
          page={pg.page}
          pageSize={pg.pageSize}
          totalCount={filtered.length}
          onPageChange={pg.setPage}
          onPageSizeChange={pg.setPageSize}
          noun="usuarios"
          ariaLabel="Paginación de usuarios"
        />
      )}
    </div>
  );
}
