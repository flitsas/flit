"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, Plus } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import {
  CompanyFiltersPanel,
  type CompanyFilters,
} from "@/components/admin/companies/CompanyFiltersPanel";
import { CompanyListTable } from "@/components/admin/companies/CompanyListTable";
import { CompanyStatusDialog } from "@/components/admin/companies/CompanyStatusDialog";
import { CreateCompanyDialog } from "@/components/admin/companies/CreateCompanyDialog";
import { EditCompanyDialog } from "@/components/admin/companies/EditCompanyDialog";
import { createCompany, fetchCompaniesIndex, updateCompany } from "@/lib/api/admin-companies";
import type { CompanyListItem, CompanyPagedResult } from "@/lib/api/types";

const PAGE_SIZE = 20;

// Consola admin — listado de compañías (HU #10194, AC1/AC7) + alta de compañías
// (#10118). Filtrado y paginación server-side; 4 estados UI vía UiStateBoundary.
export default function AdminCompaniesPage() {
  return (
    <ToastProvider>
      <CompaniesList />
    </ToastProvider>
  );
}

function CompaniesList() {
  const router = useRouter();
  const { show } = useToast();
  const [filters, setFilters] = useState<CompanyFilters>({});
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [result, setResult] = useState<CompanyPagedResult | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<CompanyListItem | null>(null);
  const [toggleTarget, setToggleTarget] = useState<CompanyListItem | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchCompaniesIndex(
          { ...filters, page, pageSize: PAGE_SIZE },
          signal,
        );
        if (signal?.aborted) {
          return;
        }
        setResult(data);
        setStatus(data.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [filters, page],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial de datos al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleApplyFilters = (next: CompanyFilters) => {
    setPage(1);
    setFilters(next);
  };

  const handleCreated = (razonSocial: string) => {
    setCreateOpen(false);
    show(`Compañía «${razonSocial}» creada.`, "success");
    // Vuelve a la primera página (orden por fecha DESC → la nueva aparece arriba).
    setPage(1);
    setFilters({});
    void load();
  };

  // Reemplaza la fila tocada con la compañía ya actualizada (update optimista local:
  // evita recargar todo el listado solo por un cambio de estado).
  const handleStatusConfirmed = (updated: CompanyListItem) => {
    setResult((prev) =>
      prev
        ? { ...prev, data: prev.data.map((c) => (c.id === updated.id ? updated : c)) }
        : prev,
    );
    setToggleTarget(null);
    show(
      `Compañía «${updated.razonSocial}» ${updated.estadoActivo ? "activada" : "desactivada"}.`,
      "success",
    );
  };

  // Reemplaza la fila editada con la compañía actualizada (update optimista local).
  const handleEdited = (updated: CompanyListItem) => {
    setResult((prev) =>
      prev
        ? { ...prev, data: prev.data.map((c) => (c.id === updated.id ? updated : c)) }
        : prev,
    );
    setEditTarget(null);
    show(`Compañía «${updated.razonSocial}» actualizada.`, "success");
  };

  return (
    <div className="flex min-h-full flex-col gap-4 px-6 pt-6 pb-24">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" /> Volver al inicio
      </button>

      <div className="flex flex-wrap items-start justify-between gap-3">
        <ModuleTitle
          title="Administración de compañías"
          subtitle="Parametriza políticas operativas y supervisa la auditoría de cada compañía B2B."
        />
        <button
          type="button"
          onClick={() => setCreateOpen(true)}
          className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white shadow-sm"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Plus className="h-4 w-4" /> Crear compañía
        </button>
      </div>

      <CompanyFiltersPanel onApply={handleApplyFilters} initialValue={filters} />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60" style={{ borderColor: "#DFE5ED" }}>
        <UiStateBoundary
          status={status}
          onRetry={() => void load()}
          emptyMessage="No se encontraron compañías con los filtros aplicados."
          errorMessage="No se pudo cargar el listado de compañías."
        >
          {result && (
            <CompanyListTable
              items={result.data}
              totalCount={result.totalCount}
              page={result.page}
              pageSize={result.pageSize}
              onPageChange={setPage}
              onConfigure={(tenantId) => router.push(`/admin/companies/${tenantId}`)}
              onEdit={setEditTarget}
              onToggleStatus={setToggleTarget}
            />
          )}
        </UiStateBoundary>
      </div>

      <CreateCompanyDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreate={createCompany}
        onCreated={(company) => handleCreated(company.razonSocial)}
      />

      {editTarget && (
        <EditCompanyDialog
          open
          company={editTarget}
          onClose={() => setEditTarget(null)}
          onUpdate={updateCompany}
          onUpdated={handleEdited}
        />
      )}

      {toggleTarget && (
        <CompanyStatusDialog
          company={toggleTarget}
          onClose={() => setToggleTarget(null)}
          onConfirmed={handleStatusConfirmed}
        />
      )}
    </div>
  );
}
