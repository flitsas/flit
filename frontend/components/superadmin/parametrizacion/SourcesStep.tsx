'use client';

import { useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';
import type {
  ConformationSourceInput,
} from '@/lib/api/types/procedure-parametrization-f08';

interface SourcesStepProps {
  procedureTypeId: string;
  initialSources?: ConformationSourceInput[];
  onSaved?: (sources: ConformationSourceInput[]) => void;
}

const SOURCE_CATALOG: { code: string; label: string; description: string }[] = [
  { code: 'RUNT', label: 'RUNT', description: 'Registro Único Nacional de Tránsito' },
  { code: 'SIMIT', label: 'SIMIT', description: 'Sistema de multas e infracciones' },
  { code: 'RUES', label: 'RUES', description: 'Registro Único Empresarial (persona jurídica)' },
  { code: 'RNMC', label: 'RNMC', description: 'Registro Nacional de Medidas Correctivas' },
  { code: 'FASECOLDA', label: 'FASECOLDA', description: 'Valor comercial de referencia' },
  { code: 'RESOLUCIONES', label: 'Resoluciones', description: 'Consulta de resoluciones' },
];

type SimitMode = 'INTERNAL' | 'ONLINE';

/**
 * FEATURE-08 / HU-FE-02 (CFD-04) — paso "Fuentes" del wizard de parametrización SuperAdmin.
 * Selecciona qué fuentes externas consulta el tipo, en qué orden, y el modo de SIMIT
 * (INTERNAL/ONLINE). Persiste vía PUT /conformation-profile (sources[]).
 */
export function SourcesStep({ procedureTypeId, initialSources, onSaved }: SourcesStepProps) {
  const initialCodes = new Set((initialSources ?? []).map((s) => s.sourceCode));
  const [selected, setSelected] = useState<Set<string>>(initialCodes);
  const initialSimit = (initialSources ?? []).find((s) => s.sourceCode === 'SIMIT')
    ?.config?.['simitMode'] as SimitMode | undefined;
  const [simitMode, setSimitMode] = useState<SimitMode>(initialSimit ?? 'INTERNAL');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggle = (code: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });

  function buildSources(): ConformationSourceInput[] {
    let order = 0;
    return SOURCE_CATALOG.filter((s) => selected.has(s.code)).map((s) => {
      order += 1;
      return {
        sourceCode: s.code,
        executionOrder: order,
        config: s.code === 'SIMIT' ? { simitMode } : {},
      };
    });
  }

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      const sources = buildSources();
      const result = await superadminClient.updateConformationProfile(procedureTypeId, { sources });
      onSaved?.(
        result.sources.map((s) => ({
          sourceCode: s.sourceCode,
          executionOrder: s.executionOrder,
          config: s.config,
        })),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudieron guardar las fuentes.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-bold mb-1">Fuentes a consultar</h2>
        <p className="text-xs opacity-60">
          Selecciona las fuentes externas que el trámite consulta y su orden.
        </p>
      </div>

      <div className="space-y-2">
        {SOURCE_CATALOG.map((source) => {
          const checked = selected.has(source.code);
          return (
            <div key={source.code}>
              <label
                htmlFor={`source-${source.code}`}
                className="flex items-center gap-4 rounded-xl p-4 border cursor-pointer transition"
                style={{
                  borderColor: checked ? '#557EFF' : '#DFE5ED',
                  background: checked ? 'rgba(85,126,255,0.06)' : 'transparent',
                }}
              >
                <input
                  id={`source-${source.code}`}
                  type="checkbox"
                  checked={checked}
                  onChange={() => toggle(source.code)}
                  className="h-4 w-4 rounded"
                  aria-label={`Fuente ${source.label}`}
                />
                <div className="flex-1 min-w-0">
                  <p className="text-xs font-semibold">{source.label}</p>
                  <p className="text-[10px] opacity-60">{source.description}</p>
                </div>
              </label>

              {source.code === 'SIMIT' && checked && (
                <div className="ml-8 mt-2 flex items-center gap-2">
                  <label htmlFor="simit-mode" className="text-[10px] font-semibold opacity-70">
                    Modo SIMIT
                  </label>
                  <select
                    id="simit-mode"
                    value={simitMode}
                    onChange={(e) => setSimitMode(e.target.value as SimitMode)}
                    aria-label="Modo SIMIT"
                    className="px-2 py-1 rounded-lg border text-xs"
                    style={{ borderColor: '#DFE5ED' }}
                  >
                    <option value="INTERNAL">Interno</option>
                    <option value="ONLINE">En línea</option>
                  </select>
                </div>
              )}
            </div>
          );
        })}
      </div>

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
