'use client';

import { useCallback, useEffect, useState } from 'react';
import { ChevronRight, RefreshCw } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { IdentityAuditEvent } from '@/lib/api/types/procedure-runtime';

/**
 * Panel de tracking de identidad compartido (CF-07, Feature #11004, HU #11007). Extraído del
 * `IdentityAuditPanel` original de `BiometricStep` y generalizado para consumirse desde Validaciones,
 * Prevalidaciones y el propio trámite con un único punto de entrada: `validationId`.
 *
 * D2 — visible para cualquier usuario autenticado del tenant con acceso al módulo (NO restringido a
 * SuperAdmin): el backend ya sanea la bitácora (sin secretos ni PII cruda), así que este componente no
 * evalúa ningún gate de rol.
 *
 * Disclosure colapsable con carga perezosa (o auto-abierto) que consulta
 * `GET /biometric-validations/{validationId}/audit` — el endpoint SIN instancia (CF-07).
 * `refreshKey` permite al drawer de prevalidación re-sincronizar la bitácora en cada poll.
 */
export function IdentityValidationTrackingPanel({
  validationId,
  refreshKey = 0,
  defaultOpen = false,
}: {
  validationId: string;
  /** Cuando cambia (p. ej. cada poll del detalle), recarga la bitácora si el panel está abierto. */
  refreshKey?: number;
  /** Abrir el disclosure al montar (detalle en vivo de prevalidación). */
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const [events, setEvents] = useState<IdentityAuditEvent[] | null>(null);
  const [referenced, setReferenced] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadAudit = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await tramitesClient.getBiometricAuditByValidation(validationId);
      setEvents(res.events);
      setReferenced(res.referencedFromOtherProcedure ?? false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo cargar la bitácora.');
    } finally {
      setLoading(false);
    }
  }, [validationId]);

  // Carga inicial si defaultOpen; re-sync cuando el padre hace poll (refreshKey).
  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: setState ocurre tras el await
    void loadAudit();
  }, [open, refreshKey, loadAudit]);

  const toggle = () => {
    setOpen((prev) => !prev);
  };

  return (
    <div className="mt-3 border-t pt-3">
      <button
        type="button"
        onClick={toggle}
        className="flex items-center gap-1.5 text-[11px] font-semibold opacity-70 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        aria-expanded={open}
        aria-label="Ver tracking de la validación"
      >
        <ChevronRight
          className={`h-3 w-3 transition-transform ${open ? 'rotate-90' : ''}`}
          aria-hidden
        />
        Ver tracking
      </button>

      {open && (
        <div className="mt-2 space-y-2">
          {loading && (
            <p className="text-[11px] opacity-60" role="status" aria-live="polite">
              Cargando bitácora…
            </p>
          )}
          {error && (
            <p className="text-[11px]" style={{ color: '#FF4E00' }} role="alert" aria-live="polite">
              {error}
            </p>
          )}
          {/*
           * HU #10350 — identidad reutilizada ("apalancada"): la validación se realizó en otro trámite
           * del mismo cliente. Aviso informativo (no error); debajo se muestra su bitácora real.
           */}
          {referenced && (
            <p
              className="rounded-lg px-2.5 py-1.5 text-[11px]"
              style={{ background: 'rgba(85,126,255,0.10)', color: '#3B5BDB' }}
              role="status"
            >
              Validación reutilizada de otro trámite del mismo cliente (identidad ya verificada). El
              historial técnico corresponde a ese trámite.
            </p>
          )}
          {!loading && !error && events && events.length === 0 && (
            <p className="text-[11px] opacity-60">Sin eventos registrados todavía.</p>
          )}
          {events && events.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-[10.5px]">
                <thead>
                  <tr className="opacity-60">
                    <th className="py-1 pr-2 font-semibold">Fecha</th>
                    <th className="py-1 pr-2 font-semibold">Etapa</th>
                    <th className="py-1 pr-2 font-semibold">Resultado</th>
                    <th className="py-1 pr-2 font-semibold">Cifrado</th>
                    <th className="py-1 pr-2 font-semibold">Detalle</th>
                  </tr>
                </thead>
                <tbody>
                  {events.map((e, i) => (
                    <tr key={i} className="border-t align-top" style={{ borderColor: '#EEF1F6' }}>
                      <td className="whitespace-nowrap py-1 pr-2 opacity-70">{formatFecha(e.occurredAt)}</td>
                      <td className="py-1 pr-2 font-medium">{e.stage}</td>
                      <td className="py-1 pr-2">{auditOutcomeLabel(e)}</td>
                      <td className="py-1 pr-2">
                        {e.decryptOk == null ? '—' : e.decryptOk ? 'OK' : 'Falló'}
                      </td>
                      <td className="py-1 pr-2 opacity-70">
                        {e.errorType ?? e.providerStatus ?? e.message ?? ''}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <button
            type="button"
            onClick={() => void loadAudit()}
            disabled={loading}
            className="flex items-center gap-1 text-[10.5px] font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ color: '#557EFF' }}
            aria-label="Refrescar bitácora"
          >
            <RefreshCw className={`h-2.5 w-2.5 ${loading ? 'animate-spin' : ''}`} aria-hidden />
            Refrescar
          </button>
        </div>
      )}
    </div>
  );
}

/** Formatea una fecha ISO a texto legible (es-CO). Devuelve el ISO crudo si no parsea. */
function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

/** Texto del resultado de un evento de bitácora, anexando el código HTTP si lo hay. */
function auditOutcomeLabel(e: IdentityAuditEvent): string {
  return e.httpStatus != null ? `${e.outcome} (HTTP ${e.httpStatus})` : e.outcome;
}
