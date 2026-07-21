'use client';

export type PlateRequestStatus = 'none' | 'requesting' | 'pending' | 'completed';

interface PlateRequestSectionProps {
  status?: PlateRequestStatus;
  assignedPlate?: string;
  onRequest?: () => void;
}

/**
 * FEATURE-08 / HU-FE-04 (CFD-08) — sección de solicitud de placa del wizard dinámico. Se registra
 * bajo <code>section_type='plate_request'</code> (integra FEATURE-04). Cuatro estados de UI:
 * sin solicitud / solicitando / en trámite / placa asignada.
 */
export function PlateRequestSection({
  status = 'none',
  assignedPlate,
  onRequest,
}: PlateRequestSectionProps) {
  return (
    <section aria-label="Solicitud de placa" className="space-y-3">
      <h2 className="text-base font-bold mb-1">Solicitud de placa</h2>

      {status === 'completed' ? (
        <p className="text-xs" style={{ color: '#5a8a1f' }}>
          Placa asignada{assignedPlate ? `: ${assignedPlate}` : ''}.
        </p>
      ) : status === 'pending' ? (
        <p className="text-xs" style={{ color: '#a86f00' }} role="status">
          Solicitud de placa en trámite.
        </p>
      ) : (
        <>
          <p className="text-xs opacity-60">
            Este trámite requiere solicitar una placa antes de radicar.
          </p>
          <button
            type="button"
            onClick={() => onRequest?.()}
            disabled={status === 'requesting'}
            aria-label="Solicitar placa"
            className="rounded-xl px-4 py-2 text-sm font-bold text-white transition disabled:opacity-60"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            {status === 'requesting' ? 'Solicitando…' : 'Solicitar placa'}
          </button>
        </>
      )}
    </section>
  );
}
