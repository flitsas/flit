"use client";

// Submódulo "Clientes ICT" (ronda 2, Feature #10888): CRUD de las credenciales de integración
// (ict.integration_clients) que usan los gestores para registrar pre-trámites. SuperAdmin administra
// cualquier compañía (selector); un admin no-super, solo la suya. El SECRETO lo genera el sistema y se
// muestra UNA sola vez (no se puede recuperar) — se regenera con "Regenerar secreto".
import { useCallback, useEffect, useState } from "react";
import { Check, Copy, KeyRound, Power, PowerOff, RotateCcw, X } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ApiError } from "@/lib/api/types";
import {
  createIctClient,
  fetchIctClients,
  resetIctClientSecret,
  updateIctClient,
  type IctClient,
} from "@/lib/api/ict-clients";
import { fetchAllCompanies } from "@/lib/api/admin-companies";
import type { CompanyListItem } from "@/lib/api/types";
import { SearchableSelect } from "@/components/atom/SearchableSelect";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";

import { formatFechaHora } from "@/lib/format/date";
interface Props {

  isSuperAdmin: boolean;
  tenantId: string | null;
}

const CELDA_BORDE = { borderColor: "#DFE5ED" };

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
  const pg = usePaginacion();
  const [clients, setClients] = useState<IctClient[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [username, setUsername] = useState("");
  const [companyId, setCompanyId] = useState("");
  const [testMode, setTestMode] = useState(false);
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
    fetchAllCompanies({}, controller.signal)
      .then((data) => setCompanies(data.filter((c) => c.estadoActivo)))
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
      const res = await createIctClient({ username: username.trim(), tenantId: effectiveTenantId, testMode });
      setClients((prev) => [res.client, ...prev]);
      setRevealed({ username: res.client.username, secret: res.generatedSecret });
      setUsername("");
      setCompanyId("");
      setTestMode(false);
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
          Nuevo cliente
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
          <label
            className="flex items-center gap-2 text-xs text-slate-500"
            title="La compañía no consultará fuentes externas de pago (SOAT/RNMC/RUNT); todas sus filas caen en Con Novedades. Para pruebas de carga."
          >
            <input
              type="checkbox"
              checked={testMode}
              onChange={(e) => setTestMode(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300"
            />
            Modo prueba (no consulta fuentes de pago)
          </label>
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

      {/* Bug #13055 — tabla homologada con el modelo de trámites: cabecera y filas de table-styles,
          RowActions, loader del carrito y paginación en cliente con «Filas por página». */}
      {status === "loading" ? (
        <CarLoaderModal label="Cargando clientes ICT…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="No hay clientes ICT registrados."
          errorMessage="No se pudieron cargar los clientes ICT."
          onRetry={() => void load()}
        >
          <div className="overflow-x-auto">
            <table
              aria-label="Clientes ICT"
              className="text-xs"
              style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
            >
              <thead>
                <tr>
                  {["Usuario", "Compañía", "Scopes", "Estado", "Último ingreso", "Acciones"].map((col, i, arr) => (
                    <th
                      key={col}
                      scope="col"
                      className={`${TABLA_HEADER_CELL_CLS} ${i === 0 ? "rounded-l-xl" : ""} ${i === arr.length - 1 ? "rounded-r-xl text-right" : ""}`}
                      style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                    >
                      {col}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {pg.paginar(clients).map((c) => (
                  <tr key={c.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                    <td className="rounded-l-xl border-y border-l px-4 py-3 font-medium" style={CELDA_BORDE}>{c.username}</td>
                    <td className="border-y px-4 py-3" style={CELDA_BORDE}>{c.tenantName}</td>
                    <td
                      className="max-w-[240px] truncate border-y px-4 py-3 font-mono text-xs"
                      style={CELDA_BORDE}
                      title={parseScopes(c.scopes)}
                    >
                      {parseScopes(c.scopes)}
                    </td>
                    <td className="border-y px-4 py-3" style={CELDA_BORDE}>
                      <StatusBadge label={c.isActive ? "Activo" : "Inactivo"} tone={c.isActive ? "success" : "danger"} />
                      {c.testMode && (
                        <span
                          className="ml-1 inline-flex align-middle"
                          title="No consulta fuentes de pago; todas sus filas caen en Con Novedades."
                        >
                          <StatusBadge label="Prueba" tone="warning" />
                        </span>
                      )}
                    </td>
                    <td className="whitespace-nowrap border-y px-4 py-3" style={CELDA_BORDE}>
                      {c.lastLoginAt ? formatFechaHora(new Date(c.lastLoginAt)) : "—"}
                    </td>
                    <td className="rounded-r-xl border-y border-r px-4 py-3" style={CELDA_BORDE}>
                      <RowActions
                        actions={[
                          {
                            icon: c.isActive ? PowerOff : Power,
                            label: `${c.isActive ? "Desactivar" : "Activar"} cliente ${c.username}`,
                            tone: c.isActive ? "danger" : "primary",
                            disabled: busyId === c.id,
                            onClick: () => void toggleActive(c),
                          },
                          {
                            icon: RotateCcw,
                            label: `Regenerar secreto de ${c.username}`,
                            tone: "primary",
                            disabled: busyId === c.id,
                            onClick: () => void handleResetSecret(c),
                          },
                        ]}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={pg.page}
            pageSize={pg.pageSize}
            totalCount={clients.length}
            onPageChange={pg.setPage}
            onPageSizeChange={pg.setPageSize}
            noun="clientes ICT"
            ariaLabel="Paginación de clientes ICT"
          />
        </UiStateBoundary>
      )}
    </div>
  );
}
