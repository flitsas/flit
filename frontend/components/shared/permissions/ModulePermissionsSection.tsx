"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { CircleDot, Lock, RotateCcw, ShieldCheck, UserCog } from "lucide-react";
import { getAccessibleModules, getTenantRole, type AccessibleModule } from "@/lib/api/security";
import { superadminClient } from "@/lib/api/superadmin-client";
import {
  INVITATION_PENDING_MESSAGE,
  TARGET_SUPERADMIN_MESSAGE,
  listGrantablePermissions,
  listUserOverrides,
  overrideErrorCode,
  overrideErrorMessage,
  removeUserOverride,
  setModuleOverrides,
  setUserOverride,
  type GrantablePermissionItem,
  type OverrideEffect,
  type UserPermissionOverride,
} from "@/lib/api/permission-overrides";
import { buildModuleViews, outOfCapOverrideIds, type ActionView, type ModuleView, type PermissionOrigin } from "@/lib/permissions/modulePermissions";
import { formatFechaHora } from "@/lib/format/date";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { UiStateBoundary } from "@flit/ui/UiStateBoundary";

// HU #13652 (Feature #13103 F-D1, Épica #12753) — sección COMPARTIDA «Permisos por módulo».
// La usa el Panel SA (`mode="superadmin"`) y la reutilizará F-D2 en las fichas del Admin de Compañía y del
// Admin OT (`mode="admin"`: tope = lo que devuelve /grantable; sin `tenantId`, el de su sesión).
// Misma fuente que la configuración de permisos por rol (RbacAdmin): `getAccessibleModules` — mismos
// módulos, mismo orden, mismos nombres; el catálogo NO se duplica.

export interface ModulePermissionsSectionProps {
  userId: string;
  /** Solo Super Admin: tenant del usuario (`?tenantId=`). Los demás administradores usan el de su sesión. */
  tenantId?: string;
  mode: "superadmin" | "admin";
  /**
   * Ids de los roles del usuario (el listado de usuarios trae una fila por rol). Con ellos se calcula
   * «Heredado del rol». Sin roles, todo lo heredado se muestra deshabilitado.
   */
  roleIds?: string[];
  /** Solo lectura: se ven los estados pero no hay controles de cambio. */
  readOnly?: boolean;
  /** Motivo mostrado cuando `readOnly` (p. ej. «Solo el Super Admin modifica a otro administrador»). */
  readOnlyReason?: string;
  /** El usuario destino es Super Admin: AC4, sin controles. */
  targetIsSuperAdmin?: boolean;
  /** La invitación del usuario sigue pendiente: AC5, sección deshabilitada. */
  invitationPending?: boolean;
  /** Se llama tras cada cambio aplicado (p. ej. para refrescar chips o contadores del llamador). */
  onChanged?: (overrides: UserPermissionOverride[]) => void;
}

const ORIGIN_LABEL: Record<PermissionOrigin, string> = {
  inherited: "Heredado del rol",
  individual: "Individual",
  partial: "Parcial",
};

const ORIGIN_ICON = { inherited: ShieldCheck, individual: UserCog, partial: CircleDot } as const;

type LoadState =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "superadmin" }
  | { status: "pending" }
  | {
      status: "ready";
      modules: AccessibleModule[];
      grantable: GrantablePermissionItem[];
      roleIds: Set<string>;
      overrides: UserPermissionOverride[];
      /** HU #13655: permisos que salieron del tope y se muestran bloqueados aunque ya no tengan override. */
      locked: Set<string>;
    };

async function fetchRolePermissionIds(mode: "superadmin" | "admin", roleIds: string[]): Promise<Set<string>> {
  const ids = new Set<string>();
  const details = await Promise.all(
    roleIds.map((id) =>
      mode === "superadmin" ? superadminClient.getRole(id) : getTenantRole(id),
    ),
  );
  for (const d of details) for (const p of d.permissions) ids.add(p.id);
  return ids;
}

