'use client';

import { useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';

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
 * Agrega documentos requeridos por el tipo (obligatorio / buzón dummy) y persiste vía
 * PUT /conformation-profile (documentRequirements[]).
 */
export function DocumentRequirementsStep({
  procedureTypeId,
  initialRequirements = [],
  onSaved,
}: DocumentRequirementsStepProps) {
  const [rows, setRows] = useState<DocumentRequirementItem[]>(initialRequirements);
  const [newCode, setNewCode] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const addRow = () => {
    const code = newCode.trim().toUpperCase();
    if (!code || rows.some((r) => r.documentTypeCode === code)) return;
    setRows((prev) => [...prev, { documentTypeCode: code, isRequired: true, isDummy: false }]);
    setNewCode('');
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
        <p className="text-xs opacity-60">Define qué documentos exige el trámite y cuáles son buzón (no bloquean).</p>
      </div>

      <div className="flex items-center gap-2">
        <input
          type="text"
          value={newCode}
          onChange={(e) => setNewCode(e.target.value.toUpperCase())}
          placeholder="CÓDIGO DE DOCUMENTO"
          aria-label="Código de documento"
          className="flex-1 px-3 py-2 rounded-xl border outline-none focus:border-[#557EFF]"
          style={{ borderColor: '#DFE5ED' }}
        />
        <button
          type="button"
          onClick={addRow}
          className="rounded-xl px-4 py-2 text-sm font-bold border"
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
            style={{ borderColor: '#DFE5ED' }}
          >
            <span className="flex-1 text-xs font-semibold">{row.documentTypeCode}</span>
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
