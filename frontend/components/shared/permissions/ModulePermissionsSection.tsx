"use client";

import { useCallback, useEffect, useId, useMemo, useState } from "react";
import { CircleDot, RotateCcw, ShieldCheck, UserCog } from "lucide-react";
import { getAccessibleModules, type AccessibleModule } from "@/lib/api/security";
import {
  INVITATION_PENDING_MESSAGE,
  PII_REVEAL_SLUG,
  TARGET_SUPERADMIN_MESSAGE,
  listGrantablePermissions,
  listUserOverrides,
  listUserRolePermissionIds,
  overrideErrorCode,
  overrideErrorMessage,
  removeUserOverride,
  setModuleOverrides,
  setUserOverride,
  type GrantablePermissionItem,
  type OverrideEffect,
  type UserPermissionOverride,
} from "@/lib/api/permission-overrides";
import { buildModuleViews, outOfCapOverrideIds, withOverrideActions, type ActionView, type ModuleView, type PermissionOrigin } from "@/lib/permissions/modulePermissions";
import { formatFechaHora } from "@/lib/format/date";
import { findFamilyPair } from "@/lib/permissions/familyPairs";
import { FamilyPairRows } from "./FamilyPairRows";
import {
  OUT_OF_CAP_LABEL,
  OUT_OF_CAP_LOCKED_TOOLTIP,
  OUT_OF_CAP_TOOLTIP,
  OutOfCapChip,
  SOLO_SA_TOOLTIP,
  SoloSaChip,
} from "./PermissionChips";

export { OUT_OF_CAP_LABEL, OUT_OF_CAP_TOOLTIP, SOLO_SA_TOOLTIP };
import { fetchSessionPolicy } from "@/lib/api/session-policy";
import { mensajeVigencia } from "@/lib/auth/vigencia";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { Modal } from "@/components/atom/Modal";
import { UiStateBoundary } from "@flit/ui/UiStateBoundary";

// HU #13652 (Feature #13103 F-D1, Épica #12753) — sección COMPARTIDA «Permisos por módulo».
// La usa el Panel SA (`mode="superadmin"`) y la reutilizará F-D2 en las fichas del Admin de Compañía y del
// Admin OT (`mode="admin"`: tope = lo que devuelve /grantable; sin `tenantId`, el de su sesión).
// Misma fuente que la configuración de permisos por rol (RbacAdmin): `getAccessibleModules` — mismos
// módulos, mismo orden, mismos nombres; el catálogo NO se duplica.

