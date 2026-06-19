'use client';

import { useCallback, useEffect, useState } from 'react';
import { Check, Copy, RefreshCw } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  BiometricEstado,
  BiometricParte,
  BiometricTipoDoc,
  BiometricValidation,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Re-consulta el estado del wizard tras iniciar/refrescar (server-driven). */
  onRefresh?: () => void;
}

/** Partes que requieren biométrica por modalidad. */
function partesFor(modalidad: WizardModalidad): BiometricParte[] {
  return modalidad === 'traspaso' ? ['comprador', 'vendedor'] : ['comprador'];
}

const PARTE_LABEL: Record<BiometricParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

const DOC_OPTIONS: { value: BiometricTipoDoc; label: string }[] = [
  { value: 'CC', label: 'Cédula de ciudadanía (CC)' },
  { value: 'CE', label: 'Cédula de extranjería (CE)' },
  { value: 'TI', label: 'Tarjeta de identidad (TI)' },
  { value: 'PPT', label: 'Permiso por Protección Temporal (PPT)' },
  { value: 'PAS', label: 'Pasaporte' },
];

const ESTADO_BADGE: Record<BiometricEstado, { label: string; bg: string; color: string }> = {
  enviado: { label: 'Enviado', bg: 'rgba(85,126,255,0.12)', color: '#557EFF' },
  en_proceso: { label: 'En proceso', bg: 'rgba(249,172,0,0.12)', color: '#F9AC00' },
  aprobado: { label: 'Aprobado', bg: 'rgba(140,198,63,0.15)', color: '#5B8A1F' },
  rechazado: { label: 'Rechazado', bg: 'rgba(255,78,0,0.10)', color: '#FF4E00' },
  expirado: { label: 'Expirado', bg: '#EEF1F5', color: '#9AA5B1' },
};

