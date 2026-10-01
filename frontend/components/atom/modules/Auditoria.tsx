'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { ModuleTitle } from './ModuleTitle';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { Pagination } from '@/components/atom/Pagination';
import { UiStateBoundary } from '@/components/admin/UiStateBoundary';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { usePaginacion } from '@/components/atom/usePaginacion';
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from '@/components/atom/table-styles';
import {
  AuditoriaFilterToolbar,
  EMPTY_AUDITORIA_FILTERS,
  hasActiveAuditoriaFilters,
  type AuditoriaUiFilters,
} from './AuditoriaFilterToolbar';
import { fetchAdminAuditLog } from '@/lib/api/audit';
import type { AdminAuditLogEntry, AdminAuditLogQuery, AdminAuditModule, AdminAuditTenantType } from '@/lib/api/types';

import { formatFechaHora } from '@/lib/format/date';
/**

 * Módulo "Auditoría" (HU #10680). Pantalla SuperAdmin-only, montada DENTRO del Shell SPA
 * (nunca aislada), que consulta el rastro unificado de auditoría administrativa/seguridad
 * — GET /api/v1/superadmin/audit (HU #10679) — con filtros server-side y paginación.
 *
 * Calcado del patrón de "Validaciones de Identidad" (Validaciones.tsx): paginación
 * server-side, dos capas de filtros (`filters` UI vs `applied` consultado — selects/fechas
 * inmediato, texto con debounce ~300ms), guard anti-race (`reqIdRef`) y los 4 estados de UI
 * (cargando/error/vacío/lleno) WCAG 2.1 AA vía `UiStateBoundary`.
 */

const MODULE_LABEL: Record<AdminAuditModule, string> = {
  users: 'Usuarios',
  roles: 'Roles',
  permissions: 'Permisos',
  authentication: 'Autenticación',
  security: 'Seguridad',
  config: 'Configuración',
  tramites: 'Trámites',
  mandatarios: 'Mandatarios',
};

const TENANT_TYPE_LABEL: Record<AdminAuditTenantType, string> = {
  COMPANY: 'Compañía gestora',
  TRANSIT_OFFICE: 'Organismo de tránsito',
};

/** Formatea una fecha ISO a texto legible (es-CO), con fecha y hora. */
function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return formatFechaHora(d);
}

/** Acorta un uuid a sus primeros 8 caracteres para no romper el layout de la tabla. */
function shortId(id: string | null | undefined): string {
  if (!id) return '—';
  return id.length > 8 ? `${id.slice(0, 8)}…` : id;
}

/**
 * Convierte los filtros de la UI (strings controlados) a los query params del backend
 * (HU #10679): vacíos → undefined (no se envían), fechas a ISO (dateFrom a inicio de día,
 * dateTo a fin de día para incluir el día elegido completo).
 */
function buildApiFilters(f: AuditoriaUiFilters): Omit<AdminAuditLogQuery, 'page' | 'pageSize'> {
  const text = (s: string) => (s.trim() === '' ? undefined : s.trim());
  return {
    userId: text(f.userId),
    tenantId: text(f.tenantId),
    tenantType: f.tenantType || undefined,
    module: f.module || undefined,
    operation: text(f.operation),
    result: f.result || undefined,
    dateFrom: f.dateFrom ? `${f.dateFrom}T00:00:00` : undefined,
    dateTo: f.dateTo ? `${f.dateTo}T23:59:59` : undefined,
  };
}

/** Bug #13055 — tamaño inicial dentro del estándar de filas por página (10/25/50/100). */
const DEFAULT_PAGE_SIZE = 10;

