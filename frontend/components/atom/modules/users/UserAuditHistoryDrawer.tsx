"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { History, Loader2, X } from "lucide-react";
import { fetchAdminAuditLog } from "@/lib/api/audit";
import type { AdminAuditLogEntry, AdminAuditModule } from "@/lib/api/types";
import { ApiError } from "@/lib/api/types";

const MODULE_LABEL: Record<AdminAuditModule, string> = {
  users: "Usuarios",
  roles: "Roles",
  permissions: "Permisos",
  authentication: "Autenticación",
  security: "Seguridad",
  config: "Configuración",
};

/**
 * Verbos en español del vocabulario estable de auditoría (`AuditVocabulary.Operations`).
 * Sin esto la UI mostraba el código crudo (`assign_role`, `delete_user`…).
 */
const OPERATION_LABEL: Record<string, string> = {
  create: "Creó el registro",
  update: "Actualizó los datos",
  delete: "Eliminó el registro",
  assign_role: "Asignó un rol",
  revoke_role: "Revocó un rol",
  suspend: "Suspendió la cuenta",
  unsuspend: "Reactivó la cuenta",
  invite: "Envió una invitación",
  resend_invite: "Reenvió la invitación",
  delete_user: "Eliminó el usuario",
  login: "Inició sesión",
  login_failed: "Intento de inicio de sesión fallido",
  logout: "Cerró sesión",
  forgot_password: "Solicitó recuperar la contraseña",
  reset_password: "Restableció la contraseña",
  change_password: "Cambió la contraseña",
  admin_reset_password: "Restableció la contraseña de otro usuario",
  activate_account: "Activó la cuenta",
};

/** Etiquetas legibles de los campos que viajan en el detalle del cambio. */
const FIELD_LABEL: Record<string, string> = {
  nombre: "Nombre",
  correo: "Correo",
  roles: "Roles",
  rolAsignado: "Rol asignado",
};

function formatFecha(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short" }).format(d);
}

export function operationLabel(entry: AdminAuditLogEntry): string {
  const op = entry.operation?.trim();
  if (op && OPERATION_LABEL[op]) return OPERATION_LABEL[op];
  if (op) return op.replaceAll("_", " ");
  return "Cambio registrado";
}

