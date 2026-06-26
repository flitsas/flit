'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  AlertCircle,
  CheckCircle2,
  Clock,
  ExternalLink,
  ScanFace,
  ShieldCheck,
  XCircle,
} from 'lucide-react';
import { ModuleTitle } from './ModuleTitle';
import {
  ValidacionesFilterToolbar,
  EMPTY_VALIDACIONES_FILTERS,
  hasActiveValidacionesFilters,
  type ValidacionesUiFilters,
} from './ValidacionesFilterToolbar';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  BiometricEstado,
  BiometricValidationStats,
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
 * Nota: la actualización automática por colas/suscripción es Fase 2; por ahora hay refresco manual.
 */

const ESTADO_META: Record<BiometricEstado, { label: string; color: string; bg: string }> = {
  enviado: { label: 'Enviado', color: '#557EFF', bg: 'rgba(85,126,255,0.12)' },
  en_proceso: { label: 'En proceso', color: '#B26A00', bg: 'rgba(249,172,0,0.16)' },
  aprobado: { label: 'Aprobado', color: '#5B8A1F', bg: 'rgba(140,198,63,0.16)' },
  rechazado: { label: 'Rechazado', color: '#FF4E00', bg: 'rgba(255,78,0,0.12)' },
  expirado: { label: 'Expirado', color: '#6B7280', bg: 'rgba(154,165,177,0.18)' },
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

/** Enmascara el documento dejando visibles solo los últimos 4 (no se muestra el número completo). */
function maskDoc(tipoDoc: string, documento: string): string {
  const tail = documento.length > 4 ? documento.slice(-4) : documento;
  const masked = documento.length > 4 ? `••••${tail}` : tail;
  return `${tipoDoc} ${masked}`.trim();
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
    nombre: text(f.nombre),
    parte: f.parte || undefined,
    tipoDoc: text(f.tipoDoc),
    documento: text(f.documento),
    estado: f.estado || undefined,
    provider: f.provider || undefined,
    scoreMin: num(f.scoreMin),
    scoreMax: num(f.scoreMax),
    createdFrom: f.createdFrom ? `${f.createdFrom}T00:00:00` : undefined,
    createdTo: f.createdTo ? `${f.createdTo}T23:59:59` : undefined,
    motivoRechazo: f.estado === 'rechazado' ? text(f.motivoRechazo) : undefined,
  };
}

