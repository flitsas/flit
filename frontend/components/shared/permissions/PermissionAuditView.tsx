"use client";

import { useEffect, useMemo, useState } from "react";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { SearchableSelect, type SearchableSelectOption } from "@/components/atom/SearchableSelect";
import { InlineAlert } from "@/components/atom/InlineAlert";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import type { TenantUser } from "@/lib/api/security";
import {
  listPermissionAudit,
  rejectionReasonMessage,
  type PermissionAuditPage,
  type PermissionAuditRecord,
} from "@/lib/api/permission-overrides";
import { formatFechaHora } from "@/lib/format/date";
import { isAdminRole, isSuperAdminRole } from "@/lib/users/profiles";

// HU #13654 (Feature #13103 F-D1, Épica #12753) — consulta de la auditoría de permisos individuales.
// Vista COMPARTIDA: el Panel SA la monta con `mode="superadmin"` (filtros de usuario, compañía y administrador);
// F-D2 la reutiliza con `mode="admin"` (sin filtro de compañía: la API acota al tenant del administrador).
// Tabla con el patrón de trámites: table-styles, sin tarjeta blanca, paginación numerada + «Filas por página»
// (paginada en el servidor) y loader del carrito. Es de solo lectura, por eso no lleva columna de acciones.

export interface PermissionAuditViewProps {
  mode: "superadmin" | "admin";
  /** Usuarios del alcance del llamador: alimentan los filtros de usuario, compañía y administrador. */
  users: TenantUser[];
  /** Usuario preseleccionado (p. ej. al abrir la vista desde la ficha de un usuario). */
  initialUserId?: string;
}

/** Consulta de los permisos de un usuario: la API solo la audita cuando la rechaza. */
const READ_ACTION = "READ";

const ACTION_LABEL: Record<string, string> = {
  [READ_ACTION]: "Consultó los permisos",
  GRANT: "Otorgó",
  DENY: "Denegó",
  REVOKE: "Volvió al rol",
  MODULE_SNAPSHOT: "Cambió el módulo completo",
  ROLE_CHANGE_PURGE: "Limpieza por cambio de rol",
  PII_REVEAL_CONFIRMED: "Confirmó ver datos personales",
};

const VALUE_LABEL: Record<string, string> = { allow: "Habilitado", deny: "Denegado" };

/** Las acciones sin rótulo propio se muestran genéricas: nunca el código interno. */
export function actionLabel(action: string): string {
  return ACTION_LABEL[action] ?? "Cambió un permiso";
}

function valueLabel(value: string | null): string {
  if (!value) return "Lo que dicta el rol";
  return VALUE_LABEL[value] ?? value;
}

function person(name: string | null, email: string | null): { main: string; sub: string | null } {
  const n = name?.trim();
  const e = email?.trim();
  if (n) return { main: n, sub: e || null };
  return { main: e || "—", sub: null };
}

/** Lo que se cambió, con el nombre del catálogo; el código solo si el permiso ya no existe. */
function permissionText(r: PermissionAuditRecord): string {
  if (r.action === READ_ACTION) return "Todos los permisos del usuario";
  if (r.permissionSlug) return r.permissionName ?? r.permissionSlug;
  if (r.moduleCode) return `Módulo completo: ${r.moduleName ?? r.moduleCode}`;
  return "—";
}

function outcomeText(r: PermissionAuditRecord): string {
  if (r.outcome === "OK") return "Aplicado";
  const why = rejectionReasonMessage(r.reasonCode);
  return why ? `Rechazado: ${why}` : "Rechazado";
}

function Cell({ children, first, last }: { children: React.ReactNode; first?: boolean; last?: boolean }) {
  return (
    <td
      className={`border-y px-4 py-3 align-top ${first ? "rounded-l-xl border-l" : ""} ${last ? "rounded-r-xl border-r" : ""}`}
      style={{ borderColor: "#DFE5ED" }}
    >
      {children}
    </td>
  );
}

