"use client";

import { useCallback, useEffect, useState } from "react";
import { Link2, Unlink } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchCompanyChildren } from "@/lib/api/admin-companies";
import { isHeadTenantType, tenantTypeLabel } from "@/lib/api/types";
import type { CompanyChildListItem, CompanyListItem } from "@/lib/api/types";
import { LinkCompanyDialog } from "./LinkCompanyDialog";
import { UnlinkCompanyDialog } from "./UnlinkCompanyDialog";

export interface CompanyChildrenSectionProps {
  company: CompanyListItem;
}

/** HU #12357 — sección «Clientes hijos» en ficha SuperAdmin de cabeza de grupo. */
export function CompanyChildrenSection({ company }: CompanyChildrenSectionProps) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const [children, setChildren] = useState<CompanyChildListItem[]>([]);
  const [linkOpen, setLinkOpen] = useState(false);
  const [unlinkTarget, setUnlinkTarget] = useState<CompanyChildListItem | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const data = await fetchCompanyChildren(company.id, signal);
      if (signal?.aborted) return;
      setChildren(data);
      setStatus(data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) {
        setStatus("error");
      }
    }
  }, [company.id]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  if (!isHeadTenantType(company.tenantType) && !company.isGroupParent) {
    return null;
  }

  const activeCount = children.filter((c) => c.estadoActivo).length;

  return (
    <section
      className="mt-6 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60"
      aria-labelledby="children-section-title"
    >
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 id="children-section-title" className="text-sm font-bold">
            Clientes hijos
          </h2>
          <p className="text-[11px] opacity-60">
            {company.tenantType === "MARCA_BLANCA"
              ? "Clientes vinculados a esta Marca Blanca."
              : "Clientes de la Concesión vinculados por el SuperAdmin."}
          </p>
        </div>
        <CreateButton label="Vincular cliente existente" icon={Link2} onClick={() => setLinkOpen(true)} />
      </div>

      <UiStateBoundary
        status={status}
        onRetry={() => void load()}
        emptyMessage="Aún no hay clientes vinculados a esta cabeza de grupo."
        errorMessage="No se pudo cargar el listado de clientes hijos."
        skeletonRows={3}
      >
        <div className="overflow-x-auto">
          <table className="w-full min-w-[560px] border-separate border-spacing-y-2 text-xs">
            <thead>
              <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
                <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Razón social
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  NIT
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Tipo
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Estado
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Vinculación
                </th>
                <th className="rounded-r-xl px-4 py-2.5 text-right" style={{ background: "#DFE5ED" }}>
                  Acciones
                </th>
              </tr>
            </thead>
            <tbody>
              {children.map((child) => (
                <tr key={child.id} className="bg-white dark:bg-[#0B0F14]">
                  <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold">{child.razonSocial}</td>
                  <td className="border-y px-4 py-3 font-mono">{child.nit}</td>
                  <td className="border-y px-4 py-3">{tenantTypeLabel(child.tenantType)}</td>
                  <td className="border-y px-4 py-3">
                    {child.estadoActivo ? (
                      <StatusBadge label="Activo" tone="success" />
                    ) : (
                      <StatusBadge label="Inactivo" tone="danger" />
                    )}
                  </td>
                  <td className="border-y px-4 py-3 opacity-70">{formatDate(child.fechaVinculacion)}</td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                    <button
                      type="button"
                      onClick={() => setUnlinkTarget(child)}
                      className="inline-flex items-center gap-1 rounded-lg border px-2.5 py-1.5 text-[10px] font-semibold"
                      aria-label={`Desvincular ${child.razonSocial}`}
                    >
                      <Unlink className="h-3 w-3" aria-hidden />
                      Desvincular
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

      {linkOpen && (
        <LinkCompanyDialog
          open
          headTenantId={company.id}
          headName={company.razonSocial}
          linkedChildIds={children.map((c) => c.id)}
          onClose={() => setLinkOpen(false)}
          onLinked={() => {
            setLinkOpen(false);
            void load();
          }}
        />
      )}

      {unlinkTarget && (
        <UnlinkCompanyDialog
          child={unlinkTarget}
          headName={company.razonSocial}
          onClose={() => setUnlinkTarget(null)}
          onUnlinked={() => {
            setUnlinkTarget(null);
            void load();
          }}
        />
      )}

      <p className="sr-only" aria-live="polite">
        {activeCount} clientes hijos activos
      </p>
    </section>
  );
}

function formatDate(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleDateString("es-CO", { year: "numeric", month: "2-digit", day: "2-digit" });
}
