'use client';

import type { EntryMode } from '@/lib/api/types/procedure-parametrization-f08';

interface VehicleQuerySectionProps {
  /** Modo de entrada resuelto del sectionConfig del WizardStepDto (CFD-02). */
  entryMode: EntryMode;
  vin?: string;
  plate?: string;
  onVinChange?: (value: string) => void;
  onPlateChange?: (value: string) => void;
  onConsult?: () => void;
  consulting?: boolean;
}

/**
 * FEATURE-08 / HU-FE-01 (CFD-02) — sección de consulta del vehículo del wizard dinámico, adaptada
 * al <code>entryMode</code> recibido del backend: VIN (vehículo nuevo), PLATE (ya matriculado) o BOTH
 * (ambos campos). Presentacional: captura la clave de entrada y dispara la consulta. La lógica de
 * consulta completa (RUNT/preflight) se integra al registry en HU-FE-05.
 */
export function VehicleQuerySection({
  entryMode,
  vin = '',
  plate = '',
  onVinChange,
  onPlateChange,
  onConsult,
  consulting = false,
}: VehicleQuerySectionProps) {
  const showVin = entryMode === 'VIN' || entryMode === 'BOTH';
  const showPlate = entryMode === 'PLATE' || entryMode === 'BOTH';

  return (
    <section aria-label="Consulta del vehículo" className="space-y-4">
      <div>
        <h2 className="text-base font-bold mb-1">Consulta del vehículo</h2>
        <p className="text-xs opacity-60">
          {entryMode === 'BOTH'
            ? 'Ingresa la placa o el VIN del vehículo.'
            : showVin
              ? 'Ingresa el VIN del vehículo nuevo.'
              : 'Ingresa la placa del vehículo matriculado.'}
        </p>
      </div>

      {showPlate && (
        <div className="space-y-1">
          <label htmlFor="vehicle-plate" className="text-xs font-semibold">
            Placa
          </label>
          <input
            id="vehicle-plate"
            type="text"
            value={plate}
            onChange={(e) => onPlateChange?.(e.target.value.toUpperCase())}
            placeholder="ABC123"
            aria-label="Placa del vehículo"
            className="w-full px-3 py-2 rounded-xl border outline-none focus:border-[#557EFF]"
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>
      )}

      {showVin && (
        <div className="space-y-1">
          <label htmlFor="vehicle-vin" className="text-xs font-semibold">
            VIN
          </label>
          <input
            id="vehicle-vin"
            type="text"
            value={vin}
            onChange={(e) => onVinChange?.(e.target.value.toUpperCase())}
            placeholder="8XXXXXXXXXXXXXXXX"
            aria-label="VIN del vehículo"
            className="w-full px-3 py-2 rounded-xl border outline-none focus:border-[#557EFF]"
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>
      )}

      <button
        type="button"
        onClick={() => onConsult?.()}
        disabled={consulting}
        className="rounded-xl px-4 py-2 text-sm font-bold text-white transition disabled:opacity-60"
        style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
      >
        {consulting ? 'Consultando…' : 'Consultar'}
      </button>
    </section>
  );
}