function OriginBadge({ origin }: { origin: PermissionOrigin }) {
  const Icon = ORIGIN_ICON[origin];
  return (
    <span
      className="inline-flex items-center gap-1 rounded-full border border-[#DFE5ED] px-2 py-0.5 text-[10px] font-semibold text-[#162744] dark:border-white/20 dark:text-white"
      data-origin={origin}
    >
      <Icon className="h-3 w-3" aria-hidden="true" />
      {ORIGIN_LABEL[origin]}
    </span>
  );
}

/** HU #13655 (FD2-3): textos del chip de permisos fuera del tope del administrador. */
export const OUT_OF_CAP_LABEL = "Fuera de tu tope";
export const OUT_OF_CAP_TOOLTIP = "Lo otorgó otro administrador. Puedes quitarlo, pero no volver a otorgarlo.";
const OUT_OF_CAP_LOCKED_TOOLTIP = "Está fuera de lo que puedes otorgar.";

function OutOfCapChip({ tooltip, id }: { tooltip: string; id: string }) {
  return (
    <span
      tabIndex={0}
      title={tooltip}
      aria-describedby={id}
      data-testid="out-of-cap-chip"
      className="inline-flex items-center gap-1 rounded-full border border-current px-2 py-0.5 text-[10px] font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]/40"
      style={{ color: "var(--badge-danger-fg)" }}
    >
      <Lock className="h-3 w-3" aria-hidden="true" />
      {OUT_OF_CAP_LABEL}
      <span id={id} role="tooltip" className="sr-only">
        {tooltip}
      </span>
    </span>
  );
}

function StateText({ enabled }: { enabled: boolean }) {
  return (
    <span
      className="text-[10px] font-semibold"
      style={{ color: enabled ? "var(--permiso-habilitado)" : "var(--permiso-deshabilitado)" }}
    >
      {enabled ? "Habilitado" : "Deshabilitado"}
    </span>
  );
}

