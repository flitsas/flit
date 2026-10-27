"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { AuditLogTable } from "@/components/admin/companies/AuditLogTable";
import {
  alternativasParaValor,
  clavesParaCampo,
} from "@/components/admin/companies/auditLogLabels";
import { fetchAuditLog, type AuditLogFilters } from "@/lib/api/admin-companies";
import { ApiValidationError, type AuditLogPageResponse } from "@/lib/api/types";
import { condicionesAParams, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";

// Filtros por columna del historial (HU #14005, Feature #13996). Los resuelve el SERVIDOR (HU #14004):
// Fecha = Periodo (fechaDesde/fechaHasta), el resto «contiene». El panel de fecha del embudo valida
// Desde ≤ Hasta. «Valor nuevo» no tiene filtro porque el endpoint no lo expone.
const COLUMNAS: ColumnaFiltro[] = [
  {
    id: "fecha",
    label: "Fecha",
    tipo: "fecha",
    paramDesde: "fechaDesde",
    paramHasta: "fechaHasta",
  },
  { id: "campo", label: "Campo modificado", tipo: "texto" },
  { id: "valorAnterior", label: "Valor anterior", tipo: "texto" },
  { id: "operador", label: "Operador", tipo: "texto" },
];

// Slot del historial de auditoría (HU #10194, AC5). Se monta al abrir la pestaña
// "Historial de Cambios" → carga diferida. Paginación y filtros server-side.
export function AuditLogPanel({
  tenantId,
  networkHeadId,
}: {
  tenantId: string;
  networkHeadId?: string | null;
}) {
  // Bug #13055 — tabla homologada con el modelo de trámites: filas por página elegibles.
  const { page, pageSize, setPage, setPageSize } = usePaginacion("panels.AuditLogPanel");
  const [status, setStatus] = useState<UiStatus>("loading");
  const [result, setResult] = useState<AuditLogPageResponse | null>(null);
  const [errorFiltro, setErrorFiltro] = useState<string | null>(null);

  const volverAPrimera = useCallback(() => setPage(1), [setPage]);
  const filtros = useTablaFiltros({
    columnas: COLUMNAS,
    onCambio: volverAPrimera,
  });

  const params = useMemo(() => {
    const base = condicionesAParams(filtros.condicionesTotales, COLUMNAS) as AuditLogFilters;
    // La API filtra el valor técnico; el usuario escribe lo que ve (etiquetas traducidas).
    const claves = base.campo ? clavesParaCampo(base.campo) : [];
    const alternativas = base.valorAnterior ? alternativasParaValor(base.valorAnterior) : [];
    return {
      ...base,
      ...(claves.length > 0 ? { campoClaves: claves.join(",") } : {}),
      ...(alternativas.length > 0 ? { valorAnteriorAlternativas: alternativas.join(",") } : {}),
    } as AuditLogFilters;
  }, [filtros.condicionesTotales]);
  // Clave estable: la identidad del objeto cambia en cada render del hook, su contenido no.
  const paramsKey = JSON.stringify(params);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      setErrorFiltro(null);
      try {
        const data = await fetchAuditLog(
          tenantId,
          page,
          pageSize,
          signal,
          networkHeadId,
          JSON.parse(paramsKey) as AuditLogFilters,
        );
        if (signal?.aborted) {
          return;
        }
        setResult(data);
        setStatus(data.data.length === 0 ? "empty" : "ready");
      } catch (err) {
        if (signal?.aborted) return;
        if (err instanceof ApiValidationError && err.errors.length > 0) {
          // 422 del rango (u otro filtro): se muestra su mensaje y se conserva lo ya cargado.
          setErrorFiltro(err.errors.map((e) => e.message).join(" "));
          setStatus(result ? (result.data.length === 0 ? "empty" : "ready") : "error");
          return;
        }
        setStatus("error");
      }
    },
    // `result` solo se lee para restaurar el estado tras un 422: no debe re-disparar la carga.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [tenantId, page, pageSize, networkHeadId, paramsKey],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial de datos al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // Primera carga: solo el loader. Al paginar/filtrar se conserva la barra montada.
  if (status === "loading" && !result) {
    return <CarLoaderModal label="Cargando historial de cambios…" />;
  }

  return (
    <div className="flex flex-col gap-3">
      {status === "loading" ? <CarLoaderModal label="Cargando historial de cambios…" /> : null}
      {errorFiltro ? (
        <p role="alert" className="text-xs font-semibold text-[#C2410C]">
          {errorFiltro}
        </p>
      ) : null}
      {
        // Vacío por filtros: la tabla sigue montada (encabezados con embudos y filtros de arriba);
        // el cuerpo muestra el aviso con «Limpiar filtros» (emptyContent de AuditLogTable).
        status === "empty" && filtros.hayFiltros && result ? (
          <AuditLogTable
            entries={result.data}
            totalCount={result.totalCount}
            page={result.page}
            pageSize={result.pageSize}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
            filtros={filtros}
          />
        ) : (
        <UiStateBoundary
          status={status === "loading" ? "ready" : status}
          onRetry={() => void load()}
          emptyMessage="Aún no hay cambios registrados para esta compañía."
          errorMessage="No se pudo cargar el historial de auditoría."
        >
          {result && (
            <AuditLogTable
              entries={result.data}
              totalCount={result.totalCount}
              page={result.page}
              pageSize={result.pageSize}
              onPageChange={setPage}
              onPageSizeChange={setPageSize}
              filtros={filtros}
            />
          )}
        </UiStateBoundary>
      )}
    </div>
  );
}
