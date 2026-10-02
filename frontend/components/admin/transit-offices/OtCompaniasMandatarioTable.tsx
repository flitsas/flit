"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, Pencil } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { RowActionsMenu } from "@/components/atom/RowActionsMenu";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  fetchOtAssociableCompanies,
  type AssociableCompaniesPage,
  type AssociableCompany,
} from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";

/** El servidor rechaza búsquedas de menos de 2 caracteres (422): no se envían. */
export const MIN_BUSQUEDA_COMPANIAS = 2;
const DEBOUNCE_MS = 300;

/**
 * HU #13182 — compañías activas del hub del OT, con búsqueda por nombre y NIT. Solo se usa en la
 * sección de mandatarios: la bandeja de trámites y las métricas conservan su visibilidad. Solo
 * nombre y NIT (Ley 1581). Paginación de servidor con «Filas por página».
 */
export function OtCompaniasMandatarioTable({
  transitOfficeId,
  signerNameOf,
  sinMandatarioOf,
  onEdit,
}: {
  transitOfficeId: string;
  /** Mandatario por defecto ya configurado para la compañía (vacío si no tiene regla). */
  signerNameOf: (companyTenantId: string) => string | null | undefined;
  /** HU #13139 — la compañía no tiene mandatario propio, vinculado ni general al que recurrir. */
  sinMandatarioOf?: (companyTenantId: string) => boolean;
  onEdit: (company: AssociableCompany) => void;
}) {
  const pg = usePaginacion();
  const { page, pageSize, setPage } = pg;
  const [entrada, setEntrada] = useState("");
  const [termino, setTermino] = useState("");
  const [data, setData] = useState<AssociableCompaniesPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reintento, setReintento] = useState(0);
  const primera = useRef(true);
  const terminoRef = useRef("");

  // Debounce: solo consulta con 0 (todas) o ≥ 2 caracteres.
  useEffect(() => {
    const t = entrada.trim();
    if (t.length > 0 && t.length < MIN_BUSQUEDA_COMPANIAS) return;
    const id = setTimeout(
      () => {
        if (t !== terminoRef.current) {
          terminoRef.current = t;
          setPage(1);
          setTermino(t);
        }
      },
      primera.current ? 0 : DEBOUNCE_MS,
    );
    primera.current = false;
    return () => clearTimeout(id);
  }, [entrada, setPage]);

  useEffect(() => {
    const ctrl = new AbortController();
    // Carga al montar / cambiar filtros: estado de red vive en este componente.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch al cambiar página, filtro o reintento
    setLoading(true);
    setError(null);
    void (async () => {
      try {
        const res = await fetchOtAssociableCompanies(
          transitOfficeId,
          { search: termino || undefined, page, pageSize },
          ctrl.signal,
        );
        if (!ctrl.signal.aborted) setData(res);
      } catch (err) {
        if (ctrl.signal.aborted) return;
        setData(null);
        setError(
          err instanceof ApiError && err.status === 403
            ? "No tienes permiso para ver las compañías."
            : "No se pudieron cargar las compañías.",
        );
      } finally {
        if (!ctrl.signal.aborted) setLoading(false);
      }
    })();
    return () => ctrl.abort();
  }, [transitOfficeId, termino, page, pageSize, reintento]);

  const columns: DataTableColumn<AssociableCompany>[] = useMemo(
    () => [
      { key: "nit", header: "NIT", cellClassName: "font-mono", render: (row) => row.nit || "—" },
      { key: "name", header: "Empresa", cellClassName: "font-semibold", render: (row) => row.name },
      {
        key: "signer",
        header: "Mandatario",
        render: (row) => {
          const name = signerNameOf(row.id)?.trim();
          if (!name && sinMandatarioOf?.(row.id)) {
            return (
              <StatusBadge
                tone="warning"
                ariaLabel="Sin mandatario"
                label={
                  <span className="inline-flex items-center gap-1" data-testid="ot-mandatos-sin-mandatario">
                    <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden={true} />
                    Sin mandatario
                  </span>
                }
              />
            );
          }
          return name ? (
            name
          ) : (
            <span className="text-[#59677D] dark:text-white/55">Sin definir</span>
          );
        },
      },
      {
        key: "actions",
        header: "Acción",
        align: "right",
        render: (row) => (
          <RowActionsMenu
            ariaLabel={`Acciones de ${row.name}`}
            subject={row.name}
            actions={[
              {
                icon: Pencil,
                label: `Editar mandatario de ${row.name}`,
                tone: "primary",
                onClick: () => onEdit(row),
              },
            ]}
          />
        ),
      },
    ],
    [signerNameOf, sinMandatarioOf, onEdit],
  );

  const items = data?.items ?? [];
  const status = error ? "error" : loading && !data ? "loading" : "ready";
  const buscando = termino.length > 0;
  const corta = entrada.trim().length > 0 && entrada.trim().length < MIN_BUSQUEDA_COMPANIAS;
  const reintentar = useCallback(() => setReintento((n) => n + 1), []);

  return (
    <div className="flex flex-col gap-3" data-testid="ot-mandatos-companias">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <h3 className="text-sm font-semibold text-[#162244] dark:text-white">Compañías</h3>
        <label className="min-w-[12rem] flex-1 sm:max-w-xs">
          <span className="sr-only">Buscar compañía por nombre o NIT</span>
          <input
            type="search"
            value={entrada}
            onChange={(e) => setEntrada(e.target.value)}
            placeholder="Buscar por nombre o NIT…"
            aria-describedby={corta ? "ot-mandatos-busqueda-ayuda" : undefined}
            className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162244] placeholder:text-[#59677D]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
            data-testid="ot-mandatos-company-search"
          />
        </label>
      </div>
      {corta ? (
        <p
          id="ot-mandatos-busqueda-ayuda"
          className="text-[11px] text-[#59677D] dark:text-white/65"
          data-testid="ot-mandatos-busqueda-ayuda"
        >
          Escribe al menos {MIN_BUSQUEDA_COMPANIAS} caracteres.
        </p>
      ) : null}

      <div data-testid="ot-mandatos-company-table" aria-busy={loading}>
        <DataTable
          columns={columns}
          rows={items}
          getRowKey={(row) => row.id}
          ariaLabel="Compañías activas"
          minWidth={720}
          status={status}
          errorMessage={error ?? undefined}
          onRetry={reintentar}
          emptyMessage={buscando ? "Sin resultados" : "No hay compañías activas."}
          pagination={{
            page: pg.page,
            pageSize: pg.pageSize,
            totalCount: data?.total ?? 0,
            onPageChange: pg.setPage,
            onPageSizeChange: pg.setPageSize,
          }}
        />
      </div>
    </div>
  );
}