export function ModulePermissionsSection({
  userId,
  tenantId,
  mode,
  roleIds,
  readOnly = false,
  readOnlyReason,
  targetIsSuperAdmin = false,
  invitationPending = false,
  onChanged,
}: ModulePermissionsSectionProps) {
  const [state, setState] = useState<LoadState>({ status: "loading" });
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // El tenant solo viaja para el Super Admin; los demás administradores usan el de su JWT.
  const tenantParam = mode === "superadmin" ? tenantId : undefined;
  const roleKey = (roleIds ?? []).join(",");
  const skip = targetIsSuperAdmin || invitationPending;

  const fetchAll = useCallback(async (): Promise<LoadState> => {
    try {
      const [modules, grantable, overrides, roleSet] = await Promise.all([
        getAccessibleModules(),
        listGrantablePermissions(tenantParam),
        listUserOverrides(userId, tenantParam),
        fetchRolePermissionIds(mode, roleKey ? roleKey.split(",") : []),
      ]);
      return { status: "ready", modules, grantable, roleIds: roleSet, overrides, locked: new Set(outOfCapOverrideIds(grantable, overrides)) };
    } catch (e) {
      const code = overrideErrorCode(e);
      if (code === "TARGET_SUPERADMIN") return { status: "superadmin" };
      if (code === "INVITATION_PENDING") return { status: "pending" };
      return { status: "error", message: overrideErrorMessage(e, "No se pudieron cargar los permisos del usuario.") };
    }
  }, [tenantParam, userId, mode, roleKey]);

  useEffect(() => {
    if (skip) return;
    let cancelled = false;
    void fetchAll().then((next) => {
      if (!cancelled) setState(next);
    });
    return () => {
      cancelled = true;
    };
  }, [skip, fetchAll]);

  const retry = () => {
    setState({ status: "loading" });
    void fetchAll().then(setState);
  };

  const views = useMemo<ModuleView[]>(
    () => (state.status === "ready" ? buildModuleViews(state.modules, state.grantable, state.roleIds, state.overrides, state.locked) : []),
    [state],
  );

  async function refreshOverrides() {
    const overrides = await listUserOverrides(userId, tenantParam);
    setState((prev) =>
      prev.status === "ready"
        ? { ...prev, overrides, locked: new Set([...prev.locked, ...outOfCapOverrideIds(prev.grantable, overrides)]) }
        : prev,
    );
    onChanged?.(overrides);
  }

  async function run(task: () => Promise<void>) {
    setBusy(true);
    setActionError(null);
    setNotice(null);
    try {
      await task();
      await refreshOverrides();
    } catch (e) {
      const code = overrideErrorCode(e);
      if (code === "TARGET_SUPERADMIN") setState({ status: "superadmin" });
      else if (code === "INVITATION_PENDING") setState({ status: "pending" });
      else setActionError(overrideErrorMessage(e));
      // Un 404 de override ya quitado: la lista quedó vieja; se refresca en silencio.
      if (code === "OVERRIDE_NOT_FOUND") await refreshOverrides().catch(() => undefined);
    } finally {
      setBusy(false);
    }
  }

  const toggleAction = (a: ActionView) =>
    run(async () => {
      await setUserOverride(userId, a.id, a.enabled ? "deny" : "allow", tenantParam);
    });

  const backToRole = (a: ActionView) =>
    run(async () => {
      await removeUserOverride(userId, a.id, tenantParam);
    });

  const setModule = (m: ModuleView, effect: OverrideEffect) =>
    run(async () => {
      const r = await setModuleOverrides(userId, m.code, effect, tenantParam);
      const parts: string[] = [`${r.created} ${r.created === 1 ? "acción aplicada" : "acciones aplicadas"}`];
      if (r.skipped > 0) parts.push(`${r.skipped} ya tenían un permiso individual`);
      if (r.outOfCap > 0) parts.push(`${r.outOfCap} quedaron fuera de lo que puedes otorgar`);
      if (r.excluded > 0) parts.push(`${r.excluded} no se configuran individualmente`);
      setNotice(`Módulo «${m.name}»: ${parts.join("; ")}.`);
    });

  const moduleBackToRole = (m: ModuleView) =>
    run(async () => {
      for (const a of m.actions.filter((x) => x.origin === "individual")) {
        await removeUserOverride(userId, a.id, tenantParam);
      }
    });

  if (targetIsSuperAdmin || state.status === "superadmin") {
    return <InlineAlert tone="info">{TARGET_SUPERADMIN_MESSAGE}</InlineAlert>;
  }
  if (invitationPending || state.status === "pending") {
    return (
      <div aria-disabled="true" className="opacity-90">
        <InlineAlert tone="info">{INVITATION_PENDING_MESSAGE}</InlineAlert>
      </div>
    );
  }

  const controlsOff = readOnly || busy;

  return (
    <section aria-label="Permisos por módulo" className="flex flex-col gap-3" aria-busy={busy || state.status === "loading"}>
      {readOnly && (
        <InlineAlert tone="info" title="Solo lectura">
          {readOnlyReason ?? "No puedes modificar los permisos de este usuario."}
        </InlineAlert>
      )}
      {actionError && <InlineAlert tone="error">{actionError}</InlineAlert>}
      {notice && <InlineAlert tone="success">{notice}</InlineAlert>}

      <UiStateBoundary
        status={
          state.status === "loading" ? "loading" : state.status === "error" ? "error" : views.length === 0 ? "empty" : "ready"
        }
        errorMessage={state.status === "error" ? state.message : undefined}
        onRetry={retry}
        emptyMessage="No hay permisos que se puedan configurar individualmente para este usuario."
      >
        <ul className="flex flex-col gap-3">
          {views.map((m) => (
            <li key={m.id} className="rounded-xl border border-[#DFE5ED] p-3 dark:border-white/15" data-testid={`module-${m.code}`}>
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="text-sm font-semibold text-[#162744] dark:text-white">{m.name}</h3>
                <OriginBadge origin={m.origin} />
                <span className="text-[10px] opacity-60">
                  {m.enabledCount} de {m.actions.length} habilitadas
                </span>
                {!readOnly && (
                  <div className="ml-auto flex flex-wrap gap-1.5">
                    <button
                      type="button"
                      disabled={controlsOff}
                      onClick={() => void setModule(m, "allow")}
                      aria-label={`Habilitar el módulo ${m.name} completo`}
                      className="rounded-lg border border-[#DFE5ED] px-2.5 py-1 text-[11px] font-semibold disabled:opacity-50"
                      style={{ color: "var(--permiso-habilitado)" }}
                    >
                      Habilitar módulo
                    </button>
                    <button
                      type="button"
                      disabled={controlsOff}
                      onClick={() => void setModule(m, "deny")}
                      aria-label={`Denegar el módulo ${m.name} completo`}
                      className="rounded-lg border border-[#DFE5ED] px-2.5 py-1 text-[11px] font-semibold disabled:opacity-50"
                      style={{ color: "var(--permiso-deshabilitado)" }}
                    >
                      Denegar módulo
                    </button>
                    {m.origin !== "inherited" && (
                      <button
                        type="button"
                        disabled={controlsOff}
                        onClick={() => void moduleBackToRole(m)}
                        aria-label={`Volver el módulo ${m.name} a lo que dicta el rol`}
                        className="inline-flex items-center gap-1 rounded-lg border border-[#DFE5ED] px-2.5 py-1 text-[11px] font-semibold disabled:opacity-50"
                      >
                        <RotateCcw className="h-3 w-3" aria-hidden="true" />
                        Volver al rol
                      </button>
                    )}
                  </div>
                )}
              </div>

              <ul className="mt-2 flex flex-col divide-y divide-[#DFE5ED] dark:divide-white/10">
                {m.actions.map((a) => (
                  <li key={a.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2">
                    <button
                      type="button"
                      role="switch"
                      aria-checked={a.enabled}
                      aria-label={`${a.name}, módulo ${m.name}`}
                      disabled={controlsOff || (mode === "admin" && a.outOfCap === true)}
                      onClick={() => void toggleAction(a)}
                      className="relative h-5 w-9 shrink-0 rounded-full border transition disabled:opacity-50"
                      style={{
                        background: a.enabled ? "var(--permiso-habilitado)" : "transparent",
                        borderColor: a.enabled ? "var(--permiso-habilitado)" : "var(--permiso-deshabilitado)",
                      }}
                    >
                      <span
                        aria-hidden="true"
                        className="absolute top-0.5 h-3.5 w-3.5 rounded-full bg-white transition-all"
                        style={{
                          left: a.enabled ? "1.15rem" : "0.15rem",
                          background: a.enabled ? "#FFFFFF" : "var(--permiso-deshabilitado)",
                        }}
                      />
                    </button>
                    <span className="min-w-0 flex-1 text-xs">
                      <span className="font-medium">{a.name}</span>
                      {a.origin === "individual" && a.grantedByName && (
                        <span className="block text-[10px] opacity-60">
                          Otorgado por {a.grantedByName}
                          {a.grantedAt ? ` el ${formatFechaHora(a.grantedAt)}` : ""}
                        </span>
                      )}
                    </span>
                    <StateText enabled={a.enabled} />
                    <OriginBadge origin={a.origin} />
                    {mode === "admin" && a.outOfCap && (
                      <OutOfCapChip
                        id={`cap-${m.code}-${a.id}`}
                        tooltip={a.origin === "individual" ? OUT_OF_CAP_TOOLTIP : OUT_OF_CAP_LOCKED_TOOLTIP}
                      />
                    )}
                    {a.origin === "individual" && !readOnly && (
                      <button
                        type="button"
                        disabled={controlsOff}
                        onClick={() => void backToRole(a)}
                        aria-label={`Volver ${a.name} a lo que dicta el rol`}
                        className="inline-flex items-center gap-1 rounded-lg border border-[#DFE5ED] px-2 py-1 text-[10px] font-semibold disabled:opacity-50"
                      >
                        <RotateCcw className="h-3 w-3" aria-hidden="true" />
                        Volver al rol
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      </UiStateBoundary>
    </section>
  );
}
