'use client';

interface SignatureFurSectionProps {
  furGenerated?: boolean;
  onGenerate?: () => void;
  generating?: boolean;
}

/**
 * FEATURE-08 / HU-FE-03 (CFD-07) — sección de firma / FUR del wizard dinámico. Se registra bajo
 * <code>section_type='signature_fur'</code>. Muestra el estado del FUR y permite generarlo.
 */
export function SignatureFurSection({
  furGenerated = false,
  onGenerate,
  generating = false,
}: SignatureFurSectionProps) {
  return (
    <section aria-label="Firma y FUR" className="space-y-3">
      <h2 className="text-base font-bold mb-1">Firma / FUR</h2>

      {furGenerated ? (
        <p className="text-xs" style={{ color: '#5a8a1f' }}>
          FUR generado.
        </p>
      ) : (
        <>
          <p className="text-xs opacity-60">Genera el Formulario Único de Registro para radicar.</p>
          <button
            type="button"
            onClick={() => onGenerate?.()}
            disabled={generating}
            aria-label="Generar FUR"
            className="rounded-xl px-4 py-2 text-sm font-bold text-white transition disabled:opacity-60"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            {generating ? 'Generando…' : 'Generar FUR'}
          </button>
        </>
      )}
    </section>
  );
}
