'use client';

import { useCallback, useEffect, useState } from 'react';
import { Clock } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { formatFecha } from '@/lib/format/date';
import type { FirmaPosteriorEstado } from '@/lib/api/types/procedure-runtime';

/** Partes que pueden ser persona jurídica y por tanto tener representante legal. */
const PARTES = [
  { rol: 'vendedor', label: 'Vendedor' },
  { rol: 'comprador', label: 'Comprador' },
] as const;

const AVISO_METODO =
  'Se firmará con la validación de identidad del representante legal. La firma se aplicará sola ' +
  'cuando él la complete, sin que tengas que volver a este trámite.';

/**
 * HU #11197 — opción de firmar a posteriori.
 *
 * Cuando el representante legal de una empresa tiene la identidad y la firma del baúl vencidas, el
 * trámite no puede firmarse hoy y el gestor se quedaba sin salida. Aquí puede dejarlo marcado: cuando
 * esa persona valide su identidad, el trámite —y todos los demás que la esperen— se firman solos
 * (HU #11196).
 *
 * La opción NO se ofrece si el representante tiene firma o identidad vigente (AC2): diferir un trámite
 * que puede cerrarse hoy solo lo retrasaría.
 */
export function FirmaPosteriorSection({
  instanceId,
  tenantId,
  readOnly = false,
  onChanged,
}: {
  instanceId: string;
  tenantId?: string;
  readOnly?: boolean;
  onChanged?: () => void;
}) {
  const [estados, setEstados] = useState<Record<string, FirmaPosteriorEstado>>({});
  const [saving, setSaving] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    const pares = await Promise.all(
      PARTES.map(async ({ rol }): Promise<[string, FirmaPosteriorEstado | null]> => {
        try {
          return [rol, await tramitesClient.getFirmaPosterior(instanceId, rol, tenantId)];
        } catch {
          // Una parte que aún no existe no es un error de pantalla: simplemente no se ofrece.
          return [rol, null];
        }
      }),
    );
    const resueltos: Record<string, FirmaPosteriorEstado> = {};
    for (const [rol, valor] of pares) {
      if (valor) resueltos[rol] = valor;
    }
    setEstados(resueltos);
  }, [instanceId, tenantId]);

  useEffect(() => {
    // Carga async al montar, como el resto de las secciones del paso.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const marcar = async (rol: string) => {
    setSaving(rol);
    setError(null);
    try {
      await tramitesClient.marcarFirmaPosterior(instanceId, rol, tenantId);
      await load();
      onChanged?.();
    } catch {
      setError('No se pudo marcar el trámite. Inténtalo de nuevo.');
    } finally {
      setSaving(null);
    }
  };

  // Solo se pintan las partes donde la opción aplica o donde ya se marcó (AC1/AC2/AC4). Si ninguna,
  // la sección no existe: una sección vacía solo añade ruido al paso.
  const visibles = PARTES.filter(({ rol }) => estados[rol]?.aplica || estados[rol]?.marcado);
  if (visibles.length === 0) return null;

  return (
    <section className="space-y-3" data-testid="firma-posterior">
      <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">Firma a posteriori</h3>

      {error && (
        <p role="alert" className="text-[11px] font-medium" style={{ color: '#FF4E00' }}>
          {error}
        </p>
      )}

      {visibles.map(({ rol, label }) => {
        const estado = estados[rol]!;
        return (
          <div key={rol} className="rounded-xl border px-3 py-3" style={{ borderColor: '#DFE5ED' }}>
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="text-[12px] font-semibold" style={{ color: '#162744' }}>
                  {label}
                  {estado.representanteNombre ? ` · ${estado.representanteNombre}` : ''}
                </p>
                <p className="mt-1 text-[11px] opacity-70">
                  {estado.marcado
                    ? // AC4 — al volver al trámite se ve que está esperando.
                      `Pendiente de firma. ${AVISO_METODO}`
                    : // AC3 — se informa el método y el efecto ANTES de confirmar.
                      `El representante no tiene firma ni identidad vigentes. ${AVISO_METODO}`}
                </p>
                {estado.marcado && estado.marcadoAt && (
                  <p className="mt-1 text-[11px] opacity-60">
                    Marcado el {formatFecha(estado.marcadoAt)}
                  </p>
                )}
              </div>

              {estado.marcado ? (
                <span
                  className="flex shrink-0 items-center gap-1 rounded-full border px-2.5 py-1 text-[11px] font-semibold"
                  style={{ color: '#557EFF', borderColor: '#557EFF' }}
                >
                  <Clock className="h-3.5 w-3.5" aria-hidden="true" /> Pendiente de firma
                </span>
              ) : (
                <button
                  type="button"
                  onClick={() => void marcar(rol)}
                  disabled={readOnly || saving === rol}
                  className="shrink-0 rounded-lg border px-2.5 py-1.5 text-[11px] font-semibold disabled:opacity-60"
                  style={{ color: '#557EFF', borderColor: '#557EFF' }}
                >
                  {saving === rol ? 'Marcando…' : 'Firmar más adelante'}
                </button>
              )}
            </div>
          </div>
        );
      })}
    </section>
  );
}