export function Auditoria() {
  const [entries, setEntries] = useState<AdminAuditLogEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);

  // Paginación server-side.
  const { page, pageSize, setPage, setPageSize } = usePaginacion(DEFAULT_PAGE_SIZE);
  const [total, setTotal] = useState(0);
  // Refs "latest value": se sincronizan en un efecto (nunca escribiendo `.current` en
  // render, para no violar las reglas de refs de React) y se leen dentro de `load` para
  // que la petición en vuelo siempre tome la página/tamaño/filtros vigentes.
  const pageRef = useRef(page);
  const pageSizeRef = useRef(pageSize);
  useEffect(() => {
    pageRef.current = page;
    pageSizeRef.current = pageSize;
  }, [page, pageSize]);

  // `filters` = controles de la UI (instantáneos); `applied` = lo que se consulta al
  // backend. Los selects y fechas aplican de inmediato; los inputs de texto (usuario,
  // tenant, operación) aplican tras un debounce (~300 ms).
  const [filters, setFilters] = useState<AuditoriaUiFilters>(EMPTY_AUDITORIA_FILTERS);
  const [applied, setApplied] = useState<AuditoriaUiFilters>(EMPTY_AUDITORIA_FILTERS);
  const filtersRef = useRef(filters);
  useEffect(() => {
    filtersRef.current = filters;
  }, [filters]);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Guard anti-race: solo se aplica el resultado de la petición más reciente.
  const reqIdRef = useRef(0);

  const load = useCallback(async (uiFilters: AuditoriaUiFilters) => {
    const reqId = ++reqIdRef.current;
    setFetching(true);
    try {
      const res = await fetchAdminAuditLog({
        ...buildApiFilters(uiFilters),
        page: pageRef.current,
        pageSize: pageSizeRef.current,
      });
      if (reqId !== reqIdRef.current) return; // respuesta obsoleta (llegó otra consulta después)
      setEntries(res.data);
      setTotal(res.totalCount);
      setError(null);
    } catch (err) {
      if (reqId !== reqIdRef.current) return;
      setError(err instanceof Error ? err.message : 'No se pudo cargar la auditoría.');
    } finally {
      if (reqId === reqIdRef.current) {
        setFetching(false);
        setHasLoadedOnce(true);
      }
    }
  }, []);

  // Refetch cuando cambian los filtros aplicados o la página/tamaño (carga inicial incluida).
  useEffect(() => {
    // Sincroniza con el backend (fuente externa), no deriva estado de props/state ya
    // disponibles en render.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(applied);
  }, [applied, page, pageSize, load]);

  // Limpia el timer del debounce al desmontar.
  useEffect(
    () => () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    },
    [],
  );

  const applyChange = useCallback((patch: Partial<AuditoriaUiFilters>, immediate?: boolean) => {
    const next = { ...filtersRef.current, ...patch };
    setFilters(next);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (immediate) {
      setApplied(next);
      setPage(1); // un filtro nuevo vuelve a la primera página
    } else {
      debounceRef.current = setTimeout(() => {
        setApplied(next);
        setPage(1);
      }, 300);
    }
  }, [setPage]);

  const handleRefresh = useCallback(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    void load(filtersRef.current);
  }, [load]);

  const handleClearFilters = useCallback(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setFilters(EMPTY_AUDITORIA_FILTERS);
    setApplied(EMPTY_AUDITORIA_FILTERS);
    setPage(1);
  }, [setPage]);

  const handlePageChange = useCallback((p: number) => setPage(Math.max(1, p)), [setPage]);

  // 4 estados de UI. La carga inicial (skeleton) solo aplica antes de la primera respuesta.
  const initialLoading = !hasLoadedOnce && entries === null && error === null;
  const isEmpty = entries !== null && entries.length === 0;
  // "Sin resultados" (con filtros) vs "Aún no hay auditoría" se decide por los filtros
  // EFECTIVAMENTE aplicados (no por los controles a medio escribir).
  const filtersActive = hasActiveAuditoriaFilters(applied);
  // Fallo en la carga INICIAL (sin datos previos que mostrar) → bloque de error completo.
  const initialLoadFailed = error !== null && entries === null;
  // Fallo en un refetch posterior CON datos ya en pantalla → banner no bloqueante (AC8):
  // no se pierde la última vista válida por un fallo transitorio de red.
  const staleDataError = error !== null && entries !== null;

  const status: 'loading' | 'error' | 'empty' | 'ready' = initialLoading
    ? 'loading'
    : initialLoadFailed
      ? 'error'
      : isEmpty
        ? 'empty'
        : 'ready';

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="Auditoría"
        subtitle="Rastro global de operaciones administrativas y de seguridad: usuarios, roles, permisos y autenticación."
      />

      {hasLoadedOnce && (
        <AuditoriaFilterToolbar
          filters={filters}
          onChange={applyChange}
          onRefresh={handleRefresh}
          onClearFilters={handleClearFilters}
          loading={fetching}
          resultCount={entries?.length ?? 0}
        />
      )}

      {staleDataError && (
        <div
          className="rounded-2xl p-4 border text-xs flex items-start gap-3 shrink-0"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          <div className="space-y-2">
            <p className="font-semibold">No se pudo actualizar la auditoría.</p>
            <p className="opacity-80">{error}</p>
            <button
              type="button"
              onClick={handleRefresh}
              className="px-3 py-1.5 rounded-lg text-[11px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ background: '#FF4E00' }}
            >
              Reintentar
            </button>
          </div>
        </div>
      )}

      {/* Bug #13055 — carga con el loader del carrito; vacío/error con UiStateBoundary. */}
      {status === 'loading' && <CarLoaderModal label="Cargando auditoría…" />}
      <UiStateBoundary
        status={status === 'loading' ? 'ready' : status}
        errorMessage={error ?? 'No se pudo cargar la auditoría.'}
        onRetry={handleRefresh}
        emptyMessage={
          filtersActive
            ? 'Ningún registro de auditoría coincide con los filtros aplicados. Ajusta o limpia los filtros para ver más resultados.'
            : 'Aún no hay registros de auditoría.'
        }
      >
        {entries && entries.length > 0 && (
          <>
            <AuditoriaTable rows={entries} />
            <Pagination
              page={page}
              pageSize={pageSize}
              totalCount={total}
              onPageChange={handlePageChange}
              onPageSizeChange={setPageSize}
              noun="registros de auditoría"
            />
          </>
        )}
      </UiStateBoundary>
    </div>
  );
}

/**
 * Bug #13055 — tabla homologada con el modelo de trámites: `<table>` semántica (antes una grilla de
 * div) con la cabecera #DFE5ED fija y filas-tarjeta de `table-styles`.
 * Columnas: Fecha/hora, Módulo, Operación, Resultado, Actor, Afectado, Tipo tenant, IP.
 */
