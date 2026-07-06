'use client';

import { useCallback, useEffect, useState } from 'react';
import { Check, Upload } from 'lucide-react';
import { biometricPublicClient } from '@/lib/api/tramites-client';
import type {
  BiometriaPublicView,
  CompletarBiometriaResult,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  token: string;
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'ready'; view: BiometriaPublicView };

const CARD =
  'w-full max-w-md rounded-3xl p-7 bg-white dark:bg-[#0B0F14] border';

/** Una de las 3 fotos requeridas. */
interface PhotoSlotState {
  key: 'rostro' | 'cedulaFrontal' | 'cedulaReverso';
  label: string;
  hint: string;
}

const SLOTS: PhotoSlotState[] = [
  { key: 'rostro', label: 'Selfie (rostro)', hint: 'Foto frontal de tu rostro' },
  { key: 'cedulaFrontal', label: 'Cédula — frente', hint: 'Lado frontal del documento' },
  { key: 'cedulaReverso', label: 'Cédula — reverso', hint: 'Lado posterior del documento' },
];

/**
 * Captura biométrica pública por magic-link. Lee la tarea por token; si está
 * resuelta/expirada/no encontrada muestra un mensaje terminal. En estado activo,
 * captura las 3 fotos (rostro + cédula frente/reverso) y las sube en multipart.
 * Muestra el resultado (aprobado/score, rechazado/motivo, intentos restantes).
 */
