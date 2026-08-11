"use client";

// Submódulo "Clientes ICT" (ronda 2, Feature #10888): CRUD de las credenciales de integración
// (ict.integration_clients) que usan los gestores para registrar pre-trámites. SuperAdmin administra
// cualquier compañía (selector); un admin no-super, solo la suya. El SECRETO lo genera el sistema y se
// muestra UNA sola vez (no se puede recuperar) — se regenera con "Regenerar secreto".
import { useCallback, useEffect, useState } from "react";
import { Check, Copy, KeyRound, Plus, RotateCcw, X } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ApiError } from "@/lib/api/types";
import {
  createIctClient,
  fetchIctClients,
  resetIctClientSecret,
  updateIctClient,
  type IctClient,
} from "@/lib/api/ict-clients";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import type { CompanyListItem } from "@/lib/api/types";
import { SearchableSelect } from "@/components/atom/SearchableSelect";

interface Props {
  isSuperAdmin: boolean;
  tenantId: string | null;
}

function errorMessage(e: unknown): string {
  if (e instanceof ApiError) {
    const code = (e.body as { error?: string } | undefined)?.error;
    switch (code) {
      case "username_taken":
        return "Ya existe un cliente con ese usuario.";
      case "invalid_username":
        return "El usuario debe tener entre 3 y 50 caracteres.";
      case "tenant_not_found":
        return "La compañía seleccionada no existe.";
      case "tenant_inactive":
        return "La compañía seleccionada está inactiva.";
      case "tenant_forbidden":
        return "No puede crear clientes para otra compañía.";
      case "not_found":
        return "El cliente no existe.";
      default:
        return e.message;
    }
  }
  return e instanceof Error ? e.message : "Error inesperado.";
}

function parseScopes(scopes: string): string {
  try {
    const arr = JSON.parse(scopes) as unknown;
    return Array.isArray(arr) ? arr.join(", ") : scopes;
  } catch {
    return scopes;
  }
}

