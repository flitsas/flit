'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import {
  AlertCircle,
  AlertTriangle,
  CheckCircle2,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Clock,
  Copy,
  ExternalLink,
  ListTree,
  RotateCcw,
  ScanFace,
  ShieldCheck,
  XCircle,
} from 'lucide-react';
import { ActionsMenu } from '@/components/atom/ActionsMenu';
import { ModuleTitle } from './ModuleTitle';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import {
  ValidacionesFilterToolbar,
  EMPTY_VALIDACIONES_FILTERS,
  hasActiveValidacionesFilters,
  type ValidacionesUiFilters,
} from './ValidacionesFilterToolbar';
import { PrevalidacionDetailDrawer } from './PrevalidacionDetailDrawer';
import { PersonIdentityDetailDrawer } from './PersonIdentityDetailDrawer';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  BiometricEstado,
  BiometricValidationStats,
  StuckIdentityValidation,
  StuckIdentityValidationsResponse,
  TenantBiometricPerson,
  TenantBiometricPersonFilters,
  TenantBiometricValidation,
  TenantBiometricValidationFilters,
} from '@/lib/api/types/procedure-runtime';

/**
 * Submódulo "Validaciones de Identidad" (HU #10234). Vista transversal del tenant: lista TODAS las
 * validaciones biométricas/de identidad (biometría + cotejo) con KPIs reales, consumiendo el endpoint
 * GET /api/v1/tramites/biometric-validations. Provider-aware (mock | kyverum). La gestión de captura
 * vive en el wizard del trámite; aquí es monitoreo + navegación al trámite de origen.
 *
 * AC8 — 4 estados de UI: Cargando (skeleton accesible), Error (role="alert"), Vacío (mensaje
 * explícito) y Lleno (KPIs + tabla). WCAG 2.1 AA: aria-labels por fila, foco visible, anuncios a
 * lectores de pantalla.
 *
 * Auto-refresco en vivo (fase 2): la grilla se actualiza sola cada AUTO_REFRESH_MS con los filtros
 * vigentes para reflejar los cambios que el backend persiste vía webhook/outbox de Kyverum, sin que el
 * gestor pulse "Actualizar" (que sigue disponible). Pausa cuando la pestaña no está visible.
 */

const ESTADO_META: Record<BiometricEstado, { label: string; tone: StatusTone }> = {
  enviado: { label: 'Enviado', tone: 'info' },
  en_proceso: { label: 'En proceso', tone: 'warning' },
  aprobado: { label: 'Aprobado', tone: 'success' },
  rechazado: { label: 'Rechazado', tone: 'danger' },
  expirado: { label: 'Expirado', tone: 'neutral' },
  pendiente_envio: { label: 'Pendiente de envío', tone: 'info' },
  error_envio: { label: 'Error de envío', tone: 'danger' },
};

const MODALIDAD_LABEL: Record<string, string> = {
  matricula_inicial: 'Matrícula inicial',
  traspaso: 'Traspaso',
};

const PROVIDER_LABEL: Record<string, string> = {
  mock: 'Simulado',
  kyverum: 'Kyverum',
};

/** Formatea una fecha ISO a texto legible (es-CO). Devuelve el ISO crudo si no parsea. */
function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

/** Formatea una fecha ISO solo a día (es-CO), sin hora. Para aprobación/expiración de la vigencia. */
function formatFechaCorta(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium' }).format(d);
}

/**
 * Enmascara el documento dejando visibles solo los últimos 4. CF-04 (Feature #11004, HU #11006)
 * retira el enmascarado de la TABLA principal de Validaciones (D3 — documento completo); este
 * helper se conserva solo para el banner de eventos atascados (observabilidad de dead-letter, fuera
 * del alcance de D3, que limita el documento/correo completos a "las tablas").
 */
function maskDoc(tipoDoc: string, documento: string): string {
  const tail = documento.length > 4 ? documento.slice(-4) : documento;
  const masked = documento.length > 4 ? `••••${tail}` : tail;
  return `${tipoDoc} ${masked}`.trim();
}

/**
 * Presentación de los días de vigencia restantes de una validación aprobada: color de urgencia
 * (verde holgado, ámbar por vencer, rojo vencida) + etiqueta. La vigencia es de 30 días desde la
 * aprobación; el backend ya calcula los días (0 = vencida). Null cuando la validación no está aprobada.
 */
function vigenciaBadge(dias: number | null): { label: string; color: string; bg: string } | null {
  if (dias == null) return null;
  if (dias <= 0) return { label: 'Vencida', color: '#E43D30', bg: 'rgba(228,61,48,0.12)' };
  const label = `${dias} día${dias === 1 ? '' : 's'}`;
  if (dias <= 7) return { label, color: '#F05A35', bg: 'rgba(249,172,0,0.16)' };
  return { label, color: '#70CF3A', bg: 'rgba(140,198,63,0.16)' };
}

/**
 * Convierte los filtros de la UI (strings controlados) a los query params del backend (HU #10347):
 * vacíos → undefined (no se envían), score a número, fechas a ISO (createdTo a fin de día para que la
 * fecha elegida quede incluida). motivoRechazo solo cuando se filtra por estado=rechazado.
 */
function buildApiFilters(f: ValidacionesUiFilters): TenantBiometricValidationFilters {
  const text = (s: string) => (s.trim() === '' ? undefined : s.trim());
  const num = (s: string) => {
    if (s.trim() === '') return undefined;
    const n = Number(s);
    return Number.isNaN(n) ? undefined : n;
  };
  return {
    referenceNumber: text(f.referenceNumber),
    modalidad: f.modalidad || undefined,
    name: text(f.name),
    partyRole: f.partyRole || undefined,
    documentType: text(f.documentType),
    documentNumber: text(f.documentNumber),
    status: f.status || undefined,
    provider: f.provider || undefined,
    scoreMin: num(f.scoreMin),
    scoreMax: num(f.scoreMax),
    createdFrom: f.createdFrom ? `${f.createdFrom}T00:00:00` : undefined,
    createdTo: f.createdTo ? `${f.createdTo}T23:59:59` : undefined,
    rejectionReason: f.status === 'rechazado' ? text(f.rejectionReason) : undefined,
    vigenciaEstado: f.vigenciaEstado || undefined,
    // Rango por fecha de fin de vigencia (expiraHasta a fin de día, igual que createdTo).
    expiraDesde: f.expiraDesde ? `${f.expiraDesde}T00:00:00` : undefined,
    expiraHasta: f.expiraHasta ? `${f.expiraHasta}T23:59:59` : undefined,
    venceEnDias: num(f.venceEnDias),
  };
}

