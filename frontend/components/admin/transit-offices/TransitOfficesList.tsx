"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Power, PowerOff, Plus, Search, SlidersHorizontal } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { useToast } from "@/components/admin/Toast";
import {
  fetchTransitOfficesOperationalStatus,
  type TransitOfficeOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";
import { matchesOtOfficeSearch, otHubModulePath } from "@/components/admin/transit-offices/ot-nav";
import { TransitOfficeStatusDialog } from "@/components/admin/transit-offices/TransitOfficeStatusDialog";

/**
 * Listado de organismos de tránsito con estado operativo (RF01) + acciones de ciclo de
 * vida para SuperAdmin (RF02 activar, RF03 desactivar). Cada fila muestra si el OT tiene
 * tenant dado de alta y, si lo tiene, si está activo/inactivo. Sin alta → «Dar de alta»
 * (abre CreateTransitOfficeTenantDialog vía `onCreateTenant`); con tenant → «Administrar»
 * + Activar/Desactivar con confirmación.
 */
export interface TransitOfficesListProps {
  /** Solicita el alta de tenant para una oficina sin alta (la maneja la página). */
  onCreateTenant?: (office: TransitOfficeOperationalStatus) => void;
}

export function TransitOfficesList({ onCreateTenant }: TransitOfficesListProps = {}) {
  const router = useRouter();
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOfficeOperationalStatus[]>([]);
  const [search, setSearch] = useState("");
  const [statusTarget, setStatusTarget] = useState<TransitOfficeOperationalStatus | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const data = await fetchTransitOfficesOperationalStatus(signal);
      if (signal?.aborted) {
        return;
      }
      setOffices(data);
      setStatus(data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) {
        setStatus("error");
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const filtered = useMemo(
    () => offices.filter((o) => matchesOtOfficeSearch(o, search)),
    [offices, search],
  );

  const listStatus: UiStatus =
    status === "ready" && filtered.length === 0 && search.trim() !== ""
      ? "empty"
      : status;

  const rowActions = useCallback(
    (office: TransitOfficeOperationalStatus): RowAction[] => {
      if (!office.hasTenant) {
        return [
          {
            icon: Plus,
            label: `Dar de alta ${office.name}`,
            onClick: () => onCreateTenant?.(office),
            tone: "primary",
          },
        ];
      }

      const activo = office.estadoActivo === true;
      return [
        {
          icon: SlidersHorizontal,
          label: `Administrar ${office.name}`,
          onClick: () => router.push(otHubModulePath(office.id, "tramites")),
          tone: "primary",
        },
        {
          icon: activo ? PowerOff : Power,
          label: `${activo ? "Desactivar" : "Activar"} ${office.name}`,
          onClick: () => setStatusTarget(office),
          tone: activo ? "danger" : "default",
        },
      ];
    },
    [onCreateTenant, router],
  );

  return (
    <div className="flex flex-col gap-4">
      <label className="relative block max-w-md">
        <span className="sr-only">Buscar organismo de tránsito</span>
        <Search
          className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 opacity-50"
          aria-hidden="true"
        />
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Buscar por nombre o código…"
          className="w-full rounded-xl border py-2 pl-9 pr-3 text-sm"
        />
      </label>

      <UiStateBoundary
        status={listStatus}
        onRetry={() => void load()}
        emptyMessage={
          search.trim()
            ? "No hay organismos de tránsito que coincidan con la búsqueda."
            : "No hay organismos de tránsito en el catálogo."
        }
        errorMessage="No se pudo cargar el catálogo de organismos de tránsito."
      >
        <div className="overflow-x-auto rounded-xl border">
          <table className="w-full min-w-[36rem] text-left text-sm">
            <thead>
              <tr className="border-b text-xs font-semibold uppercase tracking-wide opacity-70">
                <th className="px-4 py-3" scope="col">
                  Código
                </th>
                <th className="px-4 py-3" scope="col">
                  Organismo
                </th>
                <th className="px-4 py-3" scope="col">
                  Departamento
                </th>
                <th className="px-4 py-3" scope="col">
                  Estado
                </th>
                <th className="px-4 py-3 text-right" scope="col">
                  Acción
                </th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((office) => (
                <tr
                  key={office.id}
                  className="border-b last:border-b-0 hover:bg-slate-50/80"
                >
                  <td className="px-4 py-3 font-mono text-xs">{office.code}</td>
                  <td className="px-4 py-3 font-medium">{office.name}</td>
                  <td className="px-4 py-3 text-xs opacity-80">{office.departmentCode}</td>
                  <td className="px-4 py-3">
                    <EstadoBadge office={office} />
                  </td>
                  <td className="px-4 py-3 text-right">
                    <RowActions actions={rowActions(office)} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

      {statusTarget && (
        <TransitOfficeStatusDialog
          office={statusTarget}
          onClose={() => setStatusTarget(null)}
          onConfirmed={(nextActivo) => {
            setOffices((current) =>
              current.map((o) =>
                o.id === statusTarget.id ? { ...o, estadoActivo: nextActivo } : o,
              ),
            );
            show(
              `Organismo de tránsito «${statusTarget.name}» ${
                nextActivo ? "activado" : "desactivado"
              }.`,
              "success",
            );
            setStatusTarget(null);
          }}
        />
      )}
    </div>
  );
}

/** Badge de estado operativo: Sin alta | Activo | Inactivo (texto + color, WCAG AA). */
function EstadoBadge({ office }: { office: TransitOfficeOperationalStatus }) {
  const { label, color, bg } = !office.hasTenant
    ? { label: "Sin alta", color: "#475569", bg: "#E2E8F0" }
    : office.estadoActivo === true
      ? { label: "Activo", color: "#0F766E", bg: "#CCFBF1" }
      : { label: "Inactivo", color: "#B91C1C", bg: "#FEE2E2" };

  return (
    <span
      className="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold"
      style={{ color, backgroundColor: bg }}
    >
      {label}
    </span>
  );
}