export function Validaciones() {
  const [validations, setValidations] = useState<TenantBiometricValidation[] | null>(null);
  const [stats, setStats] = useState<BiometricValidationStats | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);

  // `filters` = controles de la UI (instantáneos); `applied` = lo que se consulta al backend. Los chips
  // y fechas aplican de inmediato; los inputs de texto aplican tras un debounce (~300 ms). El filtrado
  // se delega al backend (HU #10347) — NO se filtra client-side sobre el cap de 500 filas.
  const [filters, setFilters] = useState<ValidacionesUiFilters>(EMPTY_VALIDACIONES_FILTERS);
  const [applied, setApplied] = useState<ValidacionesUiFilters>(EMPTY_VALIDACIONES_FILTERS);
  const filtersRef = useRef(filters);
  filtersRef.current = filters;
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const load = useCallback(async (uiFilters: ValidacionesUiFilters) => {
    setFetching(true);
    try {
      const res = await tramitesClient.listTenantBiometricValidations(buildApiFilters(uiFilters));
      setValidations(res.validations);
      setStats(res.stats);
      setError(() => null);
    } catch (err) {
      setError(() =>
        err instanceof Error ? err.message : 'No se pudieron cargar las validaciones.',
      );
    } finally {
      setFetching(false);
      setHasLoadedOnce(true);
    }
  }, []);

  // Refetch cuando cambia el conjunto de filtros aplicados (incluye la carga inicial con `applied` vacío).
  useEffect(() => {
    void load(applied);
  }, [applied, load]);

  // Limpia el timer del debounce al desmontar.
  useEffect(() => () => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
  }, []);

  const applyChange = useCallback((patch: Partial<ValidacionesUiFilters>, immediate?: boolean) => {
    // Si el estado deja de ser 'rechazado', se oculta y limpia el filtro de motivo (AC1).
    const normalized =
      'estado' in patch && patch.estado !== 'rechazado'
        ? { ...patch, motivoRechazo: '' }
        : patch;
    const next = { ...filtersRef.current, ...normalized };
    setFilters(next);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (immediate) {
      setApplied(next);
    } else {
      debounceRef.current = setTimeout(() => setApplied(next), 300);
    }
  }, []);

  const handleRefresh = async () => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    await load(filtersRef.current);
  };

  const handleClearFilters = useCallback(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setFilters(EMPTY_VALIDACIONES_FILTERS);
    setApplied(EMPTY_VALIDACIONES_FILTERS);
  }, []);

  // AC8 — estados de UI. La carga inicial (skeleton) solo aplica antes de la primera respuesta.
  const initialLoading = !hasLoadedOnce && validations === null && error === null;
  const isEmpty = validations !== null && validations.length === 0;
  // "Sin resultados" (AC2) vs "Aún no hay validaciones" se decide por los filtros EFECTIVAMENTE aplicados.
  const filtersActive = hasActiveValidacionesFilters(applied);

  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <ModuleTitle
        title="Validaciones de Identidad"
        subtitle="Validación biométrica, OCR IA y cotejo RUNT en tiempo real."
      />

      <StatsCards stats={stats} loading={initialLoading} />

      {hasLoadedOnce && (
        <ValidacionesFilterToolbar
          filters={filters}
          onChange={applyChange}
          onRefresh={() => void handleRefresh()}
          onClearFilters={handleClearFilters}
          loading={fetching}
          resultCount={validations?.length ?? 0}
        />
      )}

      {error && (
        <div
          className="rounded-2xl p-4 border text-xs flex items-start gap-3"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
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
              style={{ background: '#FF4E00' }}
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
          style={{ borderColor: '#DFE5ED' }}
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

      {!initialLoading && !isEmpty && validations !== null && (
        <ValidacionesTable rows={validations} />
      )}
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
    { l: 'Total validaciones', v: stats?.total, i: ShieldCheck, c: '#557EFF' },
    { l: 'Aprobadas', v: stats?.aprobadas, i: CheckCircle2, c: '#5B8A1F' },
    { l: 'En proceso', v: stats?.enProceso, i: Clock, c: '#B26A00' },
    { l: 'Rechazadas', v: stats?.rechazadas, i: XCircle, c: '#FF4E00' },
  ];
  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-2 shrink-0">
      {cards.map((k) => {
        const Icon = k.i;
        return (
          <div
            key={k.l}
            className="rounded-2xl px-4 py-2.5 bg-white dark:bg-[#0B0F14] border flex items-center justify-between"
            style={{ borderColor: '#DFE5ED' }}
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

/** Tabla de validaciones reales. Cada fila enlaza al trámite de origen (vista del wizard). */
function ValidacionesTable({ rows }: { rows: TenantBiometricValidation[] }) {
  return (
    <div className="flex-1 min-h-0 flex flex-col">
      {/* Cabecera decorativa: el lector de pantalla lee el aria-label completo de cada fila. */}
      <div
        className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl"
        style={{ background: '#DFE5ED', color: '#162744' }}
        aria-hidden="true"
      >
        <div className="col-span-2">Trámite</div>
        <div className="col-span-3">Persona</div>
        <div className="col-span-2">Documento</div>
        <div className="col-span-2">Estado</div>
        <div className="col-span-1">Score</div>
        <div className="col-span-2">Fecha</div>
      </div>
      <ul className="flex-1 min-h-[16rem] overflow-y-auto space-y-2 pt-2" aria-label="Validaciones de identidad">
        {rows.map((r) => (
          <ValidacionRow key={r.id} row={r} />
        ))}
      </ul>
    </div>
  );
}

function ValidacionRow({ row: r }: { row: TenantBiometricValidation }) {
  const meta = ESTADO_META[r.estado] ?? ESTADO_META.enviado;
  const modalidad = MODALIDAD_LABEL[r.modalidad] ?? r.modalidad;
  const provider = PROVIDER_LABEL[r.provider] ?? r.provider;
  const parte = r.parte ? ` (${r.parte})` : '';
  const ariaLabel =
    `Validación de ${r.nombre}${parte}, trámite ${r.referenceNumber} (${modalidad}), ` +
    `proveedor ${provider}, estado ${meta.label}` +
    (r.score != null ? `, score ${r.score}` : '') +
    (r.estado === 'rechazado' && r.motivoRechazo ? `, motivo: ${r.motivoRechazo}` : '') +
    `, ${formatFecha(r.createdAt)}. Abrir trámite.`;

  return (
    <li>
      <a
        href={`/tramites/${r.instanceId}`}
        aria-label={ariaLabel}
        className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs hover:border-[#557EFF] transition focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ borderColor: '#DFE5ED' }}
      >
        <div className="col-span-2 min-w-0">
          <span className="flex items-center gap-1 font-mono font-semibold" style={{ color: '#557EFF' }}>
            <span className="truncate">{r.referenceNumber || '—'}</span>
            <ExternalLink className="h-3 w-3 shrink-0 opacity-60" aria-hidden="true" />
          </span>
          <span className="block text-[10px] opacity-60">{modalidad}</span>
        </div>
        <div className="col-span-3 min-w-0">
          <span className="block font-medium truncate">{r.nombre}</span>
          <span className="block text-[10px] opacity-60">
            {provider}
            {r.parte ? ` · ${r.parte}` : ''}
          </span>
        </div>
        <div className="col-span-2 font-mono text-[11px] opacity-80">
          {maskDoc(r.tipoDoc, r.documento)}
        </div>
        <div className="col-span-2">
          <span
            className="inline-block px-2 py-0.5 rounded-full text-[10px] font-semibold"
            style={{ background: meta.bg, color: meta.color }}
          >
            {meta.label}
          </span>
          {r.estado === 'rechazado' && r.motivoRechazo && (
            <span className="mt-0.5 block text-[10px] opacity-70 truncate" title={r.motivoRechazo}>
              {r.motivoRechazo}
            </span>
          )}
        </div>
        <div className="col-span-1 font-semibold">{r.score ?? '—'}</div>
        <div className="col-span-2 text-[11px] opacity-70">{formatFecha(r.createdAt)}</div>
      </a>
    </li>
  );
}
