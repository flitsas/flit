'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertCircle, Clock, Copy, ExternalLink, Lock, Pencil, Plus, RotateCcw, ScanFace, Send, ShieldAlert } from 'lucide-react';
import { ModuleTitle } from './ModuleTitle';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { tramitesClient, TramitesApiError } from '@/lib/api/tramites-client';
import type {
  BiometricEstado,
  EditarPrevalidacionResult,
  IniciarPrevalidacionResult,
  PrevalidacionPersonType,
  TenantBiometricValidation,
  TenantBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';
import { PrevalidacionForm, PrevalidacionSuccessPanel } from './PrevalidacionForm';
import {
  parseRateLimitDetail,
  PrevalidacionEditForm,
  PrevalidacionResendResultPanel,
  type RateLimitInfo,
} from './PrevalidacionEditForm';

/**
 * Módulo "Prevalidaciones de Identidad" (HU #10868 — Feature #10864 CF-01; ampliado por HU #10944,
 * CF-03, con las acciones Editar/Reenviar). Pantalla dedicada para crear prevalidaciones standalone
 * (sin trámite) y ver/gestionar el estado de las existentes. Reutiliza el endpoint
 * GET /biometric-validations con filtro standalone=true para mostrar solo las prevalidaciones sin
 * trámite. Cuando el BE todavía no soporte el filtro, muestra todas y nota el comportamiento
 * contract-first.
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
  personType?: PrevalidacionPersonType;
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

function maskDoc(tipoDoc: string, documento: string): string {
  const tail = documento.length > 4 ? documento.slice(-4) : documento;
  const masked = documento.length > 4 ? `••••${tail}` : tail;
  return `${tipoDoc} ${masked}`.trim();
}

const GRID_COLS =
  'minmax(0,1.4fr) minmax(0,1.1fr) minmax(0,0.9fr) minmax(0,1.1fr) minmax(0,1.2fr) minmax(0,1fr) minmax(0,1.6fr)';

export function PrevalidacionesModule() {
  const [validations, setValidations] = useState<TenantBiometricValidation[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);

  const [showForm, setShowForm] = useState(false);
  const [prefillNueva, setPrefillNueva] = useState<PrefillNueva | undefined>(undefined);
  const [successResult, setSuccessResult] = useState<IniciarPrevalidacionResult | null>(null);

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

  const load = useCallback(async () => {
    const reqId = ++reqIdRef.current;
    setFetching(true);
    try {
      const res: TenantBiometricValidationsResponse =
        await tramitesClient.listTenantBiometricValidations(
          { standalone: true } as Parameters<typeof tramitesClient.listTenantBiometricValidations>[0],
        );
      if (reqId !== reqIdRef.current) return;
      // HU #10869: filter client-side if backend doesn't support standalone param yet
      const standalone = res.validations.filter((v) => v.instanceId === null);
      // HU #10944 (AC3/D12) — fix de bug preexistente (HU #10869): el fallback comparaba el MISMO
      // filtro dos veces (nunca mostraba nada distinto). Ahora, si no hay ninguna standalone, cae a
      // TODAS las filas devueltas (tal como ya decía el comentario original) para que una validación
      // ligada a un trámite pueda renderizarse en modo solo lectura (defensa en profundidad, D11/D12)
      // en vez de desaparecer silenciosamente.
      setValidations(standalone.length > 0 ? standalone : res.validations);
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
    void load();
  }, [load]);

  const handleSuccess = (result: IniciarPrevalidacionResult) => {
    setShowForm(false);
    setPrefillNueva(undefined);
    setSuccessResult(result);
    void load();
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
    void load();
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
      void load();
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
              onClick={() => void load()}
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

      {/* Estado: Vacío */}
      {isEmpty && !error && (
        <div className="flex-1 min-h-0 grid place-items-center rounded-2xl border">
          <div className="text-center max-w-md px-6 py-12">
            <ScanFace className="mx-auto h-10 w-10 opacity-30" aria-hidden="true" />
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
          </div>
        </div>
      )}

      {/* Estado: Lleno (tabla) */}
      {!initialLoading && !isEmpty && validations !== null && validations.length > 0 && (
        <div className="overflow-x-auto shrink-0">
          <div className="min-w-[920px]">
            <div
              className="sticky top-0 z-10 grid gap-2 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl"
              style={{ background: '#DFE5ED', color: '#162744', gridTemplateColumns: GRID_COLS }}
              aria-hidden="true"
            >
              <div>Persona</div>
              <div>Documento</div>
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
                />
              ))}
            </ul>
          </div>
        </div>
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
}: {
  row: TenantBiometricValidation;
  now: number;
  resendMeta: ResendMeta;
  onEdit: (row: TenantBiometricValidation) => void;
  onResendClick: (row: TenantBiometricValidation) => void;
  onNewFor: (row: TenantBiometricValidation) => void;
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

  const ariaLabel =
    `Prevalidación de ${r.name}, documento ${maskDoc(r.documentType, r.documentNumber)}, ` +
    `estado ${meta.label}` +
    (r.validatedAt ? `, aprobada` : '') +
    (isTramite ? ', solo lectura, pertenece a un trámite' : '') +
    `.`;

  return (
    <li
      className="grid gap-2 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs"
      style={{ gridTemplateColumns: GRID_COLS }}
      aria-label={ariaLabel}
    >
      <div className="min-w-0">
        <span className="block font-medium truncate">{r.name}</span>
        {r.partyRole && (
          <span className="block text-[10px] opacity-60">{r.partyRole}</span>
        )}
      </div>
      <div className="min-w-0 font-mono text-[11px] opacity-80 truncate">
        {maskDoc(r.documentType, r.documentNumber)}
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
      <div>
        {r.captureUrl ? (
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => void copiar()}
              className="inline-flex items-center gap-1 rounded-lg border px-2 py-1 text-[10px] font-semibold transition hover:border-[#557EFF] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ color: '#557EFF' }}
              aria-label={`Copiar enlace de prevalidación de ${r.name}`}
            >
              <Copy className="h-3 w-3" aria-hidden="true" />
              {copied ? 'Copiado' : 'Copiar'}
            </button>
            <a
              href={r.captureUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center rounded-lg border px-2 py-1 text-[10px] font-semibold text-[#557EFF] transition hover:border-[#557EFF] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              aria-label={`Abrir enlace de prevalidación de ${r.name}`}
            >
              <ExternalLink className="h-3 w-3" aria-hidden="true" />
            </a>
          </div>
        ) : (
          <span className="opacity-60">—</span>
        )}
      </div>
      <div>
        {isTramite ? (
          <span className="inline-flex items-center gap-1 text-[10px] opacity-60">
            <Lock className="h-3 w-3 shrink-0" aria-hidden="true" />
            Solo lectura (pertenece a un trámite)
          </span>
        ) : isApproved ? (
          <div className="flex flex-col items-start gap-1">
            <span className="inline-flex items-center gap-1 text-[10px] opacity-70">
              <ShieldAlert className="h-3 w-3 shrink-0" aria-hidden="true" />
              Identidad aprobada: no editable ni reenviable.
            </span>
            <button
              type="button"
              onClick={() => onNewFor(r)}
              className="inline-flex items-center gap-1 rounded-lg border px-2 py-1 text-[10px] font-semibold text-[#557EFF] transition hover:border-[#557EFF] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              aria-label={`Crear nueva prevalidación para ${r.name}`}
            >
              <Plus className="h-3 w-3" aria-hidden="true" />
              Nueva prevalidación
            </button>
          </div>
        ) : (
          <div className="flex flex-col items-start gap-1">
            <div className="flex items-center gap-1.5">
              <button
                type="button"
                onClick={() => onEdit(r)}
                className="inline-flex items-center gap-1 rounded-lg border px-2 py-1 text-[10px] font-semibold transition hover:border-[#557EFF] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                style={{ color: '#557EFF' }}
                aria-label={`Editar prevalidación de ${r.name}`}
              >
                <Pencil className="h-3 w-3" aria-hidden="true" />
                Editar
              </button>
              <button
                type="button"
                onClick={() => onResendClick(r)}
                disabled={resendDisabledReason !== null}
                aria-disabled={resendDisabledReason !== null}
                aria-label={
                  resendDisabledReason
                    ? `Reenviar validación de ${r.name}, no disponible: ${resendDisabledReason}`
                    : `Reenviar validación de ${r.name}`
                }
                title={resendDisabledReason ?? undefined}
                className="inline-flex items-center gap-1 rounded-lg border px-2 py-1 text-[10px] font-semibold transition hover:border-[#557EFF] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:opacity-50 disabled:hover:border-transparent"
                style={{ color: resendDisabledReason ? undefined : '#557EFF' }}
              >
                {resendDisabledReason ? (
                  <Clock className="h-3 w-3" aria-hidden="true" />
                ) : (
                  <Send className="h-3 w-3" aria-hidden="true" />
                )}
                Reenviar
              </button>
            </div>
            {resendDisabledReason && (
              <span className="text-[10px] opacity-60">{resendDisabledReason}</span>
            )}
          </div>
        )}
      </div>
    </li>
  );
}