/** Filtros de persona para el endpoint agrupado (HU #11271): sin campos de validación. */
function buildPersonApiFilters(f: ValidacionesUiFilters): TenantBiometricPersonFilters {
  const text = (s: string) => (s.trim() === '' ? undefined : s.trim());
  const num = (s: string) => {
    if (s.trim() === '') return undefined;
    const n = Number(s);
    return Number.isNaN(n) ? undefined : n;
  };
  return {
    name: text(f.name),
    documentType: text(f.documentType),
    documentNumber: text(f.documentNumber),
    status: f.status || undefined,
    createdFrom: f.createdFrom ? `${f.createdFrom}T00:00:00` : undefined,
    createdTo: f.createdTo ? `${f.createdTo}T23:59:59` : undefined,
    vigenciaEstado: f.vigenciaEstado || undefined,
    expiraDesde: f.expiraDesde ? `${f.expiraDesde}T00:00:00` : undefined,
    expiraHasta: f.expiraHasta ? `${f.expiraHasta}T23:59:59` : undefined,
    venceEnDias: num(f.venceEnDias),
  };
}

type ValidacionesViewMode = 'person' | 'validation';

/** Cadencia del auto-refresco en vivo de la grilla (fase 2). 15 s: fresco sin presionar el backend. */
const AUTO_REFRESH_MS = 15_000;

/** Opciones de filas por página (el cliente decide; de 10 en 10 hasta 50). */
const PAGE_SIZE_OPTIONS = [10, 20, 30, 40, 50];
const DEFAULT_PAGE_SIZE = 20;

