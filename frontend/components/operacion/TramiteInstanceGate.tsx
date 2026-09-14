'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';
import { TramiteDetalleModal } from '@/components/operacion/TramiteDetalleModal';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { usePermissions } from '@/hooks/usePermissions';
import { tramitesClient } from '@/lib/api/tramites-client';
import { isNetworkReadOnly, type NetworkInstanceDetail } from '@/lib/tramites/network-scope';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12362 (AC7) — entrada a `/tramites/{id}` con conciencia de red.
 *
 * <p>La fila del listado nunca manda un trámite de un cliente hijo al asistente, así que la única
 * forma de llegar aquí con uno es un enlace directo (copiado, guardado, compartido). Para la cabeza
 * de red esa dirección NO abre el asistente: se resuelve el trámite por la ruta consolidada y, si
 * el dueño no es el propio tenant, se muestra el detalle en modo consulta. Ni una petición de
 * escritura, ni un error del servidor a la vista.</p>
 *
 * <p>Para cualquier otro usuario —cliente sin jerarquía, cliente hijo, SuperAdmin— el componente es
 * transparente: monta el asistente de siempre sin ninguna llamada adicional (AC4/AC5).</p>
 */

type Estado =
  | { kind: 'asistente' }
  | { kind: 'resolviendo' }
  | { kind: 'consulta'; item: InstanceSummary };

/**
 * Resumen mínimo para el modal cuando el listado consolidado no devolvió la fila (p. ej. el
 * radicado no coincide con la búsqueda libre). Los campos que el detalle no trae quedan en su
 * valor «no aplica»; el modal completa el resto con sus propias cargas.
 */
function resumenDesdeDetalle(detail: NetworkInstanceDetail): InstanceSummary {
  return {
    id: detail.id,
    referenceNumber: detail.referenceNumber,
    modalidad: 'OTROS',
    estado: detail.status,
    plateFlowStatus: detail.plateFlowStatus ?? null,
    placa: null,
    vin: null,
    vehiculoMarca: null,
    vehiculoLinea: null,
    compradorNombre: null,
    compradorDocumento: null,
    organismoTransito: null,
    pasoActual: 0,
    totalPasos: 0,
    createdAt: detail.createdAt,
    draftFinalizedAt: detail.draftFinalizedAt ?? null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: false,
    prioritario: detail.prioritario ?? false,
    tenantId: detail.tenantId,
    companiaNombre: detail.tenantName ?? null,
    subsanacionActiva: detail.subsanacionActiva ?? false,
    subsanacionCount: detail.subsanacionCount ?? 0,
    ultimoRechazoMotivo: null,
  };
}

export function TramiteInstanceGate({ instanceId }: { instanceId: string }) {
  const router = useRouter();
  const { isGroupParent, isSuperAdmin, tenantId } = usePermissions();
  // Solo la cabeza de red (no SuperAdmin) puede haber llegado a un trámite ajeno por enlace.
  const esCabeza = isGroupParent && !isSuperAdmin && !!tenantId;
  const [estado, setEstado] = useState<Estado>(
    esCabeza ? { kind: 'resolviendo' } : { kind: 'asistente' },
  );

  useEffect(() => {
    if (!esCabeza) return;
    let active = true;
    const resolver = async () => {
      let detail: NetworkInstanceDetail;
      try {
        detail = await tramitesClient.getNetworkInstance(instanceId);
      } catch {
        // Fuera de la red (o ruta no disponible): el asistente sigue siendo la pantalla dueña de
        // ese caso y de su propio manejo de errores. No se inventa nada aquí.
        if (active) setEstado({ kind: 'asistente' });
        return;
      }
      if (!active) return;
      // Se compara SOLO el tenant: la marca de procedencia (`fromNetwork`) la lleva todo lo que
      // sale de la ruta consolidada, incluidos los trámites propios de la cabeza.
      if (!isNetworkReadOnly({ tenantId: detail.tenantId }, tenantId)) {
        setEstado({ kind: 'asistente' });
        return;
      }
      let item: InstanceSummary | null = null;
      try {
        const res = await tramitesClient.searchNetworkInstances({
          busqueda: detail.referenceNumber,
          take: 10,
          skip: 0,
        });
        item = res.items.find((it) => it.id === detail.id) ?? null;
      } catch {
        item = null;
      }
      if (!active) return;
      setEstado({ kind: 'consulta', item: item ?? resumenDesdeDetalle(detail) });
    };
    void resolver();
    return () => {
      active = false;
    };
  }, [esCabeza, instanceId, tenantId]);

  if (estado.kind === 'resolviendo') {
    return <CarLoaderModal label="Abriendo trámite…" />;
  }

  if (estado.kind === 'consulta') {
    return (
      <TramiteDetalleModal
        open
        onClose={() => router.push('/tramites')}
        instanceId={instanceId}
        item={estado.item}
        readOnly
        consultaMode
      />
    );
  }

  return <TramiteWizard existingInstanceId={instanceId} onExit={() => router.push('/tramites')} />;
}
