"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Search } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import type { TransitOffice } from "@/lib/api/types";
import { matchesOtOfficeSearch, otHubModulePath } from "@/components/admin/transit-offices/ot-nav";

/** Listado de organismos de tránsito con búsqueda (HU #10236 AC1). */
export function TransitOfficesList() {
  const router = useRouter();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [search, setSearch] = useState("");

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const data = await fetchTransitOffices(undefined, signal);
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
          style={{ borderColor: "#DFE5ED" }}
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
        <div className="overflow-x-auto rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
          <table className="w-full min-w-[32rem] text-left text-sm">
            <thead>
              <tr className="border-b text-xs font-semibold uppercase tracking-wide opacity-70" style={{ borderColor: "#DFE5ED" }}>
                <th className="px-4 py-3" scope="col">
                  Código
                </th>
                <th className="px-4 py-3" scope="col">
                  Organismo
                </th>
                <th className="px-4 py-3" scope="col">
                  Departamento
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
                  style={{ borderColor: "#DFE5ED" }}
                >
                  <td className="px-4 py-3 font-mono text-xs">{office.code}</td>
                  <td className="px-4 py-3 font-medium">{office.name}</td>
                  <td className="px-4 py-3 text-xs opacity-80">{office.departmentCode}</td>
                  <td className="px-4 py-3 text-right">
                    <button
                      type="button"
                      onClick={() => router.push(otHubModulePath(office.id, "tramites"))}
                      className="rounded-lg px-3 py-1.5 text-xs font-semibold text-white"
                      style={{ background: "#557EFF" }}
                    >
                      Administrar
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>
    </div>
  );
}