export function Validaciones() {
  const [viewMode, setViewMode] = useState<ValidacionesViewMode>('person');
  const viewModeRef = useRef(viewMode);
  viewModeRef.current = viewMode;

  const [validations, setValidations] = useState<TenantBiometricValidation[] | null>(null);
  const [persons, setPersons] = useState<TenantBiometricPerson[] | null>(null);
  const [stats, setStats] = useState<BiometricValidationStats | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(null);
  // Eventos de identidad ATASCADOS (dead-letter) del tenant + ids que se están reencolando.
  // HU #11268 — cuatro estados del panel: loading / error / vacío / lleno.
  const [stuck, setStuck] = useState<StuckIdentityValidationsResponse | null>(null);
  const [stuckLoading, setStuckLoading] = useState(true);
  const [stuckError, setStuckError] = useState<string | null>(null);
  const [requeuing, setRequeuing] = useState<Set<string>>(() => new Set());
  const [requeuingAll, setRequeuingAll] = useState(false);
  // Panel lateral de proceso/tracking (CF-06/07): tabla compacta + botón "Proceso".
  const [processId, setProcessId] = useState<string | null>(null);
  // HU #11273 — historial multi-validación por persona.
  const [personDetail, setPersonDetail] = useState<{
    documentType: string;
    documentNumber: string;
  } | null>(null);

  // Paginación server-side (el listado ya NO se topa a 500; se navega por páginas).
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [total, setTotal] = useState(0);
  const pageRef = useRef(page);
  pageRef.current = page;
  const pageSizeRef = useRef(pageSize);
  pageSizeRef.current = pageSize;

  // `filters` = controles de la UI (instantáneos); `applied` = lo que se consulta al backend. Los chips
  // y fechas aplican de inmediato; los inputs de texto aplican tras un debounce (~300 ms). El filtrado
  // se delega al backend (HU #10347) — NO se filtra client-side sobre el cap de 500 filas.
  const [filters, setFilters] = useState<ValidacionesUiFilters>(EMPTY_VALIDACIONES_FILTERS);
  const [applied, setApplied] = useState<ValidacionesUiFilters>(EMPTY_VALIDACIONES_FILTERS);
  const filtersRef = useRef(filters);
  filtersRef.current = filters;
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Refs para el auto-refresco: el intervalo lee lo último sin re-suscribirse y se evitan carreras.
  const appliedRef = useRef(applied);
  appliedRef.current = applied;
  const validationsRef = useRef(validations);
  validationsRef.current = validations;
  const personsRef = useRef(persons);
  personsRef.current = persons;
  const fetchingRef = useRef(false);
  const reqIdRef = useRef(0);

  const load = useCallback(
    async (uiFilters: ValidacionesUiFilters, opts?: { background?: boolean }) => {
      const reqId = ++reqIdRef.current; // marca de secuencia: solo se aplica el resultado más reciente
      fetchingRef.current = true;
      if (!opts?.background) setFetching(true);
      try {
        if (viewModeRef.current === 'person') {
          const res = await tramitesClient.listTenantBiometricPersons({
            ...buildPersonApiFilters(uiFilters),
            page: pageRef.current,
            pageSize: pageSizeRef.current,
          });
          if (reqId !== reqIdRef.current) return;
          setPersons(res.persons);
          setValidations(null);
          setStats(res.stats);
          setTotal(res.total);
        } else {
          const res = await tramitesClient.listTenantBiometricValidations({
            ...buildApiFilters(uiFilters),
            page: pageRef.current,
            pageSize: pageSizeRef.current,
          });
          if (reqId !== reqIdRef.current) return;
          setValidations(res.validations);
          setPersons(null);
          setStats(res.stats);
          setTotal(res.total);
        }
        setError(() => null);
        setLastUpdatedAt(new Date());
      } catch (err) {
        if (reqId !== reqIdRef.current) return;
        // En auto-refresco con datos ya en pantalla, un fallo transitorio NO machaca la vista con el error.
        if (
          opts?.background &&
          (validationsRef.current !== null || personsRef.current !== null)
        ) {
          return;
        }
        setError(() =>
          err instanceof Error ? err.message : 'No se pudieron cargar las validaciones.',
        );
      } finally {
        if (reqId === reqIdRef.current) {
          fetchingRef.current = false;
          setFetching(false);
          setHasLoadedOnce(true);
        }
      }
    },
    [],
  );

  // Eventos atascados (dead-letter): independiente de los filtros. HU #11268 — expone error/carga
  // sin romper la grilla principal (el fallo queda acotado al panel de atascadas).
  const stuckRef = useRef(stuck);
  stuckRef.current = stuck;
  const refreshStuck = useCallback(async (opts?: { background?: boolean }) => {
    if (!opts?.background) setStuckLoading(true);
    try {
      const res = await tramitesClient.listStuckIdentityValidations();
      setStuck(res);
      setStuckError(null);
    } catch (err) {
      // En auto-refresco con datos ya mostrados, un fallo transitorio no machaca el panel.
      if (opts?.background && stuckRef.current !== null) return;
      setStuckError(
        err instanceof Error ? err.message : 'No se pudieron cargar las validaciones atascadas.',
      );
    } finally {
      if (!opts?.background) setStuckLoading(false);
    }
  }, []);

  // Refetch cuando cambian los filtros aplicados O la página/tamaño (carga inicial incluida).
  useEffect(() => {
    void load(applied);
  }, [applied, page, pageSize, load]);

  // Los eventos atascados son por-tenant (independientes de filtros/página): refrescan con los filtros.
  useEffect(() => {
    void refreshStuck();
  }, [applied, refreshStuck]);

  // Auto-refresco en vivo (fase 2 — "suscripción"): tras la primera carga, refresca la grilla cada
  // AUTO_REFRESH_MS con los filtros vigentes para reflejar los cambios que el backend persiste vía
  // webhook/outbox de Kyverum (aprobado/rechazado/enviado) SIN que el gestor pulse "Actualizar". Pausa
  // cuando la pestaña no está visible (ahorra red) y refresca al volver a ella; nunca solapa peticiones.
  useEffect(() => {
    if (!hasLoadedOnce) return;
    const tick = () => {
      if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;
      if (fetchingRef.current) return;
      void load(appliedRef.current, { background: true });
      void refreshStuck({ background: true });
    };
    const intervalId = setInterval(tick, AUTO_REFRESH_MS);
    const onVisibility = () => {
      if (document.visibilityState === 'visible') tick();
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      clearInterval(intervalId);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [hasLoadedOnce, load, refreshStuck]);

  // Limpia el timer del debounce al desmontar.
  useEffect(() => () => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
  }, []);

  const applyChange = useCallback((patch: Partial<ValidacionesUiFilters>, immediate?: boolean) => {
    // Si el estado deja de ser 'rechazado', se oculta y limpia el filtro de motivo (AC1).
    const normalized =
      'status' in patch && patch.status !== 'rechazado'
        ? { ...patch, rejectionReason: '' }
        : patch;
    const next = { ...filtersRef.current, ...normalized };
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

  const handleRefresh = async () => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    await load(filtersRef.current);
    void refreshStuck();
  };

  const handleClearFilters = useCallback(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setFilters(EMPTY_VALIDACIONES_FILTERS);
    setApplied(EMPTY_VALIDACIONES_FILTERS);
    setPage(1);
  }, []);

  const handlePageChange = useCallback((p: number) => setPage(Math.max(1, p)), []);
  const handlePageSizeChange = useCallback((size: number) => {
    setPageSize(size);
    setPage(1); // cambiar el tamaño reinicia a la primera página
  }, []);

  // Reencolar ("desatascar") un evento: reinicia sus intentos en el backend y refresca atascados + grilla.
  const handleRequeue = useCallback(
    async (id: string) => {
      setRequeuing((s) => new Set(s).add(id));
      try {
        await tramitesClient.requeueStuckIdentityValidation(id);
        // El worker lo retomará; ya no figura como atascado. Refresca ambas vistas.
        await Promise.all([refreshStuck(), load(filtersRef.current, { background: true })]);
      } catch {
        void refreshStuck(); // refleja el estado real si el reencolado falló
      } finally {
        setRequeuing((s) => {
          const next = new Set(s);
          next.delete(id);
          return next;
        });
      }
    },
    [refreshStuck, load],
  );

  // Reencolar TODOS los atascados de una vez.
  const handleRequeueAll = useCallback(async () => {
    setRequeuingAll(true);
    try {
      await tramitesClient.requeueAllStuckIdentityValidations();
      await Promise.all([refreshStuck(), load(filtersRef.current, { background: true })]);
    } catch {
      void refreshStuck();
    } finally {
      setRequeuingAll(false);
    }
  }, [refreshStuck, load]);

  // AC8 — estados de UI. La carga inicial (skeleton) solo aplica antes de la primera respuesta.
  const initialLoading =
    !hasLoadedOnce &&
    validations === null &&
    persons === null &&
    error === null;
  const isEmpty =
    (viewMode === 'person' && persons !== null && persons.length === 0) ||
    (viewMode === 'validation' && validations !== null && validations.length === 0);
  // "Sin resultados" (AC2) vs "Aún no hay validaciones" se decide por los filtros EFECTIVAMENTE aplicados.
  const filtersActive = hasActiveValidacionesFilters(applied);

  const switchViewMode = (mode: ValidacionesViewMode) => {
    if (mode === viewMode) return;
    viewModeRef.current = mode;
    setViewMode(mode);
    setPage(1);
    pageRef.current = 1;
    setValidations(null);
    setPersons(null);
    if (mode === 'person') {
      const cleared: ValidacionesUiFilters = {
        ...filtersRef.current,
        referenceNumber: '',
        modalidad: '',
        partyRole: '',
        provider: '',
        scoreMin: '',
        scoreMax: '',
        rejectionReason: '',
      };
      setFilters(cleared);
      setApplied(cleared);
      void load(cleared);
    } else {
      void load(appliedRef.current);
    }
  };

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="Identidad"
        subtitle="Validación biométrica, OCR IA y cotejo RUNT en tiempo real."
        right={
          <div className="flex items-center gap-3">
            {/* HU #10868 — enlace a pantalla de prevalidación standalone */}
            <Link
              href="/tramites/prevalidaciones"
              className="flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold transition hover:border-[#4F74C9] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#4F74C9]"
              style={{ color: '#4F74C9' }}
              aria-label="Ir a prevalidaciones de identidad"
            >
              <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
              Prevalidaciones
            </Link>
            {hasLoadedOnce ? <LiveIndicator at={lastUpdatedAt} /> : undefined}
          </div>
        }
      />

      <StatsCards stats={stats} loading={initialLoading} />

      {hasLoadedOnce && (
        <div
          className="flex flex-wrap items-center gap-2 shrink-0"
          role="group"
          aria-label="Modo de vista de la grilla"
        >
          <button
            type="button"
            onClick={() => switchViewMode('person')}
            className="rounded-xl px-3 py-1.5 text-[11px] font-semibold border focus-visible:outline focus-visible:outline-2"
            style={{
              borderColor: viewMode === 'person' ? '#4F74C9' : 'rgba(22,39,68,0.15)',
              background: viewMode === 'person' ? 'rgba(79,116,201,0.12)' : 'transparent',
              color: viewMode === 'person' ? '#4F74C9' : undefined,
            }}
            aria-pressed={viewMode === 'person'}
          >
            Por persona
          </button>
          <button
            type="button"
            onClick={() => switchViewMode('validation')}
            className="rounded-xl px-3 py-1.5 text-[11px] font-semibold border focus-visible:outline focus-visible:outline-2"
            style={{
              borderColor: viewMode === 'validation' ? '#4F74C9' : 'rgba(22,39,68,0.15)',
              background: viewMode === 'validation' ? 'rgba(79,116,201,0.12)' : 'transparent',
              color: viewMode === 'validation' ? '#4F74C9' : undefined,
            }}
            aria-pressed={viewMode === 'validation'}
          >
            Por validación
          </button>
        </div>
      )}

      {hasLoadedOnce && (
        <ValidacionesFilterToolbar
          filters={filters}
          onChange={applyChange}
          onRefresh={() => void handleRefresh()}
          onClearFilters={handleClearFilters}
          loading={fetching}
          resultCount={
            viewMode === 'person' ? (persons?.length ?? 0) : (validations?.length ?? 0)
          }
          groupedMode={viewMode === 'person'}
          resultCountLabel={
            viewMode === 'person'
              ? total === 0
                ? 'Sin resultados'
                : `${total} persona${total === 1 ? '' : 's'}`
              : undefined
          }
        />
      )}

      {stuckLoading && (
        <div
          className="rounded-2xl border p-4 shrink-0 animate-pulse"
          style={{ borderColor: 'rgba(240,90,53,0.35)', background: 'rgba(249,172,0,0.06)' }}
          role="status"
          aria-label="Cargando validaciones atascadas"
        >
          <div className="h-4 w-48 rounded bg-black/10 dark:bg-white/10" />
          <div className="mt-2 h-3 w-72 max-w-full rounded bg-black/5 dark:bg-white/5" />
        </div>
      )}

      {!stuckLoading && stuckError && (
        <div
          className="rounded-2xl p-4 border text-xs flex items-start gap-3 shrink-0"
          style={{ borderColor: '#F05A35', background: 'rgba(249,172,0,0.10)', color: '#F05A35' }}
          role="alert"
          aria-label="Error al cargar validaciones atascadas"
        >
          <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
          <div className="space-y-2">
            <p className="font-semibold">No se pudieron cargar las validaciones atascadas.</p>
            <p className="opacity-80">{stuckError}</p>
            <button
              type="button"
              onClick={() => void refreshStuck()}
              className="px-3 py-1.5 rounded-lg text-[11px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ background: '#F05A35' }}
            >
              Reintentar
            </button>
          </div>
        </div>
      )}

      {!stuckLoading && !stuckError && stuck && stuck.total > 0 && (
        <StuckEventsBanner
          stuck={stuck}
          requeuing={requeuing}
          requeuingAll={requeuingAll}
          onRequeue={handleRequeue}
          onRequeueAll={() => void handleRequeueAll()}
        />
      )}

      {error && (
        <div
          className="rounded-2xl p-4 border text-xs flex items-start gap-3"
          style={{ borderColor: '#E43D30', background: 'rgba(228,61,48,0.06)', color: '#E43D30' }}
          role="alert"
          aria-live="polite"
        >
          <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
          <div className="space-y-2">
            <p className="font-semibold">No se pudieron cargar las validaciones.</p>
            <p className="opacity-80">{error}</p>
            <button
              type="button"
              onClick={() => void handleRefresh()}
              className="px-3 py-1.5 rounded-lg text-[11px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ background: '#E43D30' }}
            >
              Reintentar
            </button>
          </div>
        </div>
      )}

      {initialLoading && <ValidacionesSkeleton />}

      {isEmpty && (
        <div
          className="flex-1 min-h-0 grid place-items-center rounded-2xl border"
        >
          <div className="text-center max-w-md px-6 py-10">
            <ScanFace className="mx-auto h-10 w-10 opacity-30" aria-hidden="true" />
            {filtersActive ? (
              // AC2 — hubo respuesta vacía CON filtros activos: no es el estado inicial sin datos.
              <>
                <p className="mt-3 text-sm font-semibold">Sin resultados.</p>
                <p className="mt-1 text-xs opacity-70">
                  Ninguna validación coincide con los filtros aplicados. Ajusta o limpia los filtros
                  para ver más resultados.
                </p>
              </>
            ) : (
              <>
                <p className="mt-3 text-sm font-semibold">Aún no hay validaciones de identidad.</p>
                <p className="mt-1 text-xs opacity-70">
                  Las validaciones aparecen aquí cuando inicias la identidad de una parte desde el paso
                  de identidad de un trámite.
                </p>
              </>
            )}
          </div>
        </div>
      )}

      {!initialLoading && !isEmpty && viewMode === 'validation' && validations !== null && (
        <ValidacionesTable rows={validations} onViewProcess={setProcessId} />
      )}

      {!initialLoading && !isEmpty && viewMode === 'person' && persons !== null && (
        <PersonasTable
          rows={persons}
          onOpenPerson={(docType, docNumber) =>
            setPersonDetail({ documentType: docType, documentNumber: docNumber })
          }
        />
      )}

      {!initialLoading &&
        ((viewMode === 'validation' && validations !== null && validations.length > 0) ||
          (viewMode === 'person' && persons !== null && persons.length > 0)) && (
        <PaginationBar
          page={page}
          pageSize={pageSize}
          total={total}
          disabled={fetching}
          onPageChange={handlePageChange}
          onPageSizeChange={handlePageSizeChange}
        />
      )}

      {processId && (
        <PrevalidacionDetailDrawer
          validationId={processId}
          onClose={() => setProcessId(null)}
          onStatusChanged={() => void load(appliedRef.current, { background: true })}
          title="Proceso de validación"
        />
      )}

      {personDetail && (
        <PersonIdentityDetailDrawer
          documentType={personDetail.documentType}
          documentNumber={personDetail.documentNumber}
          onClose={() => setPersonDetail(null)}
          onStatusChanged={() => void load(appliedRef.current, { background: true })}
        />
      )}
    </div>
  );
}

