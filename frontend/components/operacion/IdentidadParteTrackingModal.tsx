'use client';

import { useEffect, useMemo, useState } from 'react';
import { Modal } from '@/components/atom/Modal';
import { IdentidadLecturaHumana } from '@/components/operacion/IdentidadLecturaHumana';
import { SeccionCargando, SeccionError, SeccionVacia } from '@/components/operacion/detalle/primitivos';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { BiometricParte, BiometricValidation } from '@/lib/api/types/procedure-runtime';

const KYVERUM = 'kyverum';

/**
 * Modal de tracking de identidad por UNA parte (click en línea de la columna Firmas).
 *
 * <p>HU #12186 — pinta la LECTURA HUMANA (`IdentidadLecturaHumana`), no la bitácora cruda. La
 * bitácora sigue estando, dentro de esa misma lectura y un clic más adentro, para soporte. El
 * panel técnico a secas se conserva donde tiene sentido: dentro del asistente, delante del
 * operador que está a mitad del flujo y sí quiere el diagnóstico.</p>
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

  // Solo las validaciones con bitácora. El filtro mira el proveedor, pero eso NO llega a pantalla:
  // lo que se pinta es la lectura humana, que no lo nombra en ningún sitio.
  const conBitacora = useMemo(
    () => validations.filter((v) => v.provider === KYVERUM),
    [validations],
  );

  const title = `Validación de identidad · ${rotulo}`;

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
      {!loading && !error && firmaBaul && conBitacora.length === 0 ? (
        <SeccionVacia mensaje="Esta parte quedó acreditada con su firma electrónica, así que no hizo falta validar su identidad." />
      ) : null}
      {!loading && !error && !firmaBaul && conBitacora.length === 0 ? (
        <SeccionVacia mensaje="Todavía no se ha iniciado la validación de identidad de esta parte." />
      ) : null}
      {!loading && !error && conBitacora.length > 0 ? (
        <div className="space-y-3">
          {conBitacora.map((v) => (
            <IdentidadLecturaHumana key={v.id} validation={v} rolLabel={rotulo} tenantId={tenantId} />
          ))}
        </div>
      ) : null}
    </Modal>
  );
}
