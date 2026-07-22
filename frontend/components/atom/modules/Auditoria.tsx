'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { ModuleTitle } from './ModuleTitle';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { Pagination } from '@/components/atom/Pagination';
import { UiStateBoundary } from '@/components/admin/UiStateBoundary';
import {
  AuditoriaFilterToolbar,
  EMPTY_AUDITORIA_FILTERS,
  hasActiveAuditoriaFilters,
  type AuditoriaUiFilters,
} from './AuditoriaFilterToolbar';
import { fetchAdminAuditLog } from '@/lib/api/audit';
import type { AdminAuditLogEntry, AdminAuditLogQuery, AdminAuditModule, AdminAuditTenantType } from '@/lib/api/types';

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
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
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

/** Opciones de filas por página. */
const PAGE_SIZE_OPTIONS = [10, 20, 30, 50, 100];
const DEFAULT_PAGE_SIZE = 20;

export function Auditoria() {
  const [entries, setEntries] = useState<AdminAuditLogEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);

  // Paginación server-side.
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
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
  }, []);

  const handleRefresh = useCallback(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    void load(filtersRef.current);
  }, [load]);

  const handleClearFilters = useCallback(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setFilters(EMPTY_AUDITORIA_FILTERS);
    setApplied(EMPTY_AUDITORIA_FILTERS);
    setPage(1);
  }, []);

  const handlePageChange = useCallback((p: number) => setPage(Math.max(1, p)), []);
  const handlePageSizeChange = useCallback((size: number) => {
    setPageSize(size);
    setPage(1); // cambiar el tamaño reinicia a la primera página
  }, []);

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

      <UiStateBoundary
        status={status}
        skeletonRows={6}
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
            <div className="flex flex-wrap items-center justify-between gap-3 shrink-0">
              <label className="flex items-center gap-1.5 text-[11px]">
                <span className="opacity-60">Filas por página</span>
                <select
                  value={pageSize}
                  onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                  disabled={fetching}
                  aria-label="Filas por página"
                  className="rounded-lg border bg-white px-2 py-1 text-xs outline-none focus:border-[#557EFF] disabled:opacity-50 dark:bg-[#0B0F14]"
                >
                  {PAGE_SIZE_OPTIONS.map((n) => (
                    <option key={n} value={n}>
                      {n}
                    </option>
                  ))}
                </select>
              </label>
              <Pagination page={page} pageSize={pageSize} totalCount={total} onPageChange={handlePageChange} className="mt-0 justify-end" />
            </div>
          </>
        )}
      </UiStateBoundary>
    </div>
  );
}

/**
 * Plantilla de columnas compartida por cabecera y filas: Fecha/hora, Módulo, Operación,
 * Resultado, Actor, Afectado, Tipo tenant, IP.
 */
const GRID_COLS =
  'minmax(0,1.3fr) minmax(0,0.9fr) minmax(0,1fr) minmax(0,0.8fr) minmax(0,1fr) minmax(0,1.2fr) minmax(0,1.1fr) minmax(0,0.9fr)';

function AuditoriaTable({ rows }: { rows: AdminAuditLogEntry[] }) {
  return (
    // Scroll horizontal en pantallas angostas. `shrink-0` evita que el flex del módulo
    // colapse el contenedor de scroll a casi nada.
    <div className="overflow-x-auto shrink-0">
      <div className="min-w-[960px]">
        {/* Cabecera decorativa: el lector de pantalla lee el aria-label completo de cada fila. */}
        <div
          className="sticky top-0 z-10 grid gap-2 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl"
          style={{ background: '#DFE5ED', color: '#162744', gridTemplateColumns: GRID_COLS }}
          aria-hidden="true"
        >
          <div>Fecha y hora</div>
          <div>Módulo</div>
          <div>Operación</div>
          <div>Resultado</div>
          <div>Actor</div>
          <div>Afectado</div>
          <div>Tipo tenant</div>
          <div>IP</div>
        </div>
        <ul className="space-y-2 pt-2" aria-label="Registros de auditoría">
          {rows.map((r) => (
            <AuditoriaRow key={r.id} row={r} />
          ))}
        </ul>
      </div>
    </div>
  );
}

function AuditoriaRow({ row: r }: { row: AdminAuditLogEntry }) {
  const moduleLabel = r.module ? (MODULE_LABEL[r.module] ?? r.module) : '—';
  const tenantTypeLabel = r.tenantType ? (TENANT_TYPE_LABEL[r.tenantType] ?? r.tenantType) : '—';
  const isSuccess = r.result === 'success';
  const isFailure = r.result === 'failure';
  const resultLabel = isSuccess ? 'Éxito' : isFailure ? 'Fallo' : '—';
  const afectado = r.targetEntityType
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
    <li>
      <div
        role="row"
        aria-label={ariaLabel}
        className="grid gap-2 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs"
        style={{ gridTemplateColumns: GRID_COLS }}
      >
        <div className="min-w-0 text-[10px] leading-tight opacity-80">{formatFecha(r.changedAt)}</div>
        <div className="min-w-0 truncate">{moduleLabel}</div>
        <div className="min-w-0 truncate font-mono text-[11px]">{r.operation ?? '—'}</div>
        <div className="min-w-0">
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
        </div>
        <div className="min-w-0 truncate font-mono text-[11px]" title={r.changedBy ?? undefined}>
          {shortId(r.changedBy)}
        </div>
        <div className="min-w-0 truncate font-mono text-[11px]" title={r.targetEntityId ?? undefined}>
          {afectado}
        </div>
        <div className="min-w-0 truncate">{tenantTypeLabel}</div>
        <div className="min-w-0 truncate font-mono text-[11px]">{r.clientIp ?? '—'}</div>
      </div>
    </li>
  );
}
