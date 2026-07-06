"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchImprontasHistorial } from "@/lib/api/admin-improntas";
import type { ImprontaHistorialItem } from "@/lib/api/types-improntas";
import { ImprontaHistorialTable } from "./ImprontaHistorialTable";
import { IMPRONTA_FILTER_FORM_CLS, IMPRONTA_INPUT_CLS } from "./improntas-form-styles";
import { improntaDateFromToIso, improntaDateToToIso } from "./improntas-nav";

const PAGE_SIZE = 20;

interface ImprontaFilters {
  placa: string;
  dateFrom: string;
  dateTo: string;
}

const EMPTY_FILTERS: ImprontaFilters = { placa: "", dateFrom: "", dateTo: "" };

/**
 * Vista de historial de improntas generadas (HU #10470). Filtra por placa y rango de
 * fecha, con los 4 estados de UI obligatorios: cargando (skeleton de `UiStateBoundary`),
 * error específico (AC2, sin datos crudos del backend), vacío explícito distinto entre
 * "sin filtro" y "sin resultados para este filtro" (AC3), y tabla con datos (AC1).
 * Mismo patrón que `ClientProceduresSection` (transit-offices): filtros controlados +
 * `fetchImprontasHistorial` server-side + tabla paginada. `load` recibe los filtros como
 * argumento (en vez de leerlos de un closure memoizado) para evitar refetch en cada
 * tecleo y a la vez evitar valores obsoletos: el filtro solo se aplica explícitamente
 * al enviar el formulario (AC3 "When aplica el filtro"), nunca en tiempo real.
 */
export function ImprontaHistorialSection() {
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<ImprontaHistorialItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);

  const [placaInput, setPlacaInput] = useState("");
  const [dateFromInput, setDateFromInput] = useState("");
  const [dateToInput, setDateToInput] = useState("");
  const [appliedFilters, setAppliedFilters] = useState<ImprontaFilters>(EMPTY_FILTERS);

  const load = useCallback(
    async (filters: ImprontaFilters, targetPage: number, signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchImprontasHistorial(
          {
            placa: filters.placa.trim() || undefined,
            dateFrom: improntaDateFromToIso(filters.dateFrom),
            dateTo: improntaDateToToIso(filters.dateTo),
            page: targetPage,
            pageSize: PAGE_SIZE,
          },
          signal,
        );
        if (signal?.aborted) {
          return;
        }
        setRows(result.data);
        setTotalCount(result.totalCount);
        setPage(result.page);
        setStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- recarga al cambiar de página, con los filtros ya aplicados
    void load(appliedFilters, page, controller.signal);
    return () => controller.abort();
  }, [load, page, appliedFilters]);

  const applyFilters = () => {
    const next: ImprontaFilters = { placa: placaInput, dateFrom: dateFromInput, dateTo: dateToInput };
    setAppliedFilters(next);
    setPage(1);
    void load(next, 1);
  };

  const hasActiveFilter =
    appliedFilters.placa.trim() !== "" || appliedFilters.dateFrom !== "" || appliedFilters.dateTo !== "";

  return (
    <div className="flex flex-1 flex-col gap-4">
      <form
        className={IMPRONTA_FILTER_FORM_CLS}
        onSubmit={(e) => {
          e.preventDefault();
          applyFilters();
        }}
        aria-label="Filtros del historial de improntas"
      >
        <label className="text-xs font-semibold" style={{ color: "#162744" }} htmlFor="impronta-historial-placa">
          Placa
          <input
            id="impronta-historial-placa"
            type="text"
            className={`mt-1 uppercase ${IMPRONTA_INPUT_CLS}`}
            value={placaInput}
            onChange={(e) => setPlacaInput(e.target.value.toUpperCase())}
            placeholder="ABC123"
          />
        </label>
        <label className="text-xs font-semibold" style={{ color: "#162744" }} htmlFor="impronta-historial-desde">
          Desde
          <input
            id="impronta-historial-desde"
            type="date"
            className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
            value={dateFromInput}
            onChange={(e) => setDateFromInput(e.target.value)}
          />
        </label>
        <label className="text-xs font-semibold" style={{ color: "#162744" }} htmlFor="impronta-historial-hasta">
          Hasta
          <input
            id="impronta-historial-hasta"
            type="date"
            className={`mt-1 ${IMPRONTA_INPUT_CLS}`}
            value={dateToInput}
            onChange={(e) => setDateToInput(e.target.value)}
          />
        </label>
        <div className="flex items-end">
          <button
            type="submit"
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            Aplicar filtros
          </button>
        </div>
      </form>

      <UiStateBoundary
        status={status}
        onRetry={() => void load(appliedFilters, page)}
        skeletonRows={5}
        emptyMessage={
          hasActiveFilter
            ? "Sin resultados para este filtro."
            : "Aún no hay improntas generadas para este tenant."
        }
        errorMessage="No se pudo cargar el historial de improntas. Intenta nuevamente."
      >
        <ImprontaHistorialTable
          rows={rows}
          totalCount={totalCount}
          page={page}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
        />
      </UiStateBoundary>
    </div>
  );
}
