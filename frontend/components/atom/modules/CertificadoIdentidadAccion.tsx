'use client';

import { useState } from 'react';
import { Icono } from "@flit/ui/Icono";
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  estadoCertificado,
  TEXTO_CERTIFICADO_EN_PREPARACION,
} from '@/lib/identity/certificado-validacion';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * HU #14303 — descarga de la carta de una validación manual aprobada, o «Certificado en preparación».
 * Con trámite usa la ruta por instancia; sin trámite (prevalidación, detalle manual), la ruta por validación.
 * No pinta nada si la validación no está aprobada.
 */
export function CertificadoIdentidadAccion({
  validation,
  instanceId,
}: {
  validation: Pick<BiometricValidation, 'id' | 'status' | 'provider' | 'cartaDisponible'>;
  instanceId?: string | null;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const estado = estadoCertificado(validation);
  if (estado === 'ninguno') return null;

  if (estado === 'en_preparacion') {
    return (
      <p className="text-xs opacity-70" role="status" aria-live="polite">
        {TEXTO_CERTIFICADO_EN_PREPARACION}
      </p>
    );
  }

  const descargar = async () => {
    setBusy(true);
    setError(null);
    try {
      const { blob, filename } = instanceId
        ? await tramitesClient.downloadBiometricCertificado(instanceId, validation.id)
        : await tramitesClient.downloadValidationCertificado(validation.id);
      const objectUrl = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = objectUrl;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(objectUrl);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo descargar el certificado.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-1">
      <button
        type="button"
        onClick={() => void descargar()}
        disabled={busy}
        className="inline-flex w-fit items-center gap-2 rounded-xl border px-3 py-2 text-xs font-semibold focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-50 border-border"
        style={{ color: 'var(--di-action)' }}
      >
        <Icono nombre="nube-bajar" className="h-3.5 w-3.5 shrink-0" />
        {busy ? 'Descargando…' : 'Descargar certificado'}
      </button>
      {error ? (
        <p className="text-xs text-destructive" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
