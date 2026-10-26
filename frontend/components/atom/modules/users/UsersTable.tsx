"use client";

import { useMemo, type ReactNode } from "react";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { TablaFiltrosBar, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import type { RowAction } from "@/components/atom/RowActions";
import { RowActionsMenu, destructivasAlFinal } from "@/components/atom/RowActionsMenu";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { ProfileCell, RoleCell } from "./ProfileRoleCell";
import { USER_PROFILE_ORDER, profileShortLabel, resolveProfile } from "@/lib/users/profiles";

import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
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
  /**
   * Acciones de la fila. Se muestran en UN botón «Acciones» con menú (RowActionsMenu, como en trámites y mandatos);
   * las de tono `danger` van al final. Cada opción conserva su nombre accesible completo («Editar usuario Ana»).
   */
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
  // Perfil y Rol viven en columnas separadas (HU #11551): antes se apilaban en una sola celda
  // y el chip de Perfil (siempre "Gestor" para cualquier usuario de compañía) eclipsaba el rol
  // real, dando la impresión de que todos los usuarios tenían el mismo rol.
  //
  // Bug #13055 — tabla homologada con el modelo de trámites: `<table>` semántica (antes una grilla de
  // div). La columna Acciones se ajusta al contenido (`w-px whitespace-nowrap`), así que los botones
  // ya no se desbordan hacia la celda Fecha como pasaba con el ancho fijo por acción.
  const pg = usePaginacion("users.UsersTable");
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

  // HU #14006: un filtro por cada columna con datos (estándar HU #13997). Siguen a las columnas
  // visibles: «Empresa» solo existe en los listados cross-tenant. «Acciones» nunca se filtra.
  const columnasFiltro = useMemo<ColumnaFiltro[]>(
    () => [
      { id: "nombre", label: "Nombre", tipo: "texto" },
      { id: "correo", label: "Correo", tipo: "texto" },
      ...(showTenantColumn
        ? [{ id: "empresa", label: "Empresa", tipo: "texto" } as ColumnaFiltro]
        : []),
      {
        id: "perfil",
        label: "Perfil",
        tipo: "seleccion",
        opciones: USER_PROFILE_ORDER.map((p) => ({ value: p, label: profileShortLabel(p) })),
      },
      {
        id: "rol",
        label: "Rol",
        tipo: "seleccion",
        opciones: roleOptions.map((r) => ({ value: r, label: r })),
      },
      { id: "estado", label: "Estado", tipo: "seleccion", opciones: STATUS_OPTIONS },
      { id: "fecha", label: "Fecha", tipo: "fecha" },
    ],
    [showTenantColumn, roleOptions],
  );

  // Cada cambio de filtro vuelve a la página 1; la paginación se aplica DESPUÉS de filtrar.
  const filtros = useTablaFiltros({
    columnas: columnasFiltro,
    periodoColumnaId: "fecha",
    onCambio: () => setPage(1),
  });

  const filtered = useMemo(
    () =>
      filtrarFilas(rows, filtros.condicionesTotales, filtros.busqueda, {
        columnas: {
          nombre: (r) => r.fullName,
          correo: (r) => r.email,
          empresa: (r) => r.tenantName,
          perfil: (r) => resolveProfile(r),
          rol: (r) => r.role,
          estado: (r) => (r.isSuspended ? "blocked" : r.status),
          fecha: (r) => r.createdAt,
        },
        busqueda: (r) => [r.fullName, r.email, r.tenantName],
      }),
    [rows, filtros.condicionesTotales, filtros.busqueda],
  );

  const visibles = pg.paginar(filtered);

  return (
    <div className="flex flex-col gap-3">
      <TablaFiltrosBar
        filtros={filtros}
        buscador={{ placeholder: "Buscar por nombre o correo…", ariaLabel: "Buscar usuarios" }}
        acciones={toolbar}
      />

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
              style={{
                background:
                  "linear-gradient(135deg,var(--color-flit-brand),var(--color-flit-tech))",
              }}
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
        <TablaFiltrosVacio
          onLimpiar={filtros.limpiar}
          mensaje="Ningún usuario coincide con la búsqueda o los filtros"
        />
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
                  <tr
                    key={row.rowKey}
                    className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}
                  >
                    <td className={`${CELDA} rounded-l-xl border-l`} style={CELDA_STYLE}>
                      <p className="truncate font-semibold">{row.fullName}</p>
                      <p className="truncate text-xs opacity-60">{row.email}</p>
                    </td>
                    {showTenantColumn && (
                      <td className={CELDA} style={CELDA_STYLE}>
                        <span
                          className="block truncate opacity-70"
                          title={row.tenantName ?? undefined}
                        >
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
                        <RowActionsMenu
                          ariaLabel={`Acciones de ${row.fullName}`}
                          subject={row.fullName}
                          actions={destructivasAlFinal(actionsFor(row))}
                        />
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
