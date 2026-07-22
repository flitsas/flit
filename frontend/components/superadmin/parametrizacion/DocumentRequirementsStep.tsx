'use client';

import { useEffect, useMemo, useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';
import { fetchDocumentTypes } from '@/lib/api/admin-document-types';
import type { DocumentType } from '@/lib/api/types-documents';

export interface DocumentRequirementItem {
  documentTypeCode: string;
  isRequired: boolean;
  isDummy: boolean;
}

interface DocumentRequirementsStepProps {
  procedureTypeId: string;
  initialRequirements?: DocumentRequirementItem[];
  onSaved?: (requirements: DocumentRequirementItem[]) => void;
}

/**
 * FEATURE-08 / HU-FE-03 (CFD-06) — paso "Documentos" del wizard de parametrización SuperAdmin.
 * Los documentos requeridos por el tipo se ELIGEN de una lista: el catálogo de documentos ya creados
 * en el Admin de Documentos (fetchDocumentTypes), no se escriben a mano. Se marca cada uno como
 * obligatorio / buzón (dummy) y se persiste vía PUT /conformation-profile (documentRequirements[]).
 */
export function DocumentRequirementsStep({
  procedureTypeId,
  initialRequirements = [],
  onSaved,
}: DocumentRequirementsStepProps) {
  const [rows, setRows] = useState<DocumentRequirementItem[]>(initialRequirements);
  const [catalog, setCatalog] = useState<DocumentType[]>([]);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [selectedCode, setSelectedCode] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    fetchDocumentTypes({ page: 1, pageSize: 200 })
      .then((res) => {
        if (!active) return;
        setCatalog(res.data.filter((d) => d.estado === 'activo'));
      })
      .catch(() => {
        if (active) setCatalogError('No se pudo cargar el catálogo de documentos.');
      });
    return () => {
      active = false;
    };
  }, []);

  // Nombre por código (para etiquetar filas ya elegidas), tomado del catálogo.
  const nameByCode = useMemo(
    () => new Map(catalog.map((d) => [d.codigo, d.nombre])),
    [catalog],
  );

  // Documentos del catálogo aún no agregados (los que se pueden elegir).
  const options = useMemo(
    () => catalog.filter((d) => !rows.some((r) => r.documentTypeCode === d.codigo)),
    [catalog, rows],
  );

  const addSelected = () => {
    if (!selectedCode || rows.some((r) => r.documentTypeCode === selectedCode)) return;
    setRows((prev) => [...prev, { documentTypeCode: selectedCode, isRequired: true, isDummy: false }]);
    setSelectedCode('');
  };

  const patchRow = (code: string, patch: Partial<DocumentRequirementItem>) =>
    setRows((prev) => prev.map((r) => (r.documentTypeCode === code ? { ...r, ...patch } : r)));

  const removeRow = (code: string) =>
    setRows((prev) => prev.filter((r) => r.documentTypeCode !== code));

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      const result = await superadminClient.updateConformationProfile(procedureTypeId, {
        documentRequirements: rows,
      });
      onSaved?.(
        result.documentRequirements.map((d) => ({
          documentTypeCode: d.documentTypeCode,
          isRequired: d.isRequired,
          isDummy: d.isDummy,
        })),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudieron guardar los documentos.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-bold mb-1">Documentos requeridos</h2>
        <p className="text-xs opacity-60">
          Elige del catálogo de documentos qué exige el trámite y marca cuáles son buzón (no bloquean).
        </p>
      </div>

      {catalogError && (
        <p role="alert" className="text-xs font-medium" style={{ color: '#FF4E00' }}>
          {catalogError}
        </p>
      )}

      <div className="flex items-center gap-2">
        <select
          value={selectedCode}
          onChange={(e) => setSelectedCode(e.target.value)}
          aria-label="Documento del catálogo"
          className="flex-1 px-3 py-2 rounded-xl border outline-none focus:border-[#557EFF] bg-white dark:bg-[#0B0F14]"
        >
          <option value="">
            {catalog.length === 0 ? 'Sin documentos en el catálogo' : 'Selecciona un documento…'}
          </option>
          {options.map((d) => (
            <option key={d.id} value={d.codigo}>
              {d.nombre} ({d.codigo})
            </option>
          ))}
        </select>
        <button
          type="button"
          onClick={addSelected}
          disabled={!selectedCode}
          className="rounded-xl px-4 py-2 text-sm font-bold border disabled:opacity-40"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
        >
          Agregar
        </button>
      </div>

      <ul className="space-y-2">
        {rows.map((row) => (
          <li
            key={row.documentTypeCode}
            data-testid={`doc-${row.documentTypeCode}`}
            className="flex items-center gap-3 rounded-xl p-3 border"
          >
            <span className="flex-1 text-xs font-semibold">
              {nameByCode.get(row.documentTypeCode) ?? row.documentTypeCode}
              <span className="ml-1 font-mono opacity-50">({row.documentTypeCode})</span>
            </span>
            <label className="flex items-center gap-1 text-[11px]">
              <input
                type="checkbox"
                checked={row.isRequired}
                onChange={() => patchRow(row.documentTypeCode, { isRequired: !row.isRequired })}
                aria-label={`${row.documentTypeCode} obligatorio`}
              />
              Obligatorio
            </label>
            <label className="flex items-center gap-1 text-[11px]">
              <input
                type="checkbox"
                checked={row.isDummy}
                onChange={() => patchRow(row.documentTypeCode, { isDummy: !row.isDummy })}
                aria-label={`${row.documentTypeCode} buzón`}
              />
              Buzón
            </label>
            <button
              type="button"
              onClick={() => removeRow(row.documentTypeCode)}
              aria-label={`Quitar ${row.documentTypeCode}`}
              className="text-[11px] font-bold"
              style={{ color: '#FF4E00' }}
            >
              Quitar
            </button>
          </li>
        ))}
      </ul>

      {error && (
        <p role="alert" className="text-xs font-medium" style={{ color: '#FF4E00' }}>
          {error}
        </p>
      )}

      <button
        type="button"
        onClick={handleSave}
        disabled={saving}
        className="w-full rounded-xl py-2.5 text-sm font-bold text-white transition disabled:opacity-60"
        style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
      >
        {saving ? 'Guardando…' : 'Guardar y continuar'}
      </button>
    </div>
  );
}
