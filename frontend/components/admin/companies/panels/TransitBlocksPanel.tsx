"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Ban, Search } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { RowActions } from "@/components/atom/RowActions";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { OtScopeConfirmDialog } from "@/components/admin/companies/OtScopeConfirmDialog";
import {
  addTransitBlock,
  fetchTransitBlocks,
  fetchTransitOffices,
  removeTransitBlock,
} from "@/lib/api/admin-companies";
import {
  fetchTransitOfficesOperationalStatus,
  type TransitOfficeOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";
import type { TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";

type PendingAction = {
  office: TransitOffice;
  enable: boolean;
};

/**
 * Consola SuperAdmin de bloqueos de OT para cabeza Marca Blanca (HU #12408 AC1/AC2).
 * Solo se monta cuando la ficha es MARCA_BLANCA sin padre.
 */
export function TransitBlocksPanel({
  tenantId,
  activeChildrenCount = 0,
}: {
  tenantId: string;
  activeChildrenCount?: number;
}) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [blockedIds, setBlockedIds] = useState<string[]>([]);
  const [operationalById, setOperationalById] = useState<
    Record<string, { hasTenant: boolean; estadoActivo: boolean | null }>
  >({});
  const [search, setSearch] = useState("");
  // Bug #13055 — tabla homologada con el modelo de trámites: filas por página elegibles.
  const pg = usePaginacion();
  const [pending, setPending] = useState<PendingAction | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [catalog, blocks, opStatus] = await Promise.all([
          fetchTransitOffices(undefined, signal),
          fetchTransitBlocks(tenantId, signal),
          fetchTransitOfficesOperationalStatus(signal).catch(
            () => [] as TransitOfficeOperationalStatus[],
          ),
        ]);
        if (signal?.aborted) return;
        setOffices(catalog);
        setBlockedIds(blocks.transitOfficeIds);
        setOperationalById(
          Object.fromEntries(
            opStatus.map((s) => [s.id, { hasTenant: s.hasTenant, estadoActivo: s.estadoActivo }]),
          ),
        );
        setStatus(catalog.length === 0 ? "empty" : "ready");
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

  const operableOffices = useMemo(
    () =>
      offices.filter((office) => {
        const op = operationalById[office.id];
        return Boolean(op?.hasTenant && op.estadoActivo);
      }),
    [offices, operationalById],
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return operableOffices;
    return operableOffices.filter(
      (o) => o.name.toLowerCase().includes(q) || o.code.toLowerCase().includes(q),
    );
  }, [operableOffices, search]);

  const lastPage = Math.max(1, Math.ceil(filtered.length / pg.pageSize));
  const safePage = Math.min(pg.page, lastPage);
  const pageRows = pg.paginar(filtered);

  const requestToggle = (office: TransitOffice) => {
    const enable = !blockedIds.includes(office.id);
    if (activeChildrenCount > 0) {
      setPending({ office, enable });
      return;
    }
    void persistToggle(office, enable);
  };

  const persistToggle = async (office: TransitOffice, enable: boolean) => {
    setBusy(true);
    const prev = blockedIds;
    setBlockedIds((current) =>
      enable ? Array.from(new Set([...current, office.id])) : current.filter((id) => id !== office.id),
    );
    try {
      if (enable) {
        await addTransitBlock(tenantId, office.id);
      } else {
        await removeTransitBlock(tenantId, office.id);
      }
    } catch (err) {
      setBlockedIds(prev);
      const serverMessage =
        err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      show(
        serverMessage ??
          `No se pudo ${enable ? "bloquear" : "desbloquear"} ${office.name}.`,
        "error",
      );
    } finally {
      setBusy(false);
      setPending(null);
    }
  };

  const columns: DataTableColumn<TransitOffice>[] = [
    {
      key: "name",
      header: "Organismo",
      render: (office) => <span className="font-semibold">{office.name}</span>,
    },
    {
      key: "code",
      header: "Código",
      render: (office) => <span className="font-mono opacity-60">{office.code}</span>,
    },
    {
      key: "estado",
      header: "Estado en red",
      render: (office) => {
        const blocked = blockedIds.includes(office.id);
        return (
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge
              label={blocked ? "Bloqueado" : "Disponible"}
              tone={blocked ? "warning" : "success"}
            />
            <span className="sr-only">{blocked ? "Bloqueado para la red" : "Disponible para radicar"}</span>
          </div>
        );
      },
    },
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (office) => {
        const blocked = blockedIds.includes(office.id);
        return (
          <RowActions
            actions={[
              {
                icon: Ban,
                label: `${blocked ? "Desbloquear" : "Bloquear"} ${office.name}`,
                onClick: () => requestToggle(office),
                tone: blocked ? "primary" : "danger",
                disabled: busy,
              },
            ]}
          />
        );
      },
    },
  ];

  return (
    <>
      <section aria-label="Bloqueos de organismos de tránsito" className="space-y-3">
        <div className="flex items-start gap-2">
          <Ban className="mt-0.5 h-4 w-4 shrink-0 opacity-70" aria-hidden />
          <div>
            <h4 className="text-sm font-semibold">Organismos bloqueados (Marca Blanca)</h4>
            <p className="mt-0.5 text-xs opacity-60">
              Excluye organismos de la red: la cabeza y sus clientes hijos dejan de verlos al radicar.
              Solo aplica a compañías Marca Blanca.
            </p>
          </div>
        </div>

        {status === "loading" ? (
          <CarLoaderModal label="Cargando bloqueos de organismos…" />
        ) : (
        <UiStateBoundary
          status={status === "ready" && operableOffices.length === 0 ? "empty" : status}
          onRetry={() => void load()}
          emptyMessage="No hay organismos operativos en la plataforma."
          errorMessage="No se pudieron cargar los bloqueos de organismos."
          skeletonRows={3}
        >
          <div className="flex max-w-md items-center gap-2 rounded-xl border bg-white p-2.5 focus-within:outline focus-within:outline-2 focus-within:outline-[#557EFF] dark:bg-[#0B0F14]">
            <Search className="h-4 w-4 opacity-60" aria-hidden />
            <label htmlFor="ot-blocks-search" className="sr-only">
              Buscar organismo de tránsito
            </label>
            <input
              id="ot-blocks-search"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                pg.setPage(1);
              }}
              placeholder="Buscar por nombre o código…"
              className="flex-1 bg-transparent text-xs outline-none"
            />
          </div>

          <DataTable
            columns={columns}
            rows={pageRows}
            getRowKey={(office) => office.id}
            emptyMessage="Ningún organismo coincide con la búsqueda."
            ariaLabel="Bloqueos de organismos de tránsito"
            minWidth={560}
            pagination={{
              page: safePage,
              pageSize: pg.pageSize,
              totalCount: filtered.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
        )}
      </section>

      {pending && (
        <OtScopeConfirmDialog
          open
          action={pending.enable ? "bloquear" : "desbloquear"}
          officeName={pending.office.name}
          affectedChildrenCount={activeChildrenCount}
          busy={busy}
          onConfirm={() => void persistToggle(pending.office, pending.enable)}
          onCancel={() => {
            if (!busy) setPending(null);
          }}
        />
      )}
    </>
  );
}
