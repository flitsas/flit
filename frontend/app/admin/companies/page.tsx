"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, Building2 } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
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
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { createCompany, fetchCompaniesIndex, updateCompany } from "@/lib/api/admin-companies";
import type { CompanyListItem, CompanyPagedResult } from "@/lib/api/types";
import { usePermissions } from "@/hooks/usePermissions";

const PAGE_SIZE = 20;

// Consola admin — listado de compañías (HU #10194, AC1/AC7) + alta de compañías
// (#10118). Filtrado y paginación server-side; 4 estados UI vía UiStateBoundary.
// HU #11227: SuperAdmin puede incluir OT con toggle. HU #11228: AdminCompany redirige a su ficha.
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
  const { isSuperAdmin, isAdminCompany, tenantId } = usePermissions();
  const [filters, setFilters] = useState<CompanyFilters>({});
  const [page, setPage] = useState(1);
  // HU #11227 — solo SuperAdmin; desactivado = excluir OT (default).
  const [includeTransitOffices, setIncludeTransitOffices] = useState(false);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [result, setResult] = useState<CompanyPagedResult | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<CompanyListItem | null>(null);
  const [toggleTarget, setToggleTarget] = useState<CompanyListItem | null>(null);

  // HU #11228 — AdminCompany no ve el listado multi-compañía: va al configurador de su tenant.
  useEffect(() => {
    if (isAdminCompany && !isSuperAdmin && tenantId) {
      router.replace(`/admin/companies/${tenantId}`);
    }
  }, [isAdminCompany, isSuperAdmin, tenantId, router]);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      if (isAdminCompany && !isSuperAdmin) {
        return;
      }
      setStatus("loading");
      try {
        const data = await fetchCompaniesIndex(
          {
            ...filters,
            page,
            pageSize: PAGE_SIZE,
            // SuperAdmin: toggle; cualquier otro caso (defensa) excluye OT.
            excludeTransitOffices: isSuperAdmin ? !includeTransitOffices : true,
          },
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
    [filters, page, includeTransitOffices, isSuperAdmin, isAdminCompany],
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
    <div className="flex min-h-screen flex-col gap-4 px-4 md:px-6 pt-6 pb-10">
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
        <CreateButton label="Crear compañía" icon={Building2} onClick={() => setCreateOpen(true)} />
      </div>

      {isSuperAdmin && (
        <ToggleSwitch
          id="include-transit-offices"
          label="Incluir organismos de tránsito"
          description="Solo SuperAdmin. Desactivado = solo compañías B2B."
          checked={includeTransitOffices}
          onChange={(next) => {
            setIncludeTransitOffices(next);
            setPage(1);
          }}
        />
      )}

      <CompanyFiltersPanel onApply={handleApplyFilters} initialValue={filters} />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <UiStateBoundary
          status={status}
          onRetry={() => void load()}
          emptyMessage="No se encontraron compañías B2B con los filtros aplicados."
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