export function IctClientsPanel({ isSuperAdmin, tenantId }: Props) {
  const [clients, setClients] = useState<IctClient[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [username, setUsername] = useState("");
  const [companyId, setCompanyId] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [revealed, setRevealed] = useState<{ username: string; secret: string } | null>(null);
  const [copied, setCopied] = useState(false);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      // SuperAdmin ve todas las compañías; el resto, solo la suya (lo resuelve el backend por el token).
      const res = await fetchIctClients();
      setClients(res.items);
      setStatus(res.items.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  useEffect(() => {
    if (!isSuperAdmin) return;
    const controller = new AbortController();
    fetchCompaniesIndex({ page: 1, pageSize: 200 }, controller.signal)
      .then((r) => setCompanies(r.data.filter((c) => c.estadoActivo)))
      .catch(() => {
        // El selector de compañía es opcional: si falla, el alta queda deshabilitada para SuperAdmin.
      });
    return () => controller.abort();
  }, [isSuperAdmin]);

  const effectiveTenantId = isSuperAdmin ? companyId : tenantId ?? "";

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setFormError(null);
    if (username.trim().length < 3) {
      setFormError("El usuario debe tener al menos 3 caracteres.");
      return;
    }
    if (!effectiveTenantId) {
      setFormError(isSuperAdmin ? "Seleccione una compañía." : "No se pudo determinar su compañía.");
      return;
    }

    setCreating(true);
    try {
      const res = await createIctClient({ username: username.trim(), tenantId: effectiveTenantId });
      setClients((prev) => [res.client, ...prev]);
      setRevealed({ username: res.client.username, secret: res.generatedSecret });
      setUsername("");
      setCompanyId("");
      setShowForm(false);
      setStatus("ready");
    } catch (err) {
      setFormError(errorMessage(err));
    } finally {
      setCreating(false);
    }
  }

  async function toggleActive(client: IctClient) {
    setBusyId(client.id);
    try {
      const updated = await updateIctClient(client.id, { isActive: !client.isActive });
      setClients((prev) => prev.map((c) => (c.id === client.id ? updated : c)));
    } catch {
      // Silencioso: el estado no cambia; el usuario puede reintentar.
    } finally {
      setBusyId(null);
    }
  }

  async function handleResetSecret(client: IctClient) {
    setBusyId(client.id);
    try {
      const res = await resetIctClientSecret(client.id);
      setClients((prev) => prev.map((c) => (c.id === client.id ? res.client : c)));
      setRevealed({ username: res.client.username, secret: res.generatedSecret });
    } catch {
      // Silencioso.
    } finally {
      setBusyId(null);
    }
  }

  function copySecret() {
    if (!revealed) return;
    void navigator.clipboard?.writeText(revealed.secret).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    });
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <p className="text-xs text-slate-500">
          Credenciales que usan los gestores para registrar pre-trámites por integración. El secreto se
          muestra una sola vez al crear o regenerar.
        </p>
        <button
          type="button"
          onClick={() => {
            setShowForm((v) => !v);
            setFormError(null);
          }}
          className="inline-flex shrink-0 items-center gap-2 rounded-xl px-3 py-1.5 text-sm font-semibold text-white"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Plus className="h-4 w-4" /> Nuevo cliente
        </button>
      </div>

      {revealed && (
        <div className="rounded-xl border border-[#557EFF]/40 bg-[#557EFF]/5 p-3">
          <div className="flex items-start justify-between gap-3">
            <div className="flex flex-col gap-1">
              <p className="text-xs font-semibold text-[#162744] dark:text-slate-100">
                <KeyRound className="mr-1 inline h-3.5 w-3.5" />
                Secreto de «{revealed.username}» — cópielo ahora, no se volverá a mostrar.
              </p>
              <code className="rounded bg-white px-2 py-1 font-mono text-sm text-[#162744] dark:bg-slate-900 dark:text-slate-100">
                {revealed.secret}
              </code>
            </div>
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={copySecret}
                className="inline-flex items-center gap-1 rounded-lg border border-slate-300 px-2 py-1 text-xs text-[#557EFF] hover:bg-[#557EFF]/10"
              >
                {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                {copied ? "Copiado" : "Copiar"}
              </button>
              <button
                type="button"
                aria-label="Cerrar"
                onClick={() => setRevealed(null)}
                className="rounded-lg border border-slate-300 p-1 text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        </div>
      )}

      {showForm && (
        <form onSubmit={handleCreate} className="flex flex-wrap items-end gap-3 rounded-xl border border-slate-200 p-3 dark:border-slate-700">
          <label className="flex flex-col gap-1 text-xs text-slate-500">
            Usuario
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="usuario del gestor"
              className="rounded border border-slate-300 bg-white px-2 py-1 text-sm dark:bg-slate-800 dark:text-white"
            />
          </label>
          {isSuperAdmin && (
            <SearchableSelect
              id="ict-clients-compania"
              label="Compañía"
              options={companies.map((c) => ({ value: c.id, label: c.razonSocial, hint: c.nit }))}
              value={companyId}
              onChange={setCompanyId}
              placeholder="Buscar compañía…"
              className="min-w-[220px]"
            />
          )}
          <button
            type="submit"
            disabled={creating}
            className="rounded-md bg-[#557EFF] px-3 py-1.5 text-sm font-medium text-white disabled:opacity-40"
          >
            {creating ? "Creando…" : "Crear cliente"}
          </button>
          {formError && <span className="text-xs text-[#FF4E00]">{formError}</span>}
        </form>
      )}

      <UiStateBoundary
        status={status}
        emptyMessage="No hay clientes ICT registrados."
        errorMessage="No se pudieron cargar los clientes ICT."
        onRetry={() => void load()}
        skeletonRows={3}
      >
        <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-700">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500 dark:bg-slate-800">
              <tr>
                <th className="px-3 py-2">Usuario</th>
                <th className="px-3 py-2">Compañía</th>
                <th className="px-3 py-2">Scopes</th>
                <th className="px-3 py-2">Estado</th>
                <th className="px-3 py-2">Último ingreso</th>
                <th className="px-3 py-2">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {clients.map((c) => (
                <tr key={c.id} className="border-t border-slate-100 text-[#162744] dark:border-slate-700 dark:text-slate-200">
                  <td className="px-3 py-2 font-medium">{c.username}</td>
                  <td className="px-3 py-2">{c.tenantName}</td>
                  <td className="px-3 py-2 max-w-[240px] truncate font-mono text-xs" title={parseScopes(c.scopes)}>
                    {parseScopes(c.scopes)}
                  </td>
                  <td className="px-3 py-2">
                    <span
                      className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold ${
                        c.isActive ? "bg-[#557EFF]/10 text-[#557EFF]" : "bg-[#FF4E00]/10 text-[#FF4E00]"
                      }`}
                    >
                      {c.isActive ? "Activo" : "Inactivo"}
                    </span>
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap">
                    {c.lastLoginAt ? new Date(c.lastLoginAt).toLocaleString() : "—"}
                  </td>
                  <td className="px-3 py-2">
                    <div className="flex items-center gap-1">
                      <button
                        type="button"
                        disabled={busyId === c.id}
                        onClick={() => void toggleActive(c)}
                        className="rounded-lg border border-slate-300 px-2 py-1 text-xs hover:bg-slate-50 disabled:opacity-40 dark:hover:bg-slate-800"
                      >
                        {c.isActive ? "Desactivar" : "Activar"}
                      </button>
                      <button
                        type="button"
                        disabled={busyId === c.id}
                        onClick={() => void handleResetSecret(c)}
                        className="inline-flex items-center gap-1 rounded-lg border border-slate-300 px-2 py-1 text-xs text-[#557EFF] hover:bg-[#557EFF]/10 disabled:opacity-40"
                      >
                        <RotateCcw className="h-3.5 w-3.5" /> Regenerar secreto
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>
    </div>
  );
}
