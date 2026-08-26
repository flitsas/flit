'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { PrendaForm, PRENDA_DECISION_LABELS } from './PrendaForm';
import type { PrendaData, PrendaDecision } from '@/lib/api/types/procedure-runtime';
import { WIZARD_CARD } from './wizard-field-styles';

/** En la modificación post-registro se ofrecen todas las decisiones de gestión. */
const ALL_DECISIONS: PrendaDecision[] = [
  'solicitar',
  'registrar',
  'levantar',
  'omitir',
  'sin_prenda',
];

/**
 * R17 (HU #10600) — modificación de la elección de prenda desde el detalle del trámite (post-registro).
 * Solo se muestra si el trámite ya tiene una prenda vigente. El formulario es EDITABLE (no está bajo el
 * contexto de solo-lectura del wizard): el backend versiona la decisión (nueva vigente reemplaza a la
 * anterior) y bloquea la edición en estados finales (aprobado/anulado) con 409.
 */
export function PrendaModificar({ instanceId }: { instanceId: string }) {
  const [prenda, setPrenda] = useState<PrendaData | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [open, setOpen] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    const load = async () => {
      try {
        const p = await tramitesClient.getPrenda(instanceId);
        if (active) setPrenda(p);
      } catch {
        /* sin prenda vigente: el panel no se muestra */
      } finally {
        if (active) setLoaded(true);
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId, reloadKey]);

  // Solo se ofrece la modificación si el trámite ya tiene una prenda vigente.
  if (!loaded || !prenda) return null;

  return (
    <section
      className={WIZARD_CARD}
      aria-label="Prenda del trámite"
    >
      <div className="flex items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold">Prenda / gravamen</h3>
          <p className="text-xs opacity-60">
            Vigente: {PRENDA_DECISION_LABELS[prenda.decision]}
            {prenda.acreedorNombre ? ` · ${prenda.acreedorNombre}` : ''}
          </p>
        </div>
        <button
          type="button"
          onClick={() => setOpen((o) => !o)}
          className="px-4 py-2 rounded-xl text-xs font-semibold border"
        >
          {open ? 'Cerrar' : 'Modificar elección de prenda'}
        </button>
      </div>
      {open && (
        <div className="mt-3">
          <PrendaForm
            instanceId={instanceId}
            decisions={ALL_DECISIONS}
            hideHeader
            onSaved={() => {
              setOpen(false);
              setReloadKey((k) => k + 1);
            }}
          />
        </div>
      )}
    </section>
  );
}
