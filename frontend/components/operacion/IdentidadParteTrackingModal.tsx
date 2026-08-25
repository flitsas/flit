'use client';

import { useEffect, useMemo, useState } from 'react';
import { Modal } from '@/components/atom/Modal';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import { SeccionCargando, SeccionError, SeccionVacia } from '@/components/operacion/detalle/primitivos';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { BiometricParte, BiometricValidation } from '@/lib/api/types/procedure-runtime';

const KYVERUM = 'kyverum';

/**
 * Modal de tracking de identidad por UNA parte (click en línea de la columna Firmas).
 * FE-only: `listBiometricExpediente` + `IdentityValidationTrackingPanel`.
 */
export function IdentidadParteTrackingModal({
  open,
  instanceId,
  tenantId,
  parte,
  rotulo,
  onClose,
}: {
  open: boolean;
  instanceId: string | null;
  tenantId?: string | null;
  parte: BiometricParte;
  rotulo: string;
  onClose: () => void;
}) {
  const [validations, setValidations] = useState<BiometricValidation[]>([]);
  const [firmaBaul, setFirmaBaul] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!open || !instanceId) return;
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const res = await tramitesClient.listBiometricExpediente(
          instanceId,
          tenantId ?? undefined,
        );
        if (cancelled) return;
        const matches = (res.validations ?? []).filter((v) =>
          parte === 'comprador'
            ? v.partyRole === null || v.partyRole === 'comprador'
            : v.partyRole === parte,
        );
        setValidations(matches);
        setFirmaBaul((res.firmaBaulPartes ?? []).includes(parte));
      } catch (e: unknown) {
        if (!cancelled) {
          setError(
            e instanceof Error
              ? e.message
              : 'No se pudo cargar el tracking de identidad de esta parte.',
          );
          setValidations([]);
          setFirmaBaul(false);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [open, instanceId, tenantId, parte, reloadKey]);

  const kyverumIds = useMemo(
    () => validations.filter((v) => v.provider === KYVERUM).map((v) => v.id),
    [validations],
  );

  const title = `Tracking de identidad · ${rotulo}`;

  return (
    <Modal open={open} onClose={onClose} title={title} size="lg">
      {loading ? <SeccionCargando etiqueta="Cargando tracking de identidad" filas={3} /> : null}
      {!loading && error ? (
        <SeccionError
          mensaje={error}
          contexto="el tracking de identidad"
          onReintentar={() => setReloadKey((k) => k + 1)}
        />
      ) : null}
      {!loading && !error && firmaBaul && kyverumIds.length === 0 ? (
        <SeccionVacia mensaje="Esta parte está cubierta por firma electrónica (baúl). No hay bitácora de validación de identidad." />
      ) : null}
      {!loading && !error && !firmaBaul && kyverumIds.length === 0 ? (
        <SeccionVacia mensaje="No hay validaciones de identidad con bitácora para esta parte." />
      ) : null}
      {!loading && !error && kyverumIds.length > 0 ? (
        <div className="space-y-3">
          {kyverumIds.map((id) => (
            <IdentityValidationTrackingPanel key={id} validationId={id} defaultOpen />
          ))}
        </div>
      ) : null}
    </Modal>
  );
}
