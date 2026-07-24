'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertCircle, Copy, ExternalLink, Plus, RotateCcw, ScanFace } from 'lucide-react';
import { ModuleTitle } from './ModuleTitle';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  BiometricEstado,
  IniciarPrevalidacionResult,
  TenantBiometricValidation,
  TenantBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';
import { PrevalidacionForm, PrevalidacionSuccessPanel } from './PrevalidacionForm';

/**
 * Módulo "Prevalidaciones de Identidad" (HU #10868 — Feature #10864 CF-01).
 * Pantalla dedicada para crear prevalidaciones standalone (sin trámite) y ver el estado
 * de las existentes. Reutiliza el endpoint GET /biometric-validations con filtro standalone=true
 * para mostrar solo las prevalidaciones sin trámite. Cuando el BE todavía no soporte el filtro,
 * muestra todas y nota el comportamiento contract-first.
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
  'minmax(0,1.6fr) minmax(0,1.2fr) minmax(0,1fr) minmax(0,1.2fr) minmax(0,1.4fr) minmax(0,1.2fr)';

export function PrevalidacionesModule() {
  const [validations, setValidations] = useState<TenantBiometricValidation[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);

  const [showForm, setShowForm] = useState(false);
  const [successResult, setSuccessResult] = useState<IniciarPrevalidacionResult | null>(null);

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
      setValidations(standalone.length > 0 ? standalone : res.validations.filter((v) => !v.instanceId));
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
    setSuccessResult(result);
    void load();
  };

  const handleCloseSuccess = () => {
    setSuccessResult(null);
  };

  const handleNew = () => {
    setSuccessResult(null);
    setShowForm(true);
  };

  const initialLoading = !hasLoadedOnce && validations === null && error === null;
  const isEmpty = validations !== null && validations.length === 0 && !fetching;

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
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
          <div className="min-w-[760px]">
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
            </div>
            <ul className="space-y-2 pt-2" aria-label="Prevalidaciones de identidad">
              {validations.map((v) => (
                <PrevalidacionRow key={v.id} row={v} onRefresh={() => void load()} />
              ))}
            </ul>
          </div>
        </div>
      )}

      {/* Modal: formulario de creación */}
      {showForm && (
        <PrevalidacionForm
          onClose={() => setShowForm(false)}
          onSuccess={handleSuccess}
        />
      )}

      {/* Modal: éxito / enlace */}
      {successResult && (
        <PrevalidacionSuccessPanel
          result={successResult}
          onClose={handleCloseSuccess}
          onNew={handleNew}
        />
      )}
    </div>
  );
}

function PrevalidacionRow({
  row: r,
  onRefresh,
}: {
  row: TenantBiometricValidation;
  onRefresh: () => void;
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

  const ariaLabel =
    `Prevalidación de ${r.name}, documento ${maskDoc(r.documentType, r.documentNumber)}, ` +
    `estado ${meta.label}` +
    (r.validatedAt ? `, aprobada` : '') +
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
    </li>
  );
}

// Suppress unused warning — onRefresh is kept for future requeue/refresh actions
void ((_: () => void) => _);