export interface ModulePermissionsSectionProps {
  userId: string;
  /** Nombre del usuario destino para el aviso de vigencia («…cuando Ana inicie sesión…»). */
  userName?: string;
  /** Solo Super Admin: tenant del usuario (`?tenantId=`). Los demás administradores usan el de su sesión. */
  tenantId?: string;
  mode: "superadmin" | "admin";
  /**
   * Ya no se usa: «Heredado del rol» sale de GET .../permission-overrides/role-permissions (todos los roles del usuario,
   * con las reglas de ámbito del backend). Se conserva para no romper a los llamadores.
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

/** HU #13653 (AC2): advertencia Ley 1581 del diálogo de confirmación de `ict.pii.reveal`. */
export const PII_WARNING =
  "Este permiso da acceso a datos personales protegidos por la Ley 1581 de 2012. Confirma que el usuario lo requiere para su función.";
const VIGENCIA_FALLBACK = "El cambio se aplicará la próxima vez que la persona inicie sesión.";

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

// Lo heredado lo calcula la API con todos los roles activos del usuario. Leer cada rol con GET /security/roles/{id} exigía
// AdminCompany: al Admin OT la sección no le cargaba (hallazgo de la prueba funcional).
async function fetchRolePermissionIds(userId: string, tenantId: string | undefined): Promise<Set<string>> {
  return new Set((await listUserRolePermissionIds(userId, tenantId)).permissionIds);
}

function isFamilyPairAction(actions: ActionView[], a: ActionView): boolean {
  const pair = findFamilyPair(actions);
  return pair !== null && (a.id === pair.read.id || a.id === pair.create.id);
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

/** HU #13653 (AC2): confirmación Ley 1581 antes de otorgar `ict.pii.reveal`. Cancelar no cambia nada. */
function PiiConfirmDialog({
  permissionName,
  busy,
  onCancel,
  onConfirm,
}: {
  permissionName: string;
  busy: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const [checked, setChecked] = useState(false);
  const checkId = useId();
  return (
    <Modal open onClose={onCancel} size="sm" icon={ShieldCheck} title={`Otorgar «${permissionName}»`} busy={busy}>
      <div className="flex flex-col gap-4">
        <InlineAlert tone="warning">{PII_WARNING}</InlineAlert>
        <div className="flex items-center gap-2">
          <input
            id={checkId}
            type="checkbox"
            checked={checked}
            onChange={(e) => setChecked(e.target.checked)}
            disabled={busy}
            className="h-4 w-4"
          />
          <label htmlFor={checkId} className="text-sm font-medium">
            Confirmo
          </label>
        </div>
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-lg border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={!checked || busy}
            className="rounded-lg px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: "var(--permiso-habilitado)" }}
          >
            Otorgar
          </button>
        </div>
      </div>
    </Modal>
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
  userName,
  tenantId,
  mode,
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
  // HU #13653 (AC3): aviso de vigencia tras guardar (FC-3) y permiso PII a la espera de confirmación (AC2).
  const [vigencia, setVigencia] = useState<string | null>(null);
  const [pendingPii, setPendingPii] = useState<ActionView | null>(null);

  // El tenant solo viaja para el Super Admin; los demás administradores usan el de su JWT.
  const tenantParam = mode === "superadmin" ? tenantId : undefined;
  const skip = targetIsSuperAdmin || invitationPending;

  const fetchAll = useCallback(async (): Promise<LoadState> => {
    try {
      const [modules, grantable, overrides, roleSet] = await Promise.all([
        getAccessibleModules(),
        listGrantablePermissions(tenantParam),
        listUserOverrides(userId, tenantParam),
        fetchRolePermissionIds(userId, tenantParam),
      ]);
      return {
        status: "ready",
        modules: withOverrideActions(modules, overrides),
        grantable,
        roleIds: roleSet,
        overrides,
        locked: new Set(outOfCapOverrideIds(grantable, overrides)),
      };
    } catch (e) {
      const code = overrideErrorCode(e);
      if (code === "TARGET_SUPERADMIN") return { status: "superadmin" };
      if (code === "INVITATION_PENDING") return { status: "pending" };
      return { status: "error", message: overrideErrorMessage(e, "No se pudieron cargar los permisos del usuario.") };
    }
  }, [tenantParam, userId]);

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
        ? {
            ...prev,
            modules: withOverrideActions(prev.modules, overrides),
            overrides,
            locked: new Set([...prev.locked, ...outOfCapOverrideIds(prev.grantable, overrides)]),
          }
        : prev,
    );
    onChanged?.(overrides);
  }

  async function run(task: () => Promise<void>) {
    setBusy(true);
    setActionError(null);
    setNotice(null);
    setVigencia(null);
    try {
      await task();
      await refreshOverrides();
      // El cambio ya está guardado: si la política de sesión no responde, se avisa sin ella (no es un error del guardado).
      try {
        setVigencia(mensajeVigencia(userName?.trim() || "el usuario", await fetchSessionPolicy()));
      } catch {
        setVigencia(VIGENCIA_FALLBACK);
      }
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

  const toggleAction = (a: ActionView) => {
    // AC2: habilitar ict.pii.reveal pasa por la confirmación Ley 1581; no se guarda nada hasta confirmarla.
    if (!a.enabled && a.slug === PII_REVEAL_SLUG) {
      setPendingPii(a);
      return Promise.resolve();
    }
    return run(async () => {
      await setUserOverride(userId, a.id, a.enabled ? "deny" : "allow", tenantParam);
    });
  };

  const confirmPii = async () => {
    const a = pendingPii;
    if (!a) return;
    await run(async () => {
      await setUserOverride(userId, a.id, "allow", tenantParam, true);
    });
    setPendingPii(null);
  };

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
      if (r.excluded > 0) parts.push(`${r.excluded} se otorgan una a una (p. ej. con confirmación) o no se configuran individualmente`);
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
      {(notice || vigencia) && (
        <InlineAlert tone="success" title="Cambio guardado">
          {notice && <p>{notice}</p>}
          {vigencia && <p>{vigencia}</p>}
        </InlineAlert>
      )}
      {pendingPii && (
        <PiiConfirmDialog
          permissionName={pendingPii.name}
          busy={busy}
          onCancel={() => setPendingPii(null)}
          onConfirm={() => void confirmPii()}
        />
      )}

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
                {(() => {
                  // HU #13666: par Ver / Crear por tipo de servicio (módulos tramites-matriculas/-traspasos/-otros).
                  const pair = findFamilyPair(m.actions);
                  return pair ? (
                    <FamilyPairRows
                      moduleName={m.name}
                      read={pair.read}
                      create={pair.create}
                      readOnly={readOnly}
                      showOutOfCap={mode === "admin"}
                      controlsOff={controlsOff || (mode === "admin" && (pair.read.outOfCap === true || pair.create.outOfCap === true))}
                      onToggle={(a) => void toggleAction(a)}
                      onBackToRole={(a) => void backToRole(a)}
                    />
                  ) : null;
                })()}
                {m.actions.filter((a) => !isFamilyPairAction(m.actions, a)).map((a) => (
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
                      {a.superAdminOnly && (
                        <span className="ml-2 align-middle">
                          <SoloSaChip />
                        </span>
                      )}
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
