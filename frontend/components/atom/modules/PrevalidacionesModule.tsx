'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertCircle, Clock, Copy, ExternalLink, ListTree, Lock, Pencil, Plus, RotateCcw, ScanFace, Send, ShieldAlert } from 'lucide-react';
import { ActionsMenu, type ActionsMenuItem } from '@/components/atom/ActionsMenu';
import { ModuleTitle } from './ModuleTitle';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { tramitesClient, TramitesApiError } from '@/lib/api/tramites-client';
import type {
  BiometricEstado,
  EditarPrevalidacionResult,
  IniciarPrevalidacionResult,
  TenantBiometricValidation,
  TenantBiometricValidationFilters,
  TenantBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';
import { PrevalidacionForm, PrevalidacionSuccessPanel } from './PrevalidacionForm';
import {
  parseRateLimitDetail,
  PrevalidacionEditForm,
  PrevalidacionResendResultPanel,
  type RateLimitInfo,
} from './PrevalidacionEditForm';
import { PrevalidacionDetailDrawer } from './PrevalidacionDetailDrawer';
import {
  EMPTY_PREVALIDACIONES_FILTERS,
  hasActivePrevalidacionesFilters,
  PrevalidacionesFilterToolbar,
  type PrevalidacionesUiFilters,
} from './PrevalidacionesFilterToolbar';

/**
 * Módulo "Prevalidaciones de Identidad" (HU #10868 — Feature #10864 CF-01; ampliado por HU #10944,
 * CF-03, con las acciones Editar/Reenviar; y por HU #11006, Feature #11004, CF-02/CF-04/CF-05).
 * Pantalla dedicada para crear prevalidaciones standalone (sin trámite) y ver/gestionar el estado de
 * las existentes. Consume GET /biometric-validations con `standalone=true` (el backend ya soporta el
 * filtro — CF-02): solo prevalidaciones sin trámite, sin fallback client-side que mezcle filas de
 * trámite.
 *
 * 4 estados obligatorios FLIT: vacío, cargando, error, lleno. WCAG 2.1 AA.
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

/** HU #10944 (D10) — tope y cooldown de reenvíos, seguidos client-side (ver nota de límite de contrato abajo). */
const MAX_REENVIOS = 3;

/** Estado de cooldown/tope de una fila, rastreado en memoria por la sesión de esta pantalla. */
interface ResendMeta {
  count: number;
  cooldownUntil: number | null;
}

/** Datos mínimos para pre-cargar "Nueva prevalidación" a partir de una fila `aprobado` y vencida. */
interface PrefillNueva {
  documentType?: string;
  documentNumber?: string;
  name?: string;
}

/** Resultado de un reenvío (automático al editar el correo, o manual) para el panel de éxito. */
interface ResendResultState {
  email: string;
  captureUrl?: string | null;
  queued?: boolean;
  resendCount: number;
}

function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

const GRID_COLS =
  'minmax(0,1.4fr) minmax(0,1.1fr) minmax(0,1.3fr) minmax(0,0.9fr) minmax(0,1.1fr) minmax(0,1.2fr) minmax(0,1fr) minmax(0,1.6fr)';

const FILTER_DEBOUNCE_MS = 300;

function buildApiFilters(f: PrevalidacionesUiFilters): TenantBiometricValidationFilters {
  const text = (s: string) => (s.trim() === '' ? undefined : s.trim());
  return {
    standalone: true,
    name: text(f.name),
    documentType: text(f.documentType),
    documentNumber: text(f.documentNumber),
    status: f.status || undefined,
    vigenciaEstado: f.vigenciaEstado || undefined,
    createdFrom: f.createdFrom ? `${f.createdFrom}T00:00:00` : undefined,
    createdTo: f.createdTo ? `${f.createdTo}T23:59:59` : undefined,
    rejectionReason: f.status === 'rechazado' ? text(f.rejectionReason) : undefined,
  };
}

export function PrevalidacionesModule() {
  const [validations, setValidations] = useState<TenantBiometricValidation[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);

  const [filters, setFilters] = useState<PrevalidacionesUiFilters>(EMPTY_PREVALIDACIONES_FILTERS);
  const [applied, setApplied] = useState<PrevalidacionesUiFilters>(EMPTY_PREVALIDACIONES_FILTERS);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const appliedRef = useRef(applied);
  useEffect(() => {
    appliedRef.current = applied;
  }, [applied]);

  const [showForm, setShowForm] = useState(false);
  const [prefillNueva, setPrefillNueva] = useState<PrefillNueva | undefined>(undefined);
  const [successResult, setSuccessResult] = useState<IniciarPrevalidacionResult | null>(null);

  // HU #11008 (Feature #11004, CF-06) — drawer de detalle con poll (D4: mismo panel, sin ruta).
  const [detailId, setDetailId] = useState<string | null>(null);

  // HU #10944 — edición, reenvío manual y resultado de reenvío.
  const [editingRow, setEditingRow] = useState<TenantBiometricValidation | null>(null);
  const [resendConfirmRow, setResendConfirmRow] = useState<TenantBiometricValidation | null>(null);
  const [resendSubmitting, setResendSubmitting] = useState(false);
  const [resendConfirmError, setResendConfirmError] = useState<string | null>(null);
  const [resendResult, setResendResult] = useState<ResendResultState | null>(null);
  const [resendMeta, setResendMeta] = useState<Record<string, ResendMeta>>({});
  const [liveMessage, setLiveMessage] = useState('');

  // Tick ligero para refrescar la etiqueta "disponible en N min" sin depender de una acción del
  // usuario. No es una fuente de datos — solo fuerza el recálculo del cooldown en pantalla.
  const [nowTick, setNowTick] = useState(() => Date.now());
  useEffect(() => {
    const t = window.setInterval(() => setNowTick(Date.now()), 15_000);
    return () => window.clearInterval(t);
  }, []);

  const reqIdRef = useRef(0);

  const load = useCallback(async (
    uiFilters: PrevalidacionesUiFilters = appliedRef.current,
    opts?: { background?: boolean },
  ) => {
    const reqId = ++reqIdRef.current;
    if (!opts?.background) setFetching(true);
    try {
      const res: TenantBiometricValidationsResponse =
        await tramitesClient.listTenantBiometricValidations(buildApiFilters(uiFilters));
      if (reqId !== reqIdRef.current) return;
      setValidations(res.validations);
      setError(null);
    } catch (err) {
      if (reqId !== reqIdRef.current) return;
      setError(err instanceof Error ? err.message : 'No se pudieron cargar las prevalidaciones.');
    } finally {
      if (reqId === reqIdRef.current) {
        setFetching(false);
        setHasLoadedOnce(true);
      }
    }
  }, []);

  useEffect(() => {
    void Promise.resolve().then(() => load(EMPTY_PREVALIDACIONES_FILTERS));
  }, [load]);

  useEffect(() => {
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  const handleFilterChange = (patch: Partial<PrevalidacionesUiFilters>, immediate?: boolean) => {
    const next = { ...filters, ...patch };
    setFilters(next);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (immediate) {
      setApplied(next);
      void load(next);
      return;
    }
    debounceRef.current = setTimeout(() => {
      setApplied(next);
      void load(next);
    }, FILTER_DEBOUNCE_MS);
  };

  const handleClearFilters = () => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setFilters(EMPTY_PREVALIDACIONES_FILTERS);
    setApplied(EMPTY_PREVALIDACIONES_FILTERS);
    void load(EMPTY_PREVALIDACIONES_FILTERS);
  };

  // Poll en vivo del listado (con filtros vigentes): cada 5s si hay filas en proceso;
  // si no, cada 15s para no depender de un botón Actualizar.
  const hasInFlight = (validations ?? []).some(
    (v) =>
      v.status === 'en_proceso' ||
      v.status === 'enviado' ||
      v.status === 'pendiente_envio',
  );
  useEffect(() => {
    if (!hasLoadedOnce) return;
    const ms = hasInFlight ? 5_000 : 15_000;
    const tick = () => {
      if (document.hidden) return;
      void load(appliedRef.current, { background: true });
    };
    const id = window.setInterval(tick, ms);
    return () => window.clearInterval(id);
  }, [hasLoadedOnce, hasInFlight, load]);

  const handleSuccess = (result: IniciarPrevalidacionResult) => {
    setShowForm(false);
    setPrefillNueva(undefined);
    setSuccessResult(result);
    void load(appliedRef.current);
  };

  const handleCloseSuccess = () => {
    setSuccessResult(null);
  };

  const handleNew = () => {
    setSuccessResult(null);
    setPrefillNueva(undefined);
    setShowForm(true);
  };

  /** HU #10944 (D9/borde) — "Nueva prevalidación" para la misma persona desde un registro aprobado. */
  const handleNewFor = (row: TenantBiometricValidation) => {
    setPrefillNueva({ documentType: row.documentType, documentNumber: row.documentNumber, name: row.name });
    setShowForm(true);
  };

  const bumpResendMeta = useCallback((id: string) => {
    setResendMeta((prev) => {
      const cur = prev[id] ?? { count: 0, cooldownUntil: null };
      return { ...prev, [id]: { count: cur.count + 1, cooldownUntil: Date.now() + 5 * 60_000 } };
    });
  }, []);

  const applyRateLimit = useCallback((id: string, info: RateLimitInfo) => {
    setResendMeta((prev) => {
      const cur = prev[id] ?? { count: 0, cooldownUntil: null };
      return {
        ...prev,
        [id]: {
          count: info.maxedOut ? MAX_REENVIOS : cur.count,
          cooldownUntil: info.cooldownMinutes ? Date.now() + info.cooldownMinutes * 60_000 : cur.cooldownUntil,
        },
      };
    });
  }, []);

  const handleEditSaved = (row: TenantBiometricValidation, result: EditarPrevalidacionResult) => {
    setEditingRow(null);
    if (result.resent) {
      bumpResendMeta(row.id);
      const nextCount = (resendMeta[row.id]?.count ?? 0) + 1;
      setResendResult({
        email: result.validation.email,
        captureUrl: result.captureUrl,
        resendCount: nextCount,
      });
      setLiveMessage(`Datos actualizados. Validación reenviada a ${result.validation.email}.`);
    } else {
      setLiveMessage('Datos de la prevalidación actualizados. No hubo cambio de correo, no se reenvió.');
    }
    void load(appliedRef.current);
  };

  const handleConfirmResend = async () => {
    if (!resendConfirmRow) return;
    setResendSubmitting(true);
    setResendConfirmError(null);
    try {
      const result = await tramitesClient.resendPrevalidacion(resendConfirmRow.id);
      const nextCount = (resendMeta[resendConfirmRow.id]?.count ?? 0) + 1;
      bumpResendMeta(resendConfirmRow.id);
      setResendConfirmRow(null);
      setResendResult({
        email: result.validation.email,
        captureUrl: result.captureUrl,
        queued: result.queued,
        resendCount: nextCount,
      });
      setLiveMessage(
        result.queued
          ? `La validación quedó encolada para reenviarse a ${result.validation.email}.`
          : `Validación reenviada a ${result.validation.email}.`,
      );
      void load(appliedRef.current);
    } catch (err) {
      if (err instanceof TramitesApiError) {
        setResendConfirmError(err.message);
        if (err.status === 429) applyRateLimit(resendConfirmRow.id, parseRateLimitDetail(err.message));
      } else {
        setResendConfirmError(err instanceof Error ? err.message : 'No se pudo reenviar la validación.');
      }
    } finally {
      setResendSubmitting(false);
    }
  };

  const initialLoading = !hasLoadedOnce && validations === null && error === null;
  const isEmpty = validations !== null && validations.length === 0 && !fetching;
  const filtersActive = hasActivePrevalidacionesFilters(applied);

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      {/* Anuncios para lector de pantalla del resultado de editar/reenviar (WCAG 2.1 AA) */}
      <div className="sr-only" role="status" aria-live="polite">
        {liveMessage}
      </div>

      <ModuleTitle
        title="Prevalidaciones de Identidad"
        subtitle="Crea validaciones biométricas sin trámite previo. El enlace se reutiliza al crear el trámite."
        right={
          <button
            type="button"
            onClick={() => setShowForm(true)}
            className="flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold text-white transition focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
            style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
            aria-label="Crear nueva prevalidación de identidad"
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            Nueva prevalidación
          </button>
        }
      />

      {!initialLoading && (
        <PrevalidacionesFilterToolbar
          filters={filters}
          onChange={handleFilterChange}
          onClearFilters={handleClearFilters}
          resultCount={validations?.length ?? 0}
        />
      )}

      {/* Estado: Error */}
      {error && (
        <div
          className="rounded-2xl p-4 border text-xs flex items-start gap-3"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
          <div className="space-y-2">
            <p className="font-semibold">No se pudieron cargar las prevalidaciones.</p>
            <p className="opacity-80">{error}</p>
            <button
              type="button"
              onClick={() => void load(appliedRef.current)}
              className="flex items-center gap-1 px-3 py-1.5 rounded-lg text-[11px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ background: '#FF4E00' }}
            >
              <RotateCcw className="h-3 w-3" aria-hidden="true" />
              Reintentar
            </button>
          </div>
        </div>
      )}

      {/* Estado: Cargando (skeleton) */}
      {initialLoading && (
        <div
          className="flex-1 min-h-0 space-y-2 pt-2"
          role="status"
          aria-live="polite"
          aria-busy="true"
        >
          <span className="sr-only">Cargando prevalidaciones de identidad…</span>
          {[0, 1, 2].map((i) => (
            <div
              key={i}
              className="h-14 w-full animate-pulse rounded-xl bg-black/5 dark:bg-white/5"
              aria-hidden="true"
            />
          ))}
        </div>
      )}

      {/* Estado: Vacío / sin resultados de filtro */}
      {isEmpty && !error && (
        <div className="flex-1 min-h-0 grid place-items-center rounded-2xl border">
          <div className="text-center max-w-md px-6 py-12">
            <ScanFace className="mx-auto h-10 w-10 opacity-30" aria-hidden="true" />
            {filtersActive ? (
              <>
                <p className="mt-3 text-sm font-semibold">Sin resultados para estos filtros.</p>
                <p className="mt-1 text-xs opacity-70">
                  Prueba otro nombre, documento o estado, o limpia los filtros.
                </p>
                <button
                  type="button"
                  onClick={handleClearFilters}
                  className="mt-4 text-sm font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                  style={{ color: '#557EFF' }}
                >
                  Limpiar filtros
                </button>
              </>
            ) : (
              <>
                <p className="mt-3 text-sm font-semibold">No hay prevalidaciones aún.</p>
                <p className="mt-1 text-xs opacity-70">
                  Crea la primera prevalidación de identidad para adelantar la verificación biométrica sin
                  necesidad de un trámite en curso.
                </p>
                <button
                  type="button"
                  onClick={() => setShowForm(true)}
                  className="mt-4 flex items-center gap-2 mx-auto rounded-xl px-4 py-2 text-sm font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
                  style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
                >
                  <Plus className="h-4 w-4" aria-hidden="true" />
                  Nueva prevalidación
                </button>
              </>
            )}
          </div>
        </div>
      )}

      {/* Estado: Lleno (tabla) */}
      {!initialLoading && !isEmpty && validations !== null && validations.length > 0 && (
        <div className="overflow-x-auto shrink-0">
          <div className="min-w-[1020px]">
            <div
              className="sticky top-0 z-10 grid gap-2 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl"
              style={{ background: '#DFE5ED', color: '#162744', gridTemplateColumns: GRID_COLS }}
              aria-hidden="true"
            >
              <div>Persona</div>
              <div>Documento</div>
              <div>Correo</div>
              <div>Estado</div>
              <div>Creada</div>
              <div>Aprobada</div>
              <div>Enlace</div>
              <div>Acciones</div>
            </div>
            <ul className="space-y-2 pt-2" aria-label="Prevalidaciones de identidad">
              {validations.map((v) => (
                <PrevalidacionRow
                  key={v.id}
                  row={v}
                  now={nowTick}
                  resendMeta={resendMeta[v.id] ?? { count: 0, cooldownUntil: null }}
                  onEdit={setEditingRow}
                  onResendClick={(row) => {
                    setResendConfirmError(null);
                    setResendConfirmRow(row);
                  }}
                  onNewFor={handleNewFor}
                  onViewDetail={() => setDetailId(v.id)}
                />
              ))}
            </ul>
          </div>
        </div>
      )}

      {/* Drawer: detalle con poll + tracking embebido (HU #11008, CF-06) */}
      {detailId && (
        <PrevalidacionDetailDrawer
          validationId={detailId}
          onClose={() => setDetailId(null)}
          onStatusChanged={() => void load(appliedRef.current, { background: true })}
          title="Proceso de prevalidación"
        />
      )}

      {/* Modal: formulario de creación (también usado para "Nueva prevalidación" precargada, D9/borde) */}
      {showForm && (
        <PrevalidacionForm
          onClose={() => {
            setShowForm(false);
            setPrefillNueva(undefined);
          }}
          onSuccess={handleSuccess}
          initialValues={prefillNueva}
        />
      )}

      {/* Modal: éxito / enlace de creación */}
      {successResult && (
        <PrevalidacionSuccessPanel
          result={successResult}
          onClose={handleCloseSuccess}
          onNew={handleNew}
        />
      )}

      {/* Modal: edición (HU #10944, AC1/AC3/AC4/AC6) */}
      {editingRow && (
        <PrevalidacionEditForm
          row={editingRow}
          onClose={() => setEditingRow(null)}
          onSaved={(result) => handleEditSaved(editingRow, result)}
          onRateLimited={(info) => applyRateLimit(editingRow.id, info)}
        />
      )}

      {/* Confirmación de reenvío manual (HU #10944, AC2) */}
      {resendConfirmRow && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="pv-resend-confirm-title"
        >
          <div className="w-full max-w-sm rounded-2xl bg-white p-6 shadow-xl dark:bg-[#0B0F14]">
            <h2 id="pv-resend-confirm-title" className="text-base font-semibold text-[#162744] dark:text-white">
              Reenviar validación
            </h2>
            <p className="mt-2 text-sm opacity-70">
              ¿Reenviar el enlace de validación de <strong>{resendConfirmRow.name}</strong>? El enlace
              anterior dejará de funcionar.
            </p>
            {resendConfirmError && (
              <p role="alert" aria-live="assertive" className="mt-2 text-xs font-medium" style={{ color: '#FF4E00' }}>
                {resendConfirmError}
              </p>
            )}
            <div className="mt-4 flex justify-end gap-3">
              <button
                type="button"
                onClick={() => {
                  setResendConfirmRow(null);
                  setResendConfirmError(null);
                }}
                disabled={resendSubmitting}
                className="rounded-xl border px-4 py-2 text-sm font-medium text-[#162744] transition hover:bg-black/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] disabled:opacity-50 dark:text-white dark:hover:bg-white/10"
              >
                Cancelar
              </button>
              <button
                type="button"
                onClick={() => void handleConfirmResend()}
                disabled={resendSubmitting}
                className="rounded-xl px-4 py-2 text-sm font-semibold text-white transition disabled:opacity-60 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
                style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
              >
                {resendSubmitting ? 'Reenviando…' : 'Confirmar reenvío'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Resultado del reenvío (automático al editar, o manual) — HU #10944, AC1/AC2 */}
      {resendResult && (
        <PrevalidacionResendResultPanel
          email={resendResult.email}
          captureUrl={resendResult.captureUrl}
          queued={resendResult.queued}
          resendCount={resendResult.resendCount}
          onClose={() => setResendResult(null)}
        />
      )}
    </div>
  );
}

function PrevalidacionRow({
  row: r,
  now,
  resendMeta,
  onEdit,
  onResendClick,
  onNewFor,
  onViewDetail,
}: {
  row: TenantBiometricValidation;
  now: number;
  resendMeta: ResendMeta;
  onEdit: (row: TenantBiometricValidation) => void;
  onResendClick: (row: TenantBiometricValidation) => void;
  onNewFor: (row: TenantBiometricValidation) => void;
  onViewDetail: () => void;
}) {
  const meta = ESTADO_META[r.status] ?? ESTADO_META.enviado;
  const [copied, setCopied] = useState(false);

  const copiar = async () => {
    if (!r.captureUrl) return;
    try {
      await navigator.clipboard.writeText(r.captureUrl);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2500);
    } catch {
      /* sin permiso de clipboard */
    }
  };

  // HU #10944 (D12/AC3) — pertenece a un trámite ⇒ solo lectura, sin acciones de editar/reenviar.
  const isTramite = r.instanceId !== null;
  // HU #10944 (D9/AC4) — aprobada (vigente o vencida) ⇒ editar/reenviar bloqueados; se ofrece "Nueva prevalidación".
  const isApproved = r.status === 'aprobado';

  let resendDisabledReason: string | null = null;
  if (resendMeta.count >= MAX_REENVIOS) {
    resendDisabledReason = 'Se agotó el tope de 3 reenvíos.';
  } else if (resendMeta.cooldownUntil && resendMeta.cooldownUntil > now) {
    const minsLeft = Math.max(1, Math.ceil((resendMeta.cooldownUntil - now) / 60_000));
    resendDisabledReason = `Disponible en ${minsLeft} min.`;
  }

  const emailLabel = r.email ?? '—';
  const ariaLabel =
    `Prevalidación de ${r.name}, documento ${r.documentType} ${r.documentNumber}, ` +
    `correo ${emailLabel}, estado ${meta.label}` +
    (r.rejectionReason ? `, motivo: ${r.rejectionReason}` : '');

  const actionItems: ActionsMenuItem[] = [
    {
      key: 'proceso',
      label: 'Ver proceso',
      icon: ListTree,
      onSelect: onViewDetail,
    },
  ];

  if (r.captureUrl) {
    actionItems.push({
      key: 'copiar',
      label: copied ? 'Enlace copiado' : 'Copiar enlace',
      icon: Copy,
      onSelect: () => {
        void copiar();
      },
    });
    actionItems.push({
      key: 'abrir',
      label: 'Abrir captura',
      icon: ExternalLink,
      onSelect: () => {
        window.open(r.captureUrl!, '_blank', 'noopener,noreferrer');
      },
    });
  }

  if (!isTramite && isApproved) {
    actionItems.push({
      key: 'nueva',
      label: 'Nueva prevalidación',
      icon: Plus,
      onSelect: () => onNewFor(r),
    });
  } else if (!isTramite && !isApproved) {
    actionItems.push({
      key: 'editar',
      label: 'Editar',
      icon: Pencil,
      onSelect: () => onEdit(r),
    });
    actionItems.push({
      key: 'reenviar',
      label: 'Reenviar',
      icon: resendDisabledReason ? Clock : Send,
      onSelect: () => onResendClick(r),
      disabled: resendDisabledReason !== null,
      disabledReason: resendDisabledReason ?? undefined,
    });
  }

  return (
    <li className="rounded-xl bg-white dark:bg-[#0B0F14] border text-xs" aria-label={ariaLabel}>
      <div
        className="grid gap-2 items-center px-4 py-3"
        style={{ gridTemplateColumns: GRID_COLS }}
      >
        <div className="min-w-0">
          <span className="block font-medium truncate">{r.name}</span>
          {r.partyRole && (
            <span className="block text-[10px] opacity-60">{r.partyRole}</span>
          )}
        </div>
        <div className="min-w-0 font-mono text-[11px] opacity-80 truncate">
          {r.documentType} {r.documentNumber}
        </div>
        <div className="min-w-0 text-[11px] opacity-80 truncate" title={emailLabel}>
          {emailLabel}
        </div>
        <div>
          <StatusBadge label={meta.label} tone={meta.tone} ariaLabel={`Estado: ${meta.label}`} />
          {r.status === 'rechazado' && r.rejectionReason && (
            <span className="mt-0.5 block text-[10px] opacity-70 truncate" title={r.rejectionReason}>
              {r.rejectionReason}
            </span>
          )}
        </div>
        <div className="text-[10px] leading-tight opacity-80">{formatFecha(r.createdAt)}</div>
        <div className="text-[10px] leading-tight opacity-80">
          {r.validatedAt ? formatFecha(r.validatedAt) : '—'}
        </div>
        <div className="min-w-0 text-[10px] leading-tight opacity-80">
          {r.captureUrl ? (
            <span title={r.captureUrl}>Disponible</span>
          ) : (
            <span className="opacity-50">—</span>
          )}
        </div>
        <div className="flex flex-col items-start gap-1">
          <ActionsMenu
            ariaLabel={`Acciones de prevalidación de ${r.name}`}
            items={actionItems}
          />
          {isTramite && (
            <span className="inline-flex items-center gap-1 text-[10px] opacity-60">
              <Lock className="h-3 w-3 shrink-0" aria-hidden="true" />
              Solo lectura (pertenece a un trámite)
            </span>
          )}
          {!isTramite && isApproved && (
            <span className="inline-flex items-center gap-1 text-[10px] opacity-70">
              <ShieldAlert className="h-3 w-3 shrink-0" aria-hidden="true" />
              Identidad aprobada: no editable ni reenviable.
            </span>
          )}
          {!isTramite && !isApproved && resendDisabledReason && (
            <span className="text-[10px] opacity-60">{resendDisabledReason}</span>
          )}
          {copied && (
            <span className="text-[10px] font-semibold" style={{ color: '#557EFF' }} role="status" aria-live="polite">
              Enlace copiado
            </span>
          )}
        </div>
      </div>
    </li>
  );
}

