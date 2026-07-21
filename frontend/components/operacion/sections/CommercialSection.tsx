'use client';

interface CommercialSectionProps {
  requiresCommercialValue: boolean;
  commercialValueSource?: string | null;
  value?: number;
  onChange?: (value: number) => void;
}

const SOURCE_LABELS: Record<string, string> = {
  FASECOLDA: 'FASECOLDA',
  BASE_GRAVABLE: 'Base gravable',
  MERCADO_LIBRE: 'Mercado libre',
};

/**
 * FEATURE-08 / HU-FE-03 (CFD-06) — sección de valor comercial del wizard dinámico. Muestra el campo
 * de valor de venta solo cuando el tipo lo exige (<code>requiresCommercialValue</code>) e indica la
 * fuente de referencia (<code>commercialValueSource</code>).
 */
export function CommercialSection({
  requiresCommercialValue,
  commercialValueSource,
  value,
  onChange,
}: CommercialSectionProps) {
  return (
    <section aria-label="Valor comercial" className="space-y-3">
      <h2 className="text-base font-bold mb-1">Valor comercial</h2>

      {requiresCommercialValue ? (
        <div className="space-y-1">
          <label htmlFor="valor-venta" className="text-xs font-semibold">
            Valor de venta
          </label>
          <input
            id="valor-venta"
            type="number"
            min={0}
            value={value ?? ''}
            onChange={(e) => onChange?.(Number(e.target.value))}
            aria-label="Valor de venta"
            className="w-full px-3 py-2 rounded-xl border outline-none focus:border-[#557EFF]"
          />
          {commercialValueSource && (
            <p className="text-[10px] opacity-60">
              Referencia: {SOURCE_LABELS[commercialValueSource] ?? commercialValueSource}
            </p>
          )}
        </div>
      ) : (
        <p className="text-xs opacity-50">Este tipo no requiere valor comercial.</p>
      )}
    </section>
  );
}