const COLUMNAS = ['Fecha y hora', 'Módulo', 'Operación', 'Resultado', 'Actor', 'Afectado', 'Tipo tenant', 'IP'];
const CELDA = 'border-y px-4 py-3';
const CELDA_STYLE = { borderColor: '#DFE5ED' };

function AuditoriaTable({ rows }: { rows: AdminAuditLogEntry[] }) {
  return (
    // Scroll horizontal en pantallas angostas. `shrink-0` evita que el flex del módulo
    // colapse el contenedor de scroll a casi nada.
    <div className="overflow-x-auto shrink-0">
      <table
        aria-label="Registros de auditoría"
        className="text-xs"
        style={{ width: '100%', borderCollapse: 'separate', borderSpacing: '0 8px', minWidth: 960 }}
      >
        <thead>
          <tr>
            {COLUMNAS.map((col, i) => (
              <th
                key={col}
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} ${i === 0 ? 'rounded-l-xl' : ''} ${i === COLUMNAS.length - 1 ? 'rounded-r-xl' : ''}`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                {col}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <AuditoriaRow key={r.id} row={r} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Epic #12543 — en una aceptación de T&C lo que importa es el tipo de trámite, que viaja en el
 * detalle (`newValue.procedureTypeCode`) y no en `targetEntityId` (ese es el id de la evidencia).
 * Sin esto la columna «Afectado» mostraba `PROCEDURE_TERMS_ACCEPTANCE · 87f201c9…`, que no le
 * dice nada a quien audita.
 */
function tipoDeTramiteAceptado(r: AdminAuditLogEntry): string | null {
  if (r.operation !== 'accept_terms' || !r.newValue) return null;
  try {
    const detalle = JSON.parse(r.newValue) as { procedureTypeCode?: unknown };
    return typeof detalle.procedureTypeCode === 'string' && detalle.procedureTypeCode ? detalle.procedureTypeCode : null;
  } catch {
    return null;
  }
}

function AuditoriaRow({ row: r }: { row: AdminAuditLogEntry }) {
  const moduleLabel = r.module ? (MODULE_LABEL[r.module] ?? r.module) : '—';
  const tenantTypeLabel = r.tenantType ? (TENANT_TYPE_LABEL[r.tenantType] ?? r.tenantType) : '—';
  const isSuccess = r.result === 'success';
  const isFailure = r.result === 'failure';
  const resultLabel = isSuccess ? 'Éxito' : isFailure ? 'Fallo' : '—';
  const tramiteAceptado = tipoDeTramiteAceptado(r);
  const afectado = tramiteAceptado
    ? `Trámite · ${tramiteAceptado}`
    : r.targetEntityType
      ? `${r.targetEntityType} · ${shortId(r.targetEntityId)}`
      : r.targetEntityId
        ? shortId(r.targetEntityId)
        : '—';
  const ariaLabel =
    `Registro de auditoría del ${formatFecha(r.changedAt)}, módulo ${moduleLabel}, ` +
    `operación ${r.operation ?? 'sin especificar'}, resultado ${resultLabel}` +
    (r.errorCode ? `, código de error ${r.errorCode}` : '') +
    `, actor ${shortId(r.changedBy)}, afectado ${afectado}, tenant ${tenantTypeLabel}, IP ${r.clientIp ?? 'no disponible'}.`;

  return (
    <tr
      aria-label={ariaLabel}
      className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}
    >
      <td className={`${CELDA} rounded-l-xl border-l text-[10px] leading-tight`} style={CELDA_STYLE}>
        <span className="opacity-80">{formatFecha(r.changedAt)}</span>
      </td>
      <td className={CELDA} style={CELDA_STYLE}>{moduleLabel}</td>
      <td className={`${CELDA} font-mono text-[11px]`} style={CELDA_STYLE}>{r.operation ?? '—'}</td>
      <td className={CELDA} style={CELDA_STYLE}>
        {r.result ? (
          <StatusBadge
            label={resultLabel}
            tone={isSuccess ? 'success' : 'danger'}
            ariaLabel={`Resultado: ${resultLabel}`}
          />
        ) : (
          <span className="opacity-60">—</span>
        )}
        {isFailure && r.errorCode && (
          <span className="mt-0.5 block text-[10px] opacity-70 truncate" title={r.errorCode}>
            {r.errorCode}
          </span>
        )}
      </td>
      <td className={`${CELDA} font-mono text-[11px]`} style={CELDA_STYLE} title={r.changedBy ?? undefined}>
        {shortId(r.changedBy)}
      </td>
      <td className={`${CELDA} font-mono text-[11px]`} style={CELDA_STYLE} title={r.targetEntityId ?? undefined}>
        {afectado}
      </td>
      <td className={CELDA} style={CELDA_STYLE}>{tenantTypeLabel}</td>
      <td className={`${CELDA} rounded-r-xl border-r font-mono text-[11px]`} style={CELDA_STYLE}>
        {r.clientIp ?? '—'}
      </td>
    </tr>
  );
}