type FetchResult = { key: string; data?: PermissionAuditPage; failed?: boolean };

export function PermissionAuditView({ mode, users, initialUserId }: PermissionAuditViewProps) {
  const pg = usePaginacion();
  const [userId, setUserId] = useState(initialUserId ?? "");
  const [tenantId, setTenantId] = useState("");
  const [actorId, setActorId] = useState("");
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<FetchResult | null>(null);
  // Administradores vistos en la bitácora: el Super Admin no sale en el listado de usuarios, pero sí actúa.
  const [seenActors, setSeenActors] = useState<Record<string, string>>({});

  const showTenant = mode === "superadmin";
  const { page, pageSize } = pg;
  const key = JSON.stringify([userId, showTenant ? tenantId : "", actorId, page, pageSize, attempt]);

  useEffect(() => {
    let cancelled = false;
    listPermissionAudit({
      userId: userId || undefined,
      tenantId: showTenant ? tenantId || undefined : undefined,
      actorId: actorId || undefined,
      page,
      pageSize,
    })
      .then((data) => {
        if (cancelled) return;
        setResult({ key, data });
        setSeenActors((prev) => {
          const next = { ...prev };
          for (const r of data.items) next[r.actorUserId] = r.actorName?.trim() || r.actorEmail?.trim() || r.actorUserId;
          return next;
        });
      })
      .catch(() => {
        if (!cancelled) setResult({ key, failed: true });
      });
    return () => {
      cancelled = true;
    };
  }, [key, userId, tenantId, actorId, page, pageSize, showTenant]);

  const loading = result?.key !== key;
  const data = !loading ? result?.data : undefined;
  const failed = !loading && result?.failed === true;

  const userOptions = useMemo<SearchableSelectOption[]>(() => {
    const seen = new Map<string, SearchableSelectOption>();
    for (const u of users) {
      if (u.status === "pending" || u.status === "cancelled" || seen.has(u.id)) continue;
      seen.set(u.id, { value: u.id, label: u.fullName, hint: u.email });
    }
    return [...seen.values()];
  }, [users]);

  const tenantOptions = useMemo<SearchableSelectOption[]>(() => {
    const seen = new Map<string, SearchableSelectOption>();
    for (const u of users) {
      if (u.tenantId && !seen.has(u.tenantId)) seen.set(u.tenantId, { value: u.tenantId, label: u.tenantName ?? u.tenantId });
    }
    return [...seen.values()];
  }, [users]);

  const actorOptions = useMemo<SearchableSelectOption[]>(() => {
    const seen = new Map<string, SearchableSelectOption>();
    for (const u of users) {
      if (u.status === "active" && (isAdminRole(u.roleCode) || isSuperAdminRole(u.roleCode)) && !seen.has(u.id)) {
        seen.set(u.id, { value: u.id, label: u.fullName, hint: u.email });
      }
    }
    for (const [id, label] of Object.entries(seenActors)) if (!seen.has(id)) seen.set(id, { value: id, label });
    return [...seen.values()];
  }, [users, seenActors]);

  const hasFilters = Boolean(userId || actorId || (showTenant && tenantId));

  function changeFilter(set: (v: string) => void) {
    return (v: string) => {
      set(v);
      pg.setPage(1);
    };
  }

  const columns = ["Fecha", "Administrador", "Usuario", ...(showTenant ? ["Compañía"] : []), "Permiso", "Acción", "Cambio"];

  return (
    <section aria-label="Auditoría de permisos individuales" className="flex flex-col gap-3">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <SearchableSelect
          label="Usuario"
          options={userOptions}
          value={userId}
          onChange={changeFilter(setUserId)}
          defaultLabel="Todos los usuarios"
          placeholder="Buscar usuario…"
        />
        {showTenant && (
          <SearchableSelect
            label="Compañía"
            options={tenantOptions}
            value={tenantId}
            onChange={changeFilter(setTenantId)}
            defaultLabel="Todas las compañías"
            placeholder="Buscar compañía…"
          />
        )}
        <SearchableSelect
          label="Administrador"
          options={actorOptions}
          value={actorId}
          onChange={changeFilter(setActorId)}
          defaultLabel="Todos los administradores"
          placeholder="Buscar administrador…"
        />
      </div>

      {loading && <CarLoaderModal label="Cargando auditoría de permisos…" />}

      {failed && (
        <InlineAlert
          tone="error"
          action={
            <button
              type="button"
              onClick={() => setAttempt((n) => n + 1)}
              className="rounded-xl px-3 py-1.5 text-xs font-semibold text-white"
              style={{ background: "var(--color-flit-brand)" }}
            >
              Reintentar
            </button>
          }
        >
          No se pudo cargar la auditoría de permisos.
        </InlineAlert>
      )}

      {data && data.items.length === 0 && (
        <p className="py-12 text-center text-sm opacity-60" data-testid="ui-empty">
          {hasFilters
            ? "No hay registros de auditoría que coincidan con los filtros."
            : "Aún no hay registros de auditoría de permisos individuales."}
        </p>
      )}

      {data && data.items.length > 0 && (
        <>
          <div className="overflow-x-auto">
            <table
              aria-label="Auditoría de permisos individuales"
              style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px", minWidth: 760 }}
              className="text-xs"
            >
              <thead>
                <tr>
                  {columns.map((col, i, arr) => (
                    <th
                      key={col}
                      scope="col"
                      className={`${TABLA_HEADER_CELL_CLS} ${i === 0 ? "rounded-l-xl" : ""} ${i === arr.length - 1 ? "rounded-r-xl" : ""}`}
                      style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                    >
                      {col}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.items.map((r) => {
                  const actor = person(r.actorName, r.actorEmail);
                  const target = person(r.targetName, r.targetEmail);
                  return (
                    <tr key={r.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                      <Cell first>
                        <span className="whitespace-nowrap">{formatFechaHora(r.createdAt)}</span>
                      </Cell>
                      <Cell>
                        <p className="font-semibold">{actor.main}</p>
                        {actor.sub && <p className="text-[10px] opacity-60">{actor.sub}</p>}
                      </Cell>
                      <Cell>
                        <p className="font-semibold">{target.main}</p>
                        {target.sub && <p className="text-[10px] opacity-60">{target.sub}</p>}
                      </Cell>
                      {showTenant && (
                        <Cell>
                          <span className="opacity-70">{r.tenantName ?? "—"}</span>
                        </Cell>
                      )}
                      <Cell>
                        <p className="font-medium">{permissionText(r)}</p>
                        {r.permissionSlug && (r.moduleName || r.moduleCode) && (
                          <p className="text-[10px] opacity-60">{r.moduleName ?? r.moduleCode}</p>
                        )}
                      </Cell>
                      <Cell>
                        <p className="font-medium">{actionLabel(r.action)}</p>
                        <p
                          className="text-[10px]"
                          style={{ color: r.outcome === "OK" ? "var(--permiso-habilitado)" : "var(--badge-danger-fg)" }}
                        >
                          {outcomeText(r)}
                        </p>
                      </Cell>
                      <Cell last>
                        {r.action === READ_ACTION ? (
                          <span className="opacity-50">Sin cambios</span>
                        ) : r.valueBefore || r.valueAfter ? (
                          <span className="opacity-80">
                            {valueLabel(r.valueBefore)} → {valueLabel(r.valueAfter)}
                          </span>
                        ) : (
                          <span className="opacity-50">—</span>
                        )}
                      </Cell>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          <Pagination
            page={pg.page}
            pageSize={pg.pageSize}
            totalCount={data.total}
            onPageChange={pg.setPage}
            onPageSizeChange={pg.setPageSize}
            noun="registros de auditoría"
            ariaLabel="Paginación de la auditoría de permisos"
          />
        </>
      )}
    </section>
  );
}
