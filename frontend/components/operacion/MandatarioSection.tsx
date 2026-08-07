'use client';

import { useCallback, useEffect, useState } from 'react';
import { UserCheck } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { formatFecha } from '@/lib/format/date';
import type { MandateSignerSelection } from '@/lib/api/types/procedure-runtime';

/**
 * Quién firma el mandato se elige al registrar el trámite (paso FUR / documentos del expediente),
 * no como pregunta principal en la aprobación del OT.
 *
 * La elección es opcional: si hay varios mandatarios se muestran para que el gestor escoja;
 * si no elige, al preparar/aprobar aplica la resolución automática de respaldo.
 */
export function MandatarioSection({
  instanceId,
  tenantId,
  onChanged,
}: {
  instanceId: string;
  tenantId?: string;
  onChanged?: () => void;
}) {
  const [seleccion, setSeleccion] = useState<MandateSignerSelection | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const data = await tramitesClient.listMandateSigners(instanceId, tenantId);
      setSeleccion(data);
    } catch {
      setError('No se pudieron cargar los mandatarios.');
    }
  }, [instanceId, tenantId]);

  useEffect(() => {
    // Carga async al montar, como el resto de las secciones del paso.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const elegir = async (mandateSignerId: string) => {
    setSaving(true);
    setError(null);
    try {
      await tramitesClient.setMandateSigner(instanceId, mandateSignerId, tenantId);
      await load();
      onChanged?.();
    } catch {
      setError('No se pudo guardar el mandatario. Inténtalo de nuevo.');
    } finally {
      setSaving(false);
    }
  };

  // Sin mandatarios (o tipo institucional/abierto): no pintar sección vacía.
  if (!seleccion || seleccion.opciones.length === 0) {
    return null;
  }

  const { opciones, elegidoId, editable } = seleccion;

  return (
    <section className="space-y-3" aria-label="Mandatario que firma" data-testid="mandatario-section">
      <div>
        <h4 className="text-sm font-bold">Mandatario que firma</h4>
        <p className="text-xs opacity-70">
          {editable
            ? 'Opcional. Quien firmará el contrato de mandato en el expediente (junto al FUR). Si no eliges, el sistema puede resolverlo después.'
            : 'El trámite ya salió de borrador: el mandatario no puede cambiarse.'}
        </p>
      </div>

      <div className="space-y-2">
        {opciones.map((o) => {
          const seleccionado = o.id === elegidoId;
          return (
            <label
              key={o.id}
              className="flex items-start gap-3 rounded-xl border p-3"
              style={seleccionado ? { borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)' } : undefined}
            >
              <input
                type="radio"
                name="mandatario"
                className="mt-0.5"
                checked={seleccionado}
                disabled={!editable || saving}
                onChange={() => void elegir(o.id)}
              />
              <span className="min-w-0">
                <span className="flex items-center gap-1.5 text-xs font-semibold">
                  <UserCheck className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  {o.nombre}
                </span>
                <span className="block text-[11px] opacity-70">
                  {o.tipoDocumento} {o.documento}
                </span>
                {/* Tres vías ALTERNATIVAS, no requisitos acumulativos. Quien firma a mano no necesita
                    ninguna de las otras dos: el documento le deja la línea. Antes solo se nombraba la
                    identidad, así que quien podía firmar por otra vía se anunciaba como incompleto. */}
                {o.firmaFisica ? (
                  <span className="block text-[11px]" style={{ color: '#5B8A1F' }}>
                    Firma de forma física
                  </span>
                ) : o.firmaBaulVigente ? (
                  <span className="block text-[11px]" style={{ color: '#5B8A1F' }}>
                    Firma del baúl vigente
                  </span>
                ) : o.identidadVigente ? (
                  <span className="block text-[11px]" style={{ color: '#5B8A1F' }}>
                    Identidad vigente
                    {o.identidadHasta ? ` hasta el ${formatFecha(o.identidadHasta)}` : ''}
                  </span>
                ) : (
                  <span className="block text-[11px]" style={{ color: '#F9AC00' }}>
                    Sin firma del baúl ni identidad vigentes. Puedes dejar el trámite marcado para
                    firmar más adelante.
                  </span>
                )}
              </span>
            </label>
          );
        })}
      </div>

      {error && (
        <p className="text-[11px] leading-tight" style={{ color: '#E5484D' }} role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
