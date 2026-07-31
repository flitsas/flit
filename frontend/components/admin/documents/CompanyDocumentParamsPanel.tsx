"use client";

// HU #10523 (RF31) — Consola de parámetros documentales por compañía gestora.
// Permite fijar el estado (OCULTO/OBLIGATORIO/OPCIONAL) de un tipo de documento por gestora,
// consumiendo el endpoint admin de HU #10521. Sin parámetros, el checklist queda en su estado base.
import { useCallback, useEffect, useState } from "react";
import {
  fetchCompanyDocumentParams,
  upsertCompanyDocumentParam,
  type CompanyDocumentParam,
  type CompanyDocumentParamState,
} from "@/lib/api/admin-company-document-params";

const STATES: CompanyDocumentParamState[] = ["OBLIGATORIO", "OPCIONAL", "OCULTO"];

export interface CompanyDocumentParamsPanelProps {
  tenantId: string;
}

export function CompanyDocumentParamsPanel({ tenantId }: CompanyDocumentParamsPanelProps) {
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
        const data = await fetchCompanyDocumentParams(tenantId, signal);
        setItems(data);
      } catch {
        setError("No se pudieron cargar los parámetros documentales.");
      } finally {
        setLoading(false);
      }
    },
    [tenantId],
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
        const saved = await upsertCompanyDocumentParam(tenantId, { documentTypeCode, state });
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
    [tenantId],
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
        <p>Cargando…</p>
      ) : items.length === 0 ? (
        <p className="text-sm text-gray-500">Sin parámetros: se aplica el comportamiento base.</p>
      ) : (
        <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead>
            <tr>
              <th scope="col" className="py-1">Documento</th>
              <th scope="col" className="py-1">Estado</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td className="py-1 font-mono">{item.documentTypeCode}</td>
                <td className="py-1">
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