export function actorLabel(entry: AdminAuditLogEntry): string {
  const name = entry.changedByName?.trim();
  if (name) return name;
  const mail = entry.changedByEmail?.trim();
  if (mail) return mail;
  // Sin actor resoluble: eventos del sistema o de cuentas ya eliminadas.
  return entry.changedBy ? "Usuario no disponible" : "Sistema";
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (Array.isArray(value)) return value.length > 0 ? value.join(", ") : "—";
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

function parseDetail(raw: string | null | undefined): Record<string, unknown> | null {
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

export interface AuditChange {
  field: string;
  before: string;
  after: string;
}

/**
 * Convierte el par old/new del rastro en una lista de campos con antes → después, quedándose
 * solo con los que realmente cambiaron. Exportada para poder testear la lógica sin montar el
 * drawer.
 */
export function describeChanges(entry: AdminAuditLogEntry): AuditChange[] {
  const before = parseDetail(entry.oldValue);
  const after = parseDetail(entry.newValue);
  if (!before && !after) return [];

  const keys = Array.from(new Set([...Object.keys(before ?? {}), ...Object.keys(after ?? {})]));

  return keys
    .map((key) => ({
      field: FIELD_LABEL[key] ?? key,
      before: formatValue(before?.[key]),
      after: formatValue(after?.[key]),
    }))
    .filter((c) => c.before !== c.after);
}

export interface UserAuditHistoryDrawerProps {
  userId: string;
  userLabel: string;
  onClose: () => void;
}

/**
 * Drawer SuperAdmin-only: historial de cambios del usuario vía
 * GET /api/v1/superadmin/audit?userId= (HU #10679/#10680). Responde las tres preguntas del
 * requerimiento — quién, cuándo y qué cambió — con nombres y verbos en español; antes mostraba
 * el UUID del actor y el código crudo de la operación.
 */
export function UserAuditHistoryDrawer({ userId, userLabel, onClose }: UserAuditHistoryDrawerProps) {
  const [entries, setEntries] = useState<AdminAuditLogEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const reqIdRef = useRef(0);

  const load = useCallback(async () => {
    const reqId = ++reqIdRef.current;
    setLoading(true);
    setError(null);
    try {
      // Sin filtro de módulo: el userId matchea actor O afectado en cualquiera.
      const res = await fetchAdminAuditLog({ userId, page: 1, pageSize: 50 });
      if (reqId !== reqIdRef.current) return;
      setEntries(res.data);
    } catch (err) {
      if (reqId !== reqIdRef.current) return;
      if (err instanceof ApiError && err.status === 403) {
        setError("No tienes permiso para ver el historial de auditoría.");
      } else {
        setError("No se pudo cargar el historial. Inténtalo de nuevo.");
      }
      setEntries(null);
    } finally {
      if (reqId === reqIdRef.current) setLoading(false);
    }
  }, [userId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const items = useMemo(
    () => (entries ?? []).map((e) => ({ entry: e, changes: describeChanges(e) })),
    [entries],
  );

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/40 backdrop-blur-sm" role="presentation">
      <button
        type="button"
        className="absolute inset-0 cursor-default"
        aria-label="Cerrar historial"
        onClick={onClose}
      />
      <aside
        role="dialog"
        aria-modal="true"
        aria-labelledby="user-audit-title"
        className="relative z-10 flex h-full w-full max-w-md flex-col border-l bg-white shadow-xl dark:bg-[#0B0F14]"
      >
        <header className="flex items-start justify-between gap-3 border-b px-4 py-4">
          <div className="min-w-0">
            <div className="mb-1 flex items-center gap-2" style={{ color: "#557EFF" }}>
              <History className="h-4 w-4 shrink-0" aria-hidden />
              <span className="text-[10px] font-semibold uppercase tracking-wider">Auditoría</span>
            </div>
            <h2 id="user-audit-title" className="truncate text-sm font-bold">
              Historial de {userLabel}
            </h2>
            <p className="mt-0.5 text-[11px] opacity-60">Quién cambió qué y cuándo (máx. 50 eventos).</p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar"
            className="rounded-lg p-1.5 transition hover:bg-black/5"
          >
            <X className="h-4 w-4" />
          </button>
        </header>

        <div className="flex-1 overflow-y-auto px-4 py-3">
          {loading && (
            <div role="status" className="flex flex-col items-center gap-2 py-16 text-sm opacity-60">
              <Loader2 className="h-5 w-5 animate-spin" style={{ color: "#557EFF" }} />
              Cargando historial…
            </div>
          )}

          {!loading && error && (
            <div role="alert" className="space-y-3 py-10 text-center text-sm" style={{ color: "#FF4E00" }}>
              <p>{error}</p>
              <button
                type="button"
                onClick={() => void load()}
                className="rounded-xl px-3 py-1.5 text-xs font-semibold text-white"
                style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
              >
                Reintentar
              </button>
            </div>
          )}

          {!loading && !error && entries && entries.length === 0 && (
            <div className="py-16 text-center text-sm opacity-60">
              No hay eventos de auditoría registrados para este usuario.
            </div>
          )}

          {!loading && !error && items.length > 0 && (
            <ul className="space-y-2">
              {items.map(({ entry, changes }) => {
                const failed = entry.result === "failure";
                return (
                  <li
                    key={entry.id}
                    className="rounded-xl border px-3 py-2.5 text-xs"
                    style={{ borderColor: failed ? "rgba(255,78,0,0.35)" : "#DFE5ED" }}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <p className="font-semibold">{operationLabel(entry)}</p>
                      <time className="shrink-0 text-[10px] opacity-60" dateTime={entry.changedAt}>
                        {formatFecha(entry.changedAt)}
                      </time>
                    </div>

                    <p className="mt-1 text-[11px] opacity-80">
                      Por <strong>{actorLabel(entry)}</strong>
                      {entry.targetName ? (
                        <>
                          {" "}
                          sobre <strong>{entry.targetName}</strong>
                        </>
                      ) : null}
                    </p>

                    {changes.length > 0 && (
                      <ul className="mt-1.5 space-y-0.5 border-t pt-1.5">
                        {changes.map((c) => (
                          <li key={c.field} className="text-[11px]">
                            <span className="opacity-60">{c.field}:</span>{" "}
                            <span className="line-through opacity-60">{c.before}</span>{" "}
                            <span aria-hidden>→</span> <span className="font-medium">{c.after}</span>
                          </li>
                        ))}
                      </ul>
                    )}

                    <p className="mt-1 flex flex-wrap items-center gap-x-2 text-[10px] opacity-60">
                      <span>{entry.module ? (MODULE_LABEL[entry.module] ?? entry.module) : "—"}</span>
                      {failed && (
                        <span style={{ color: "#FF4E00" }}>
                          No se completó{entry.errorCode ? ` (${entry.errorCode})` : ""}
                        </span>
                      )}
                      {entry.clientIp && <span>IP {entry.clientIp}</span>}
                    </p>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </aside>
    </div>
  );
}