/**
 * Indicador de auto-refresco "En vivo" + hora de la última actualización. Decorativo: NO usa aria-live
 * (evita anunciar la hora cada 15 s a lectores de pantalla); los cambios de datos relevantes se anuncian
 * vía el contador role="status" del toolbar. El título da el contexto en hover/foco.
 */
function LiveIndicator({ at }: { at: Date | null }) {
  const time = at
    ? at.toLocaleTimeString('es-CO', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
    : null;
  return (
    <span
      className="flex items-center gap-1.5 text-[11px] font-medium opacity-70 shrink-0"
      title="La lista se actualiza automáticamente"
    >
      <span className="relative flex h-2 w-2" aria-hidden="true">
        <span
          className="absolute inline-flex h-full w-full animate-ping rounded-full opacity-60"
          style={{ background: '#70CF3A' }}
        />
        <span className="relative inline-flex h-2 w-2 rounded-full" style={{ background: '#70CF3A' }} />
      </span>
      En vivo{time ? ` · ${time}` : ''}
    </span>
  );
}

/**
 * Clave de agrupación por persona (HU #11268). Documento normalizado (trim+upper); si no hay
 * documento ni nombre utilizable → grupo de no identificados.
 */
export function stuckPersonGroupKey(event: StuckIdentityValidation): string {
  const tipo = (event.documentType ?? '').trim().toUpperCase();
  const numero = (event.documentNumber ?? '').trim().toUpperCase();
  if (tipo || numero) return `${tipo}|${numero}`;
  return '__unidentified__';
}

export type StuckPersonGroup = {
  key: string;
  label: string;
  events: StuckIdentityValidation[];
};

/** Agrupa eventos atascados por persona; no identificados al final. */
export function groupStuckByPerson(events: StuckIdentityValidation[]): StuckPersonGroup[] {
  const map = new Map<string, StuckPersonGroup>();
  for (const event of events) {
    const key = stuckPersonGroupKey(event);
    let group = map.get(key);
    if (!group) {
      const isUnidentified = key === '__unidentified__';
      const label = isUnidentified
        ? 'No identificados'
        : (event.name?.trim() ||
          [event.documentType, event.documentNumber].filter(Boolean).join(' ') ||
          'Persona');
      group = { key, label, events: [] };
      map.set(key, group);
    }
    group.events.push(event);
  }
  const groups = [...map.values()];
  groups.sort((a, b) => {
    if (a.key === '__unidentified__') return 1;
    if (b.key === '__unidentified__') return -1;
    return b.events.length - a.events.length || a.label.localeCompare(b.label, 'es');
  });
  return groups;
}

/**
 * Banner de validaciones de identidad ATASCADAS (dead-letter): agotaron los reintentos automáticos de su
 * cola —el envío al proveedor (Kyverum) o el encadenamiento async firma/FUR—. Se muestra solo cuando hay
 * atascadas. HU #11268: acordeón por persona (colapsado por defecto), con Reintentar por fila y
 * Reintentar todos.
 */
function StuckEventsBanner({
  stuck,
  requeuing,
  requeuingAll,
  onRequeue,
  onRequeueAll,
}: {
  stuck: StuckIdentityValidationsResponse;
  requeuing: Set<string>;
  requeuingAll: boolean;
  onRequeue: (id: string) => void;
  onRequeueAll: () => void;
}) {
  const groups = groupStuckByPerson(stuck.stuck);
  const [openKeys, setOpenKeys] = useState<Set<string>>(() => new Set());

  const toggleGroup = (key: string) => {
    setOpenKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  return (
    <section
      className="rounded-2xl border p-4 shrink-0"
      style={{ borderColor: '#F05A35', background: 'rgba(249,172,0,0.10)' }}
      aria-label="Validaciones de identidad atascadas"
    >
      <div className="flex items-start gap-3">
        <AlertTriangle className="h-5 w-5 shrink-0 mt-0.5" style={{ color: '#F05A35' }} aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-3">
            <p className="text-sm font-semibold" style={{ color: '#F05A35' }} role="status" aria-live="polite">
              {stuck.total} validación{stuck.total === 1 ? '' : 'es'} de identidad atascada
              {stuck.total === 1 ? '' : 's'}
            </p>
            {stuck.total > 1 && (
              <button
                type="button"
                onClick={onRequeueAll}
                disabled={requeuingAll}
                className="flex shrink-0 items-center gap-1 rounded-lg border px-2.5 py-1 text-[11px] font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                style={{ borderColor: '#F05A35', color: '#F05A35' }}
                aria-label="Reintentar todas las validaciones atascadas"
              >
                <RotateCcw className={`h-3 w-3 ${requeuingAll ? 'animate-spin' : ''}`} aria-hidden="true" />
                {requeuingAll ? 'Reencolando…' : 'Reintentar todos'}
              </button>
            )}
          </div>
          <p className="mt-0.5 text-[11px] opacity-80">
            Agotaron {stuck.maxDeliveryAttempts} reintentos automáticos —el envío al proveedor de identidad o
            el encadenamiento de firma/FUR. Reencólalas para que el sistema las procese de nuevo.
          </p>
          <ul className="mt-2 max-h-[40vh] space-y-1.5 overflow-y-auto pr-1" aria-label="Eventos atascados agrupados por persona">
            {groups.map((group) => {
              const open = openKeys.has(group.key);
              const panelId = `stuck-group-${group.key}`;
              return (
                <li key={group.key} className="rounded-xl border bg-white/70 dark:bg-[#0B0F14]" style={{ borderColor: 'rgba(240,90,53,0.3)' }}>
                  <button
                    type="button"
                    onClick={() => toggleGroup(group.key)}
                    className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-[11px] font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                    style={{ color: '#F05A35' }}
                    aria-expanded={open}
                    aria-controls={panelId}
                  >
                    <span className="min-w-0 truncate">
                      {group.label}
                      <span className="ml-1.5 font-medium opacity-70">
                        · {group.events.length} evento{group.events.length === 1 ? '' : 's'}
                      </span>
                    </span>
                    <ChevronDown
                      className={`h-3.5 w-3.5 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`}
                      aria-hidden="true"
                    />
                  </button>
                  {open && (
                    <ul id={panelId} className="space-y-1.5 border-t px-2 py-2" style={{ borderColor: 'rgba(240,90,53,0.2)' }}>
                      {group.events.map((e) => (
                        <StuckRow key={e.id} event={e} busy={requeuing.has(e.id)} onRequeue={onRequeue} />
                      ))}
                    </ul>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      </div>
    </section>
  );
}

function StuckRow({
  event,
  busy,
  onRequeue,
}: {
  event: StuckIdentityValidation;
  busy: boolean;
  onRequeue: (id: string) => void;
}) {
  // El envío al proveedor (Kyverum) vs. el encadenamiento async firma/FUR son etapas distintas; etiquetar
  // ayuda al gestor a entender qué se trabó. Default 'encadenamiento' si el backend no lo envía (transición).
  const esEnvio = event.kind === 'envio';
  const kindLabel = esEnvio ? 'Envío a proveedor' : 'Firma · FUR';
  return (
    <li
      className="flex items-center justify-between gap-3 rounded-xl border bg-white px-3 py-2 text-[11px] dark:bg-[#0B0F14]"
      style={{ borderColor: 'rgba(240,90,53,0.3)' }}
    >
      <span className="min-w-0 truncate">
        <span
          className="mr-1.5 inline-block rounded px-1.5 py-px text-[10px] font-semibold align-middle"
          style={{ background: 'rgba(240,90,53,0.14)', color: '#F05A35' }}
        >
          {kindLabel}
        </span>
        <span className="font-medium">{event.name ?? 'Persona no disponible'}</span>
        <span className="opacity-60">
          {event.documentNumber ? ` · ${maskDoc(event.documentType ?? '', event.documentNumber)}` : ''}
          {' · '}
          {event.attempts} intentos · {formatFecha(event.occurredAt)}
        </span>
      </span>
      <button
        type="button"
        onClick={() => onRequeue(event.id)}
        disabled={busy}
        className="flex shrink-0 items-center gap-1 rounded-lg px-2.5 py-1 font-semibold text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ background: '#F05A35' }}
        aria-label={`Reintentar la validación de ${event.name ?? 'persona no disponible'}`}
      >
        <RotateCcw className={`h-3 w-3 ${busy ? 'animate-spin' : ''}`} aria-hidden="true" />
        {busy ? 'Reencolando…' : 'Reintentar'}
      </button>
    </li>
  );
}

/** Barra de paginación: selector de filas por página (10–50) + navegación + "X–Y de N". */
function PaginationBar({
  page,
  pageSize,
  total,
  disabled,
  onPageChange,
  onPageSizeChange,
}: {
  page: number;
  pageSize: number;
  total: number;
  disabled: boolean;
  onPageChange: (p: number) => void;
  onPageSizeChange: (size: number) => void;
}) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);
  return (
    <div
      className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-white p-3 dark:bg-[#0B0F14] shrink-0"
    >
      <div className="flex items-center gap-3 text-[11px]">
        <label className="flex items-center gap-1.5">
          <span className="opacity-60">Filas por página</span>
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            disabled={disabled}
            aria-label="Filas por página"
            className="rounded-lg border bg-white px-2 py-1 text-xs outline-none focus:border-[#4F74C9] disabled:opacity-50 dark:bg-[#0B0F14]"
          >
            {PAGE_SIZE_OPTIONS.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
        <span className="opacity-60" role="status" aria-live="polite">
          {from}–{to} de {total}
        </span>
      </div>
      <div className="flex items-center gap-1.5">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={disabled || page <= 1}
          aria-label="Página anterior"
          className="flex h-7 w-7 items-center justify-center rounded-lg border disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        >
          <ChevronLeft className="h-4 w-4" aria-hidden="true" />
        </button>
        <span className="px-1 text-[11px] opacity-70">
          Página {page} de {totalPages}
        </span>
        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={disabled || page >= totalPages}
          aria-label="Página siguiente"
          className="flex h-7 w-7 items-center justify-center rounded-lg border disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        >
          <ChevronRight className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    </div>
  );
}

/** KPIs reales por estado. En carga muestra placeholders accesibles. */
function StatsCards({
  stats,
  loading,
}: {
  stats: BiometricValidationStats | null;
  loading: boolean;
}) {
  const cards = [
    { l: 'Total validaciones', v: stats?.total, i: ShieldCheck, c: '#4F74C9' },
    { l: 'Aprobadas', v: stats?.aprobadas, i: CheckCircle2, c: '#70CF3A' },
    { l: 'En proceso', v: stats?.enProceso, i: Clock, c: '#F05A35' },
    { l: 'Rechazadas', v: stats?.rechazadas, i: XCircle, c: '#E43D30' },
  ];
  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-2 shrink-0">
      {cards.map((k) => {
        const Icon = k.i;
        return (
          <div
            key={k.l}
            className="rounded-2xl px-4 py-2.5 bg-white dark:bg-[#0B0F14] border flex items-center justify-between"
          >
            <div>
              <p className="text-[11px] opacity-70 font-medium">{k.l}</p>
              {loading ? (
                <div
                  className="mt-1 h-6 w-12 animate-pulse rounded bg-black/10 dark:bg-white/10"
                  aria-hidden="true"
                />
              ) : (
                <p className="text-xl font-bold mt-0.5" style={{ color: k.c }}>
                  {k.v ?? 0}
                </p>
              )}
            </div>
            <Icon className="h-7 w-7 opacity-40" style={{ color: k.c }} aria-hidden="true" />
          </div>
        );
      })}
    </div>
  );
}

/** Estado de carga (AC8): placeholder accesible mientras llega la primera respuesta. */
function ValidacionesSkeleton() {
  return (
    <div
      className="flex-1 min-h-0 space-y-2 pt-2"
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      <span className="sr-only">Cargando validaciones de identidad…</span>
      {[0, 1, 2, 3].map((i) => (
        <div
          key={i}
          className="h-12 w-full animate-pulse rounded-xl bg-black/5 dark:bg-white/5"
          aria-hidden="true"
        />
      ))}
    </div>
  );
}

/**
 * Plantilla de columnas compartida por cabecera y filas. Columnas DESACOPLADas: Registro, Aprobación y
 * Vigencia van por separado (cada dato en su propia columna y filtrable desde el toolbar). minmax(0,..)
 * permite truncar el contenido dentro de cada celda del grid.
 */
const GRID_COLS =
  'minmax(0,1.5fr) minmax(0,1.4fr) minmax(0,1.1fr) minmax(0,1.3fr) minmax(0,1.2fr) minmax(0,0.5fr) minmax(0,1.1fr) minmax(0,1fr) minmax(0,1.4fr) minmax(0,1.2fr) minmax(0,0.9fr)';

/**
 * Vista agrupada por persona: reutiliza la misma fila/estilo de develop (ValidacionRow).
 * Cada fila muestra los datos de la validación MÁS RECIENTE del grupo.
 * "Ver proceso" abre el historial multi-validación de la persona (no el drawer de una sola val.).
 */
function PersonasTable({
  rows,
  onOpenPerson,
}: {
  rows: TenantBiometricPerson[];
  onOpenPerson: (documentType: string, documentNumber: string) => void;
}) {
  const byLatestId = new Map(rows.map((r) => [r.latestValidationId, r]));
  const mapped: TenantBiometricValidation[] = rows.map((p) => ({
    id: p.latestValidationId,
    instanceId: p.instanceId,
    referenceNumber: p.referenceNumber,
    modalidad: p.modalidad,
    partyRole: p.partyRole,
    name: p.name,
    documentType: p.documentType,
    documentNumber: p.documentNumber,
    status: p.status,
    score: p.score,
    provider: p.provider,
    expired: p.expired,
    createdAt: p.createdAt,
    validatedAt: p.validatedAt,
    validUntil: p.validUntil,
    daysRemaining: p.daysRemaining,
    captureUrl: p.captureUrl,
    linkExpiresAt: p.linkExpiresAt,
    email: p.email,
  }));

  return (
    <ValidacionesTable
      rows={mapped}
      onViewProcess={(latestValidationId) => {
        const person = byLatestId.get(latestValidationId);
        if (person) onOpenPerson(person.documentType, person.documentNumber);
      }}
    />
  );
}

/** Tabla de validaciones reales. Cada fila enlaza al trámite de origen (vista del wizard). */
function ValidacionesTable({
  rows,
  onViewProcess,
}: {
  rows: TenantBiometricValidation[];
  onViewProcess: (id: string) => void;
}) {
  return (
    <div className="overflow-x-auto shrink-0">
      <div className="min-w-[1080px]">
        <div
          className="sticky top-0 z-10 grid gap-2 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl"
          style={{ background: '#DDE5F0', color: '#162744', gridTemplateColumns: GRID_COLS }}
          aria-hidden="true"
        >
          <div>Trámite</div>
          <div>Persona</div>
          <div>Documento</div>
          <div>Correo</div>
          <div>Estado</div>
          <div>Score</div>
          <div>Registro</div>
          <div>Aprobación</div>
          <div>Vigencia</div>
          <div>Enlace</div>
          <div>Acciones</div>
        </div>
        <ul className="space-y-2 pt-2" aria-label="Validaciones de identidad">
          {rows.map((r) => (
            <ValidacionRow key={r.id} row={r} onViewProcess={() => onViewProcess(r.id)} />
          ))}
        </ul>
      </div>
    </div>
  );
}

function ValidacionRow({
  row: r,
  onViewProcess,
}: {
  row: TenantBiometricValidation;
  onViewProcess: () => void;
}) {
  const [copied, setCopied] = useState(false);
  const meta = ESTADO_META[r.status] ?? ESTADO_META.enviado;
  const modalidad = r.modalidad
    ? (MODALIDAD_LABEL[r.modalidad] ?? r.modalidad)
    : 'Prevalidación';
  const provider = PROVIDER_LABEL[r.provider] ?? r.provider;
  const parte = r.partyRole ? ` (${r.partyRole})` : '';
  const vigencia = vigenciaBadge(r.daysRemaining);
  const refLabel = r.referenceNumber ?? '—';
  const emailLabel = r.email ?? '—';
  const ariaLabel =
    `Validación de ${r.name}${parte}, trámite ${refLabel} (${modalidad}), ` +
    `proveedor ${provider}, correo ${emailLabel}, estado ${meta.label}` +
    (r.score != null ? `, score ${r.score}` : '') +
    (r.status === 'rechazado' && r.rejectionReason ? `, motivo: ${r.rejectionReason}` : '') +
    `, registrada ${formatFecha(r.createdAt)}` +
    (r.validatedAt ? `, aprobada ${formatFechaCorta(r.validatedAt)}` : '') +
    (r.validUntil ? `, vigente hasta ${formatFechaCorta(r.validUntil)}` : '') +
    (vigencia ? `, ${vigencia.label === 'Vencida' ? 'vigencia vencida' : `vigencia: ${vigencia.label} restantes`}` : '') +
    (r.instanceId ? '. Abrir trámite.' : '. Prevalidación standalone.');

  const rowContent = (
    <div
      className="grid gap-2 items-center px-4 py-2 text-xs"
      style={{ gridTemplateColumns: GRID_COLS }}
    >
      <div className="min-w-0">
        {r.referenceNumber ? (
          <span className="flex items-center gap-1 font-mono font-semibold" style={{ color: '#4F74C9' }}>
            <span className="truncate">{r.referenceNumber}</span>
            {r.instanceId && <ExternalLink className="h-3 w-3 shrink-0 opacity-60" aria-hidden="true" />}
          </span>
        ) : (
          <span
            className="inline-block rounded-full px-2 py-0.5 text-[10px] font-semibold"
            style={{ background: 'rgba(79,116,201,0.12)', color: '#4F74C9' }}
          >
            Prevalidación
          </span>
        )}
        <span className="block text-[10px] opacity-60">{r.modalidad ? modalidad : '—'}</span>
      </div>
      <div className="min-w-0">
        <span className="block font-semibold truncate">{r.name}</span>
        <span className="block text-[10px] opacity-60 truncate">
          {provider}
          {r.partyRole ? ` · ${r.partyRole}` : ''}
        </span>
      </div>
      <div className="min-w-0 font-mono text-[11px] opacity-80 truncate">
        {r.documentType} {r.documentNumber}
      </div>
      <div className="min-w-0 text-[11px] opacity-80 truncate" title={emailLabel}>
        {emailLabel}
      </div>
      <div className="min-w-0">
        <StatusBadge label={meta.label} tone={meta.tone} ariaLabel={`Estado: ${meta.label}`} />
        {r.status === 'rechazado' && r.rejectionReason && (
          <span className="mt-0.5 block text-[10px] opacity-70 truncate" title={r.rejectionReason}>
            {r.rejectionReason}
          </span>
        )}
      </div>
      <div className="font-semibold">{r.score ?? '—'}</div>
      <div className="min-w-0 text-[10px] leading-tight opacity-80">{formatFecha(r.createdAt)}</div>
      <div className="min-w-0 text-[10px] leading-tight opacity-80">
        {r.validatedAt ? formatFechaCorta(r.validatedAt) : '—'}
      </div>
      <div className="min-w-0 text-[10px] leading-tight">
        {r.validUntil ? (
          <>
            <span className="block opacity-80">{formatFechaCorta(r.validUntil)}</span>
            {vigencia && (
              <span
                className="mt-0.5 inline-block rounded-full px-1.5 py-px font-semibold"
                style={{ background: vigencia.bg, color: vigencia.color }}
              >
                {vigencia.label}
              </span>
            )}
          </>
        ) : (
          <span className="opacity-80">—</span>
        )}
      </div>
      <div className="min-w-0 text-[10px] leading-tight opacity-80">
        {r.captureUrl && r.linkExpiresAt ? (
          <span title={r.captureUrl}>Vence {formatFechaCorta(r.linkExpiresAt)}</span>
        ) : r.captureUrl ? (
          <span title={r.captureUrl}>Vigente</span>
        ) : (
          <span>—</span>
        )}
      </div>
      <div className="min-w-0" aria-hidden="true" />
    </div>
  );

  const copiarEnlace = async () => {
    if (!r.captureUrl) return;
    try {
      await navigator.clipboard.writeText(r.captureUrl);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      /* sin permiso de clipboard */
    }
  };

  const actionItems = [
    {
      key: 'proceso',
      label: 'Ver proceso',
      icon: ListTree,
      onSelect: onViewProcess,
    },
    ...(r.captureUrl
      ? [
          {
            key: 'copiar',
            label: 'Copiar enlace',
            icon: Copy,
            onSelect: () => {
              void copiarEnlace();
            },
          },
        ]
      : []),
  ];

  return (
    <li className="relative rounded-xl bg-white dark:bg-[#0B0F14] border hover:border-[#4F74C9] transition">
      {r.instanceId ? (
        <a
          href={`/tramites/${r.instanceId}`}
          aria-label={ariaLabel}
          className="block focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        >
          {rowContent}
        </a>
      ) : (
        <div aria-label={ariaLabel} role="listitem">
          {rowContent}
        </div>
      )}
      {/* Acciones fuera del <a> al trámite: menú portalizado (ActionsMenu). */}
      <div className="absolute right-3 top-1/2 z-10 flex -translate-y-1/2 flex-col items-end gap-0.5">
        <ActionsMenu
          ariaLabel={`Acciones de validación de ${r.name}`}
          items={actionItems}
          className="bg-white dark:bg-[#0B0F14]"
        />
        {copied && (
          <span className="text-[10px] font-semibold" style={{ color: '#4F74C9' }} role="status" aria-live="polite">
            Enlace copiado
          </span>
        )}
      </div>
    </li>
  );
}