/** Badge de color por estado de la validación. */
function EstadoBadge({ estado }: { estado: BiometricEstado }) {
  const s = ESTADO_BADGE[estado] ?? ESTADO_BADGE.enviado;
  return (
    <span
      className="px-2.5 py-1 rounded-full text-[10px] font-bold"
      style={{ background: s.bg, color: s.color }}
    >
      {s.label}
    </span>
  );
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const INPUT_BASE =
  'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]';

/** Construye el magic-link absoluto a partir del path relativo del backend. */
function absoluteLink(path: string): string {
  if (typeof window === 'undefined') return path;
  return `${window.location.origin}${path}`;
}

/**
 * Paso de validación biométrica (Slice 6, mock). Muestra una tarjeta por parte
 * requerida según la modalidad (matrícula → comprador; traspaso → comprador +
 * vendedor). Si la parte no tiene validación, un formulario inicia la biométrica
 * y, al éxito, muestra el magic-link generado (en DEV no hay envío de correo).
 * El status/gating lo decide el wizard server-driven: este paso solo refresca.
 */
export function BiometricStep({ instanceId, modalidad, onRefresh }: Props) {
  const partes = partesFor(modalidad);

  const [validations, setValidations] = useState<BiometricValidation[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // No se fija "loading" de forma síncrona: el spinner de "Actualizar" lo maneja
  // handleRefresh (acción del usuario), evitando el cascading-render del efecto.
  const load = useCallback(async () => {
    if (!instanceId) return;
    try {
      const list = await tramitesClient.listBiometric(instanceId);
      setValidations(list);
      setError(() => null);
    } catch (err) {
      setError(() =>
        err instanceof Error ? err.message : 'Error al cargar las validaciones.',
      );
    }
  }, [instanceId]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleRefresh = async () => {
    setLoading(true);
    try {
      await load();
    } finally {
      setLoading(false);
    }
    onRefresh?.();
  };

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        <p className="text-xs opacity-70">
          Validación biométrica de identidad de cada parte. Genera el enlace, el
          participante sube su selfie y la cédula (frente y reverso) y el sistema
          resuelve la validación.
        </p>
        <button
          type="button"
          onClick={() => void handleRefresh()}
          disabled={loading || !instanceId}
          className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0 disabled:opacity-50"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Actualizar estado biométrico"
        >
          <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} />
          Actualizar
        </button>
      </div>

      {error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      <div className="space-y-4">
        {partes.map((parte) => {
          const validation = (validations ?? []).find((v) =>
            modalidad === 'traspaso'
              ? v.parte === parte
              : v.parte === null || v.parte === 'comprador',
          );
          return (
            <ParteCard
              key={parte}
              parte={parte}
              instanceId={instanceId}
              validation={validation ?? null}
              onChanged={() => void handleRefresh()}
            />
          );
        })}
      </div>
    </div>
  );
}

/** Tarjeta por parte: estado actual o formulario para iniciar la biométrica. */
function ParteCard({
  parte,
  instanceId,
  validation,
  onChanged,
}: {
  parte: BiometricParte;
  instanceId: string | null;
  validation: BiometricValidation | null;
  onChanged: () => void;
}) {
  return (
    <fieldset
      className="rounded-xl border p-4"
      style={{ borderColor: '#DFE5ED' }}
      aria-label={`Biométrica ${PARTE_LABEL[parte]}`}
    >
      <legend className="px-1 text-xs font-bold flex items-center gap-2">
        {PARTE_LABEL[parte]}
        {validation && <EstadoBadge estado={validation.estado} />}
      </legend>

      {validation ? (
        <ValidationView validation={validation} />
      ) : (
        <IniciarForm parte={parte} instanceId={instanceId} onStarted={onChanged} />
      )}
    </fieldset>
  );
}

/** Vista del estado de una validación existente: score, intentos y magic-link. */
function ValidationView({ validation: v }: { validation: BiometricValidation }) {
  return (
    <div className="space-y-2 text-xs">
      <p>
        <span className="opacity-60">Participante: </span>
        <span className="font-medium">{v.nombre}</span>{' '}
        <span className="opacity-60">
          ({v.tipoDoc} {v.documento})
        </span>
      </p>
      <p className="opacity-70">{v.email}</p>
      <div className="flex flex-wrap gap-x-6 gap-y-1">
        <span>
          <span className="opacity-60">Intentos: </span>
          {v.intentos}/{v.maxIntentos}
        </span>
        {v.score !== null && (
          <span>
            <span className="opacity-60">Score: </span>
            <span className="font-semibold">{v.score}</span>
          </span>
        )}
      </div>
      {v.estado === 'aprobado' && (
        <p className="flex items-center gap-1.5 font-semibold" style={{ color: '#5B8A1F' }}>
          <Check className="h-3.5 w-3.5" /> Identidad verificada
        </p>
      )}
      {v.estado === 'rechazado' && (
        <p style={{ color: '#FF4E00' }}>
          Validación rechazada. El participante puede reintentar mientras queden intentos.
        </p>
      )}
      {(v.estado === 'expirado' || v.expired) && (
        <p style={{ color: '#9AA5B1' }}>El enlace de validación expiró.</p>
      )}
    </div>
  );
}

/** Formulario para iniciar la biométrica de una parte. */
function IniciarForm({
  parte,
  instanceId,
  onStarted,
}: {
  parte: BiometricParte;
  instanceId: string | null;
  onStarted: () => void;
}) {
  const [nombre, setNombre] = useState('');
  const [tipoDoc, setTipoDoc] = useState<BiometricTipoDoc>('CC');
  const [documento, setDocumento] = useState('');
  const [email, setEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [link, setLink] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const prefix = `bio-${parte}`;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!instanceId) return;
    if (!nombre.trim() || !documento.trim() || !email.trim()) {
      setError('Completa nombre, documento y correo.');
      return;
    }
    if (!EMAIL_RE.test(email.trim())) {
      setError('Correo no válido.');
      return;
    }
    setError(null);
    setSubmitting(true);
    try {
      const result = await tramitesClient.iniciarBiometric(instanceId, {
        parte,
        nombre: nombre.trim(),
        tipoDoc,
        documento: documento.trim(),
        email: email.trim(),
      });
      setLink(absoluteLink(result.magicLinkPath));
      onStarted();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudo iniciar la biométrica.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  const handleCopy = async () => {
    if (!link) return;
    try {
      await navigator.clipboard.writeText(link);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Sin acceso al portapapeles: el enlace ya está visible para copiar a mano.
    }
  };

  if (link) {
    return (
      <div className="space-y-2">
        <p className="text-[11px] font-semibold" style={{ color: '#5B8A1F' }}>
          Enlace de validación generado (DEV: sin envío de correo).
        </p>
        <div className="flex items-center gap-2">
          <input
            type="text"
            readOnly
            value={link}
            aria-label={`Enlace biométrico ${PARTE_LABEL[parte]}`}
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          />
          <button
            type="button"
            onClick={() => void handleCopy()}
            className="flex items-center gap-1.5 px-3 py-2 rounded-xl text-[11px] font-semibold text-white shrink-0"
            style={{ background: '#557EFF' }}
            aria-label="Copiar enlace"
          >
            {copied ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
            {copied ? 'Copiado' : 'Copiar'}
          </button>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3" noValidate>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        <div className="md:col-span-2">
          <label htmlFor={`${prefix}-nombre`} className="text-xs font-semibold mb-1.5 block">
            Nombre completo
          </label>
          <input
            id={`${prefix}-nombre`}
            type="text"
            value={nombre}
            onChange={(e) => setNombre(e.target.value)}
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>
        <div>
          <label htmlFor={`${prefix}-tipoDoc`} className="text-xs font-semibold mb-1.5 block">
            Tipo de documento
          </label>
          <select
            id={`${prefix}-tipoDoc`}
            value={tipoDoc}
            onChange={(e) => setTipoDoc(e.target.value as BiometricTipoDoc)}
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          >
            {DOC_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor={`${prefix}-documento`} className="text-xs font-semibold mb-1.5 block">
            Número de documento
          </label>
          <input
            id={`${prefix}-documento`}
            type="text"
            value={documento}
            onChange={(e) => setDocumento(e.target.value)}
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>
        <div className="md:col-span-2">
          <label htmlFor={`${prefix}-email`} className="text-xs font-semibold mb-1.5 block">
            Correo electrónico
          </label>
          <input
            id={`${prefix}-email`}
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>
      </div>

      {error && (
        <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}

      <div className="flex justify-end">
        <button
          type="submit"
          disabled={submitting || !instanceId}
          className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: '#557EFF' }}
        >
          {submitting ? 'Generando…' : 'Generar enlace biométrico'}
        </button>
      </div>
    </form>
  );
}
