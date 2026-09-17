"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchOtRevocationRequests } from "@/lib/api/admin-ot";
import type { OtRevocationRequestListItem } from "@/lib/api/types-ot";
import {
  RevocationRequestsFiltersBar,
  type RevocationRequestsFiltersValue,
} from "@/components/operacion/RevocationRequestsFiltersBar";
import { RevocationRequestsTable } from "@/components/operacion/RevocationRequestsTable";

const TAKE = 20;

const FILTROS_VACIOS: RevocationRequestsFiltersValue = {
  requestedFrom: "",
  requestedTo: "",
  statuses: [],
  transitOfficeId: "",
};

/**
 * HU #12578 (Feature #12565) — vista dedicada "Revocatorias" del lado OT: TODOS los intentos de
 * solicitud de revocatoria de los trámites del organismo, en cualquier sub-estado (AC1), con los
 * mismos filtros del listado general (fecha, estado). Sin selector de organismo: el alcance YA es un
 * único organismo, resuelto por `transitOfficeId` (perfil de Admin OT o `?transitOfficeId=` del
 * SuperAdmin — mismo mecanismo que `ClientProceduresSection`), así que
 * `RevocationRequestsFiltersBar` se monta sin `transitOfficeOptions`.
 *
 * AC2 — visible solo para Admin OT (o SuperAdmin, que supervisa TODA la bandeja OT): la pestaña que
 * lleva aquí (dock `Shell.tsx` / `OT_HUB_TABS`) ya está gateada por rol; esta sección no repite un
 * segundo chequeo de permisos — igual que el resto de secciones del hub OT.
 */
export function RevocationRequestsSection({ transitOfficeId }: { transitOfficeId?: string }) {
  const [filtros, setFiltros] = useState<RevocationRequestsFiltersValue>(FILTROS_VACIOS);
  const [skip, setSkip] = useState(0);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [items, setItems] = useState<OtRevocationRequestListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);

  const cargar = useCallback(
    (signal: AbortSignal) => {
      setStatus((prev) => (prev === "ready" ? "ready" : "loading"));
      void fetchOtRevocationRequests(
        {
          statuses: filtros.statuses.length ? filtros.statuses : undefined,
          requestedFrom: filtros.requestedFrom || undefined,
          requestedTo: filtros.requestedTo || undefined,
          skip,
          take: TAKE,
        },
        signal,
        { transitOfficeId },
      )
        .then((res) => {
          if (signal.aborted) return;
          setItems(res.items);
          setTotal(res.total);
          setStatus(res.items.length === 0 ? "empty" : "ready");
        })
        .catch(() => {
          if (signal.aborted) return;
          setStatus("error");
        });
    },
    [filtros, skip, transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    cargar(controller.signal);
    return () => controller.abort();
  }, [cargar, reloadKey]);

  const handleFiltrosChange = (next: RevocationRequestsFiltersValue) => {
    setFiltros(next);
    setSkip(0);
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]">
        <RevocationRequestsFiltersBar
          value={filtros}
          onChange={handleFiltrosChange}
          disabled={status === "loading"}
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
        />
      </UiStateBoundary>
    </div>
  );
}
