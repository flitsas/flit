'use client';

import { useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';
import {
  ENTRY_MODES,
  type EntryMode,
  type GateProfile,
} from '@/lib/api/types/procedure-parametrization-f08';

interface ConformationProfileStepProps {
  procedureTypeId: string;
  initialProfile?: GateProfile;
  onSaved?: (profile: GateProfile) => void;
}

const ENTRY_MODE_LABELS: Record<EntryMode, { label: string; description: string }> = {
  VIN: { label: 'VIN', description: 'Vehículo nuevo, aún sin placa' },
  PLATE: { label: 'Placa', description: 'Vehículo ya matriculado' },
  BOTH: { label: 'Ambas', description: 'El operador elige placa o VIN' },
};

type ValidationFlagKey =
  | 'validateCompanyRule'
  | 'validateOtOperability'
  | 'validateDuplicateProcedure';

const VALIDATION_FLAGS: { key: ValidationFlagKey; label: string; description: string }[] = [
  {
    key: 'validateCompanyRule',
    label: 'Validar regla de compañía',
    description: 'El OT debe estar habilitado para la compañía del operador.',
  },
  {
    key: 'validateOtOperability',
    label: 'Validar operabilidad del OT',
    description: 'El OT destino debe estar operativo para este tipo.',
  },
  {
    key: 'validateDuplicateProcedure',
    label: 'Validar duplicidad',
    description: 'Bloquea si ya existe un trámite activo del mismo tipo para la placa/VIN.',
  },
];

/**
 * FEATURE-08 / HU-FE-01 (CFD-02, CFD-03) — paso "Entrada y validaciones" del wizard de
 * parametrización SuperAdmin. Configura <code>entryMode</code> (PLATE/VIN/BOTH) y las banderas de
 * validación inicial del <code>gate_profile</code>, y persiste vía PUT /conformation-profile.
 */
export function ConformationProfileStep({
  procedureTypeId,
  initialProfile,
  onSaved,
}: ConformationProfileStepProps) {
  const [entryMode, setEntryMode] = useState<EntryMode>(initialProfile?.entryMode ?? 'VIN');
  const [flags, setFlags] = useState<Record<ValidationFlagKey, boolean>>({
    validateCompanyRule: Boolean(initialProfile?.validateCompanyRule),
    validateOtOperability: Boolean(initialProfile?.validateOtOperability),
    validateDuplicateProcedure: Boolean(initialProfile?.validateDuplicateProcedure),
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggleFlag = (key: ValidationFlagKey) =>
    setFlags((prev) => ({ ...prev, [key]: !prev[key] }));

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      const gateProfile: GateProfile = { entryMode, ...flags };
      const result = await superadminClient.updateConformationProfile(procedureTypeId, {
        gateProfile,
      });
      onSaved?.(result.gateProfile);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar el perfil.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-bold mb-1">Entrada y validaciones</h2>
        <p className="text-xs opacity-60">
          Define cómo entra el vehículo al trámite y qué validaciones aplican al crearlo.
        </p>
      </div>

      <fieldset className="space-y-2">
        <legend className="text-xs font-semibold mb-1">Modo de entrada</legend>
        {ENTRY_MODES.map((mode) => {
          const meta = ENTRY_MODE_LABELS[mode];
          const selected = entryMode === mode;
          return (
            <label
              key={mode}
              htmlFor={`entry-mode-${mode}`}
              className="flex items-center gap-4 rounded-xl p-4 border cursor-pointer transition"
              style={{
                borderColor: selected ? '#557EFF' : '#DFE5ED',
                background: selected ? 'rgba(85,126,255,0.06)' : 'transparent',
              }}
            >
              <input
                id={`entry-mode-${mode}`}
                type="radio"
                name="entryMode"
                value={mode}
                checked={selected}
                onChange={() => setEntryMode(mode)}
                className="h-4 w-4"
                aria-label={`Modo de entrada ${meta.label}`}
              />
              <div className="flex-1 min-w-0">
                <p className="text-xs font-semibold">{meta.label}</p>
                <p className="text-[10px] opacity-60">{meta.description}</p>
              </div>
            </label>
          );
        })}
      </fieldset>

      <fieldset className="space-y-2">
        <legend className="text-xs font-semibold mb-1">Validaciones iniciales</legend>
        {VALIDATION_FLAGS.map((flag) => {
          const checked = flags[flag.key];
          return (
            <label
              key={flag.key}
              htmlFor={`flag-${flag.key}`}
              className="flex items-center gap-4 rounded-xl p-4 border cursor-pointer transition"
              style={{
                borderColor: checked ? '#557EFF' : '#DFE5ED',
                background: checked ? 'rgba(85,126,255,0.06)' : 'transparent',
              }}
            >
              <input
                id={`flag-${flag.key}`}
                type="checkbox"
                checked={checked}
                onChange={() => toggleFlag(flag.key)}
                className="h-4 w-4 rounded"
                aria-label={flag.label}
              />
              <div className="flex-1 min-w-0">
                <p className="text-xs font-semibold">{flag.label}</p>
                <p className="text-[10px] opacity-60">{flag.description}</p>
              </div>
            </label>
          );
        })}
      </fieldset>

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
