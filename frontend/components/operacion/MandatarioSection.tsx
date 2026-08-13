'use client';

import { useCallback, useEffect, useState } from 'react';
import { UserCheck } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { WizardAccordion } from './WizardAccordion';
import { formatFecha } from '@/lib/format/date';
import type { MandateSignerSelection } from '@/lib/api/types/procedure-runtime';

const BORDER = '#DFE5ED';
const BLUE = '#557EFF';

/**
 * Quién firma el mandato — paso resumen del trámite (FUR), en disclosure como Vehículo/Comprador.
 *
 * Opcional: si hay varios mandatarios se muestran para elegir; si no elige, el sistema puede
 * resolverlo después. Institucional/abierto o sin candidatos ⇒ no se pinta.
 */
export function MandatarioSection({
  instanceId,
  tenantId,
  onChanged,
  /** Desplegable al estilo MatriculaResumen (default). */
  asDisclosure = true,
  defaultOpen = true,
}: {
  instanceId: string;
  tenantId?: string;
  onChanged?: () => void;
  asDisclosure?: boolean;
  defaultOpen?: boolean;
}) {
  const [seleccion, setSeleccion] = useState<MandateSignerSelection | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(defaultOpen);

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

  const body = (
    <div className="space-y-3" data-testid="mandatario-section-body">
      <p className="text-xs opacity-70">
        {editable
          ? 'Opcional. Quien firmará el contrato de mandato en el expediente. Si no eliges, el sistema puede resolverlo después.'
          : 'El trámite ya salió de borrador: el mandatario no puede cambiarse.'}
      </p>

      <div className="space-y-2">
        {opciones.map((o) => {
          const seleccionado = o.id === elegidoId;
          return (
            <label
              key={o.id}
              className="flex items-start gap-3 rounded-xl border p-3"
              style={
                seleccionado
                  ? { borderColor: BLUE, background: 'rgba(85,126,255,0.06)' }
                  : { borderColor: BORDER }
              }
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
                <span className="block text-xs opacity-70">
                  {o.tipoDocumento} {o.documento}
                </span>
                {o.firmaFisica ? (
                  <span className="block text-xs" style={{ color: '#5B8A1F' }}>
                    Firma de forma física
                  </span>
                ) : o.firmaBaulVigente ? (
                  <span className="block text-xs" style={{ color: '#5B8A1F' }}>
                    Firma del baúl vigente
                  </span>
                ) : o.identidadVigente ? (
                  <span className="block text-xs" style={{ color: '#5B8A1F' }}>
                    Identidad vigente
                    {o.identidadHasta ? ` hasta el ${formatFecha(o.identidadHasta)}` : ''}
                  </span>
                ) : (
                  <span className="block text-xs" style={{ color: 'var(--badge-warning-fg)' }}>
                    Sin firma del baúl ni identidad vigentes. Puedes dejar el trámite marcado para
                    firmar más adelante.
                  </span>
                )}
              </span>
            </label>
          );
        })}
      </div>

      {error ? (
        <p className="text-xs leading-tight" style={{ color: '#E5484D' }} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );

  if (!asDisclosure) {
    return (
      <section className="space-y-3" aria-label="Mandatario que firma" data-testid="mandatario-section">
        <h4 className="text-sm font-bold">Mandatario que firma</h4>
        {body}
      </section>
    );
  }

  return (
    <WizardAccordion
      title="Mandatario"
      regionLabel="Mandatario que firma"
      open={open}
      onOpenChange={setOpen}
      icon={<span className="h-4 w-1 shrink-0 rounded-full" style={{ background: BLUE }} aria-hidden="true" />}
      testId="mandatario-section"
    >
      {body}
    </WizardAccordion>
  );
}
