'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Undo2 } from 'lucide-react';
import { ModuleTitle } from '@/components/atom/modules/ModuleTitle';
import { UiStateBoundary, type UiStatus } from '@/components/admin/UiStateBoundary';
import { usePermissions } from '@/hooks/usePermissions';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { RevocationRequestListItem } from '@/lib/api/types/revocation-requests';
import type { TransitOfficeOption } from '@/lib/api/types/procedure-runtime';
import {
  RevocationRequestsFiltersBar,
  type RevocationRequestsFiltersValue,
} from '@/components/operacion/RevocationRequestsFiltersBar';
import { RevocationRequestsTable } from '@/components/operacion/RevocationRequestsTable';

const TAKE = 20;

const FILTROS_VACIOS: RevocationRequestsFiltersValue = {
  requestedFrom: '',
  requestedTo: '',
  statuses: [],
  transitOfficeId: '',
};

/**
 * HU #12578 (Feature #12565) — vista dedicada "Revocatorias" del lado gestor: listado filtrado a
 * trámites con solicitud de revocatoria en cualquier sub-estado (AC1), con los mismos filtros del
 * listado general (fecha, OT, estado).
 *
 * AC2 — visible SOLO para el Administrador de compañía (`isAdminCompany`, mismo gate que
 * `RevocationRequestButton` de HU #12573): el enlace de entrada (`TramitesTable`, botón "Revocatorias")
 * ya se oculta para cualquier otro rol; esta página se auto-protege además por si alguien llega por URL
 * directa, sin inventar un segundo sistema de permisos — es el MISMO `usePermissions().isAdminCompany`.
 */
export default function RevocatoriasPage() {
  const router = useRouter();
  const { isAdminCompany } = usePermissions();

  const [filtros, setFiltros] = useState<RevocationRequestsFiltersValue>(FILTROS_VACIOS);
  const [skip, setSkip] = useState(0);
  const [status, setStatus] = useState<UiStatus>('loading');
  const [items, setItems] = useState<RevocationRequestListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [transitOffices, setTransitOffices] = useState<TransitOfficeOption[]>([]);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!isAdminCompany) return;
    let cancelled = false;
    void tramitesClient
      .listTransitOffices()
      .then((offices) => {
        if (!cancelled) setTransitOffices(offices);
      })
      .catch(() => {
        // El selector de OT es un filtro adicional, no una condición de carga: sin catálogo la
        // vista sigue funcionando (queda sin ese filtro), igual que el resto de listados.
      });
    return () => {
      cancelled = true;
    };
  }, [isAdminCompany]);

  const cargar = useCallback(
    (signal: AbortSignal) => {
      setStatus((prev) => (prev === 'ready' ? 'ready' : 'loading'));
      void tramitesClient
        .listRevocationRequests({
          statuses: filtros.statuses.length ? filtros.statuses : undefined,
          requestedFrom: filtros.requestedFrom || undefined,
          requestedTo: filtros.requestedTo || undefined,
          transitOfficeId: filtros.transitOfficeId || undefined,
          skip,
          take: TAKE,
        })
        .then((res) => {
          if (signal.aborted) return;
          setItems(res.items);
          setTotal(res.total);
          setStatus(res.items.length === 0 ? 'empty' : 'ready');
        })
        .catch(() => {
          if (signal.aborted) return;
          setStatus('error');
        });
    },
    [filtros, skip],
  );

  useEffect(() => {
    if (!isAdminCompany) return;
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    cargar(controller.signal);
    return () => controller.abort();
  }, [isAdminCompany, cargar, reloadKey]);

  // Cambiar filtros vuelve siempre a la página 1: una página fuera de rango con el nuevo universo
  // filtrado se leería como "vacío" aunque sí haya resultados.
  const handleFiltrosChange = (next: RevocationRequestsFiltersValue) => {
    setFiltros(next);
    setSkip(0);
  };

  if (!isAdminCompany) {
    return (
      <div className="flex flex-col gap-4">
        <ModuleTitle title="Revocatorias" />
        <div
          role="alert"
          className="rounded-2xl border border-[#DFE5ED] bg-white p-6 text-sm text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
        >
          Solo el Administrador de la compañía puede ver las solicitudes de revocatoria.
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <ModuleTitle
        title="Revocatorias"
        subtitle="Trámites con solicitud de revocatoria, en cualquier sub-estado"
        right={<Undo2 className="h-5 w-5 opacity-60" aria-hidden="true" />}
      />

      <div className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]">
        <RevocationRequestsFiltersBar
          value={filtros}
          onChange={handleFiltrosChange}
          transitOfficeOptions={transitOffices}
          disabled={status === 'loading'}
        />
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="No hay solicitudes de revocatoria con los filtros aplicados."
        errorMessage="No se pudo cargar el listado de revocatorias."
        onRetry={() => setReloadKey((k) => k + 1)}
      >
        <RevocationRequestsTable
          items={items}
          total={total}
          skip={skip}
          take={TAKE}
          onPageChange={setSkip}
          onView={(item) => router.push(`/tramites/${item.procedureInstanceId}`)}
        />
      </UiStateBoundary>
    </div>
  );
}
