"use client";

// HU #10523 (RF31) — Consola de parámetros documentales por compañía gestora.
// Permite fijar el estado (OCULTO/OBLIGATORIO/OPCIONAL) de un tipo de documento por gestora,
// consumiendo el endpoint admin de HU #10521. Sin parámetros, el checklist queda en su estado base.
import { useCallback, useEffect, useState } from "react";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import {
  fetchCompanyDocumentParams,
  upsertCompanyDocumentParam,
  type CompanyDocumentParam,
  type CompanyDocumentParamState,
} from "@/lib/api/admin-company-document-params";

const STATES: CompanyDocumentParamState[] = ["OBLIGATORIO", "OPCIONAL", "OCULTO"];

export interface CompanyDocumentParamsPanelProps {
  tenantId: string;
  networkHeadId?: string | null;
}

export function CompanyDocumentParamsPanel({
  tenantId,
  networkHeadId,
}: CompanyDocumentParamsPanelProps) {
  const [items, setItems] = useState<CompanyDocumentParam[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [newCode, setNewCode] = useState("");
  const [newState, setNewState] = useState<CompanyDocumentParamState>("OBLIGATORIO");
  const [saving, setSaving] = useState(false);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);
      try {
        const data = await fetchCompanyDocumentParams(tenantId, signal, networkHeadId);
        setItems(data);
      } catch {
        setError("No se pudieron cargar los parámetros documentales.");
      } finally {
        setLoading(false);
      }
    },
    [tenantId, networkHeadId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await (no síncronos)
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const save = useCallback(
    async (documentTypeCode: string, state: CompanyDocumentParamState) => {
      setSaving(true);
      setError(null);
      try {
        const saved = await upsertCompanyDocumentParam(tenantId, { documentTypeCode, state }, networkHeadId);
        setItems((prev) => {
          const rest = prev.filter((p) => p.documentTypeCode !== saved.documentTypeCode);
          return [...rest, saved].sort((a, b) => a.documentTypeCode.localeCompare(b.documentTypeCode));
        });
      } catch {
        setError("No se pudo guardar el parámetro documental.");
      } finally {
        setSaving(false);
      }
    },
    [tenantId, networkHeadId],
  );

  const onAdd = useCallback(async () => {
    const code = newCode.trim();
    if (!code) {
      return;
    }
    await save(code, newState);
    setNewCode("");
  }, [newCode, newState, save]);

  return (
    <section aria-label="Parámetros documentales por gestora" className="space-y-4">
      <header>
        <h2 className="text-lg font-semibold">Parámetros documentales por gestora</h2>
        <p className="text-sm text-gray-500">
          Define si cada tipo de documento es obligatorio, opcional u oculto para esta compañía.
        </p>
      </header>

      {error ? (
        <p role="alert" className="text-sm text-red-600">
          {error}
        </p>
      ) : null}

      {loading ? (
        <CarLoaderModal label="Cargando parámetros documentales…" />
      ) : items.length === 0 ? (
        <p className="text-sm text-gray-500">Sin parámetros: se aplica el comportamiento base.</p>
      ) : (
        /* Bug #13055 — tabla homologada con el modelo de trámites (fija: parámetros por gestora, sin paginar) */
        <div className="overflow-x-auto">
          <table
            aria-label="Parámetros documentales"
            className="text-left text-sm"
            style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
          >
            <thead>
              <tr>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>Documento</th>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>Estado</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                  <td className="rounded-l-xl border-y border-l px-4 py-3 font-mono" style={{ borderColor: "#DFE5ED" }}>{item.documentTypeCode}</td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                    <select
                      aria-label={`Estado de ${item.documentTypeCode}`}
                      value={item.state}
                      disabled={saving}
                      onChange={(e) => void save(item.documentTypeCode, e.target.value as CompanyDocumentParamState)}
                    >
                      {STATES.map((s) => (
                        <option key={s} value={s}>
                          {s}
                        </option>
                      ))}
                    </select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="flex items-end gap-2">
        <label className="flex flex-col text-sm">
          Código de documento
          <input
            aria-label="Código de documento"
            value={newCode}
            onChange={(e) => setNewCode(e.target.value)}
            className="border px-2 py-1"
          />
        </label>
        <label className="flex flex-col text-sm">
          Estado
          <select
            aria-label="Estado del nuevo parámetro"
            value={newState}
            onChange={(e) => setNewState(e.target.value as CompanyDocumentParamState)}
          >
            {STATES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </label>
        <button type="button" onClick={() => void onAdd()} disabled={saving || !newCode.trim()}>
          Guardar
        </button>
      </div>
    </section>
  );
}
