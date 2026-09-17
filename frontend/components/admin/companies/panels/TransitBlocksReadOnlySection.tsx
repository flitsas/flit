"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Ban } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { fetchTransitBlocks, fetchTransitOffices } from "@/lib/api/admin-companies";
import type { TransitOffice } from "@/lib/api/types";

/**
 * Lista de OT bloqueados en solo lectura para cabeza o hijo de Marca Blanca (HU #12408 AC3/AC4).
 */
export function TransitBlocksReadOnlySection({
  tenantId,
  legend = "Los administra la plataforma",
}: {
  tenantId: string;
  legend?: string;
}) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const [blockedOffices, setBlockedOffices] = useState<TransitOffice[]>([]);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [catalog, blocks] = await Promise.all([
          fetchTransitOffices(undefined, signal),
          fetchTransitBlocks(tenantId, signal),
        ]);
        if (signal?.aborted) return;
        const byId = new Map(catalog.map((o) => [o.id, o]));
        const rows = blocks.transitOfficeIds
          .map((id) => byId.get(id))
          .filter((o): o is TransitOffice => Boolean(o));
        setBlockedOffices(rows);
        setStatus(rows.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const columns: DataTableColumn<TransitOffice>[] = useMemo(
    () => [
      {
        key: "name",
        header: "Organismo bloqueado",
        render: (office) => <span className="font-semibold">{office.name}</span>,
      },
      {
        key: "code",
        header: "Código",
        render: (office) => <span className="font-mono opacity-60">{office.code}</span>,
      },
      {
        key: "estado",
        header: "Estado",
        render: () => (
          <StatusBadge label="Bloqueado" tone="warning" />
        ),
      },
    ],
    [],
  );

  return (
    <section aria-label="Organismos bloqueados" className="space-y-2">
      <div className="flex items-center gap-2">
        <Ban className="h-4 w-4 opacity-70" aria-hidden />
        <h5 className="text-xs font-semibold">Organismos bloqueados</h5>
      </div>
      <p className="text-[10px] opacity-60" role="note">
        {legend}
      </p>
      <UiStateBoundary
        status={status}
        onRetry={() => void load()}
        emptyMessage="No hay organismos bloqueados para esta compañía."
        errorMessage="No se pudieron cargar los organismos bloqueados."
        skeletonRows={2}
      >
        <DataTable
          columns={columns}
          rows={blockedOffices}
          getRowKey={(office) => office.id}
          ariaLabel="Organismos bloqueados"
          minWidth={480}
        />
      </UiStateBoundary>
    </section>
  );
}