export function BiometricCapture({ token }: Props) {
  const [load, setLoad] = useState<LoadState>({ kind: 'loading' });
  const [files, setFiles] = useState<Record<string, File | undefined>>({});
  const [previews, setPreviews] = useState<Record<string, string | undefined>>({});
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [result, setResult] = useState<CompletarBiometriaResult | null>(null);

  // No se fija "loading" de forma síncrona aquí: el estado inicial ya es loading
  // (montaje) y las re-consultas posteriores conservan la vista previa hasta que
  // llega la nueva, evitando un parpadeo y el cascading-render del efecto.
  const fetchView = useCallback(async () => {
    try {
      const view = await biometricPublicClient.getByToken(token);
      setLoad(() => ({ kind: 'ready', view }));
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setLoad(() => ({
        kind: 'error',
        message: msg.startsWith('404')
          ? 'Enlace no válido o no encontrado.'
          : 'No se pudo cargar la validación. Intenta de nuevo.',
      }));
    }
  }, [token]);

  useEffect(() => {
    // fetchView solo hace setState DESPUÉS del await (carga async, no cascada
    // síncrona). El analizador no distingue el await; falso positivo de la regla.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void fetchView();
  }, [fetchView]);

  const setFile = (key: string, file: File | undefined) => {
    setFiles((f) => ({ ...f, [key]: file }));
    setPreviews((p) => {
      const next = { ...p };
      if (file) next[key] = URL.createObjectURL(file);
      else next[key] = undefined;
      return next;
    });
  };

  const allSelected =
    !!files.rostro && !!files.cedulaFrontal && !!files.cedulaReverso;

  const handleSubmit = async () => {
    if (!allSelected) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      const res = await biometricPublicClient.complete(token, {
        rostro: files.rostro!,
        cedulaFrontal: files.cedulaFrontal!,
        cedulaReverso: files.cedulaReverso!,
      });
      setResult(res);
      await fetchView();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setSubmitError(
        msg.startsWith('410')
          ? 'El enlace de validación expiró.'
          : msg.startsWith('429')
            ? 'Se agotaron los intentos de validación.'
            : msg.startsWith('409')
              ? 'Esta validación ya no admite más intentos.'
              : 'No se pudo completar la validación. Intenta de nuevo.',
      );
      await fetchView();
    } finally {
      setSubmitting(false);
    }
  };

  if (load.kind === 'loading') {
    return (
      <div className={CARD}>
        <p className="text-sm opacity-70 text-center">Cargando validación…</p>
      </div>
    );
  }

  if (load.kind === 'error') {
    return (
      <div className={CARD} role="alert">
        <h1 className="text-lg font-bold mb-2">Validación biométrica</h1>
        <p className="text-sm" style={{ color: '#FF4E00' }}>
          {load.message}
        </p>
      </div>
    );
  }

  const { view } = load;

  // Estados terminales: no hay captura posible.
  if (view.estado === 'aprobado') {
    return (
      <div className={CARD} role="status">
        <div
          className="h-14 w-14 mx-auto rounded-full grid place-items-center mb-3"
          style={{ background: 'rgba(140,198,63,0.15)' }}
        >
          <Check className="h-7 w-7" style={{ color: '#5B8A1F' }} />
        </div>
        <h1 className="text-lg font-bold text-center">¡Validación aprobada!</h1>
        <p className="text-sm opacity-70 text-center mt-1">
          Tu identidad fue verificada. Ya puedes cerrar esta página.
        </p>
      </div>
    );
  }

  if (view.estado === 'expirado' || view.expired) {
    return (
      <div className={CARD} role="alert">
        <h1 className="text-lg font-bold mb-2">Enlace expirado</h1>
        <p className="text-sm opacity-70">
          El enlace de validación biométrica expiró. Solicita uno nuevo a tu gestor.
        </p>
      </div>
    );
  }

  const intentosRestantes = Math.max(0, view.maxIntentos - view.intentos);
  const sinIntentos = intentosRestantes === 0;

  return (
    <div className={CARD}>
      <h1 className="text-lg font-bold">Validación biométrica</h1>
      <p className="text-sm opacity-70 mt-1">
        Hola {view.nombre}, sube tu selfie y tu cédula (frente y reverso) para
        verificar tu identidad.
      </p>
      <p className="text-[11px] opacity-50 mt-1">
        Intentos: {view.intentos}/{view.maxIntentos}
      </p>

      <p className="text-[10px] opacity-50 mt-3">
        Tus imágenes se usan únicamente para validar tu identidad en este trámite
        (Ley 1581 de 2012). No se comparten con terceros.
      </p>

      <div className="space-y-4 mt-5">
        {SLOTS.map((slot) => (
          <div key={slot.key}>
            <label
              htmlFor={`photo-${slot.key}`}
              className="text-xs font-semibold mb-1.5 block"
            >
              {slot.label}
            </label>
            <div className="flex items-center gap-3">
              <input
                id={`photo-${slot.key}`}
                type="file"
                accept="image/*"
                capture="environment"
                aria-label={slot.label}
                onChange={(e) => setFile(slot.key, e.target.files?.[0])}
                className="text-xs flex-1 file:mr-3 file:px-3 file:py-1.5 file:rounded-lg file:border-0 file:text-white file:text-xs file:font-semibold file:bg-[#557EFF]"
              />
              {previews[slot.key] ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img
                  src={previews[slot.key]}
                  alt={`Vista previa ${slot.label}`}
                  className="h-12 w-12 rounded-lg object-cover border"
                />
              ) : (
                <span
                  className="h-12 w-12 rounded-lg grid place-items-center border"
                  aria-hidden="true"
                >
                  <Upload className="h-4 w-4 opacity-40" />
                </span>
              )}
            </div>
            <p className="text-[10px] opacity-50 mt-1">{slot.hint}</p>
          </div>
        ))}
      </div>

      {result && result.estado === 'rechazado' && (
        <div
          className="rounded-xl p-3 text-xs border mt-4"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="status"
          aria-live="polite"
        >
          Validación rechazada (score {result.score}): {result.motivo}.
          {intentosRestantes > 0
            ? ` Te quedan ${intentosRestantes} intento(s).`
            : ' No quedan más intentos.'}
        </div>
      )}

      {submitError && (
        <p className="text-[11px] font-medium mt-3" style={{ color: '#FF4E00' }} role="alert">
          {submitError}
        </p>
      )}

      <button
        type="button"
        onClick={() => void handleSubmit()}
        disabled={!allSelected || submitting || sinIntentos}
        className="w-full mt-5 py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-50"
        style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
      >
        {submitting
          ? 'Validando…'
          : sinIntentos
            ? 'Sin intentos disponibles'
            : 'Enviar y validar'}
      </button>
    </div>
  );
}
