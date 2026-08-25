'use client';

import { useEffect, useState } from 'react';
import { Download } from 'lucide-react';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import { tramitesClient } from '@/lib/api/tramites-client';
import { formatFecha } from '@/lib/format/date';
import type {
  BiometricEstado,
  BiometricParte,
  BiometricValidation,
} from '@/lib/api/types/procedure-runtime';
import {
  SeccionCargando,
  SeccionError,
  SeccionVacia,
  TarjetaDetalle,
  type SeccionDetalleProps,
} from './primitivos';
import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';

/**
 * Sección «Validación de identidad» del modal de detalle (Frente C).
 *
 * Estado por parte + descarga de certificado + bitácora Kyverum (`IdentityValidationTrackingPanel`)
 * cuando hay `validationId`. Firma del baúl: sin tracking de identidad.
 */

const AZUL = '#557EFF';

const PARTE_LABEL: Record<BiometricParte, string> = {
  vendedor: 'Vendedor',
  comprador: 'Comprador',
};

/** Orden de presentación: saliente antes que entrante (mismo criterio que el resto del detalle). */
const PARTES_TRASPASO: BiometricParte[] = ['vendedor', 'comprador'];
const PARTES_MATRICULA: BiometricParte[] = ['comprador'];

const ESTADO_LABEL: Record<BiometricEstado, string> = {
  enviado: 'Enviado',
  en_proceso: 'En proceso',
  aprobado: 'Aprobado',
  rechazado: 'Rechazado',
  expirado: 'Expirado',
  pendiente_envio: 'Pendiente de envío',
  error_envio: 'Error de envío',
};

const ESTADO_TONE: Record<BiometricEstado, StatusTone> = {
  enviado: 'info',
  en_proceso: 'warning',
  aprobado: 'success',
  rechazado: 'danger',
  expirado: 'neutral',
  pendiente_envio: 'info',
  error_envio: 'danger',
};

interface FilaIdentidad {
  key: string;
  label: string;
  tone: StatusTone;
  statusText: string;
  timestamp: string | null;
  validationId: string | null;
  certificado: boolean;
}

/**
 * `null` = caso vacío legítimo: ninguna parte tiene validación ni acreditación por baúl (la
 * modalidad no exige identidad todavía o el trámite no la ha iniciado). Si al menos una parte tiene
 * algo, se pintan TODAS las partes esperadas — la(s) que falte(n) queda(n) como "Sin iniciar", que
 * es información útil, no un hueco mudo.
 */
function construirFilas(
  modalidad: ProcedureFamily,
  validations: BiometricValidation[],
  firmaBaulPartes: string[],
): FilaIdentidad[] | null {
  const partes = modalidad === 'TRASPASO' ? PARTES_TRASPASO : PARTES_MATRICULA;

  const resueltas = partes.map((parte) => {
    const matches = validations.filter((v) =>
      modalidad === 'TRASPASO'
        ? v.partyRole === parte
        : v.partyRole === null || v.partyRole === 'comprador',
    );
    // La última del arreglo es la vigente (mismo criterio que el panel de historial de identidad).
    const ultima = matches.length > 0 ? matches[matches.length - 1]! : null;
    const enBaul = firmaBaulPartes.includes(parte);
    return { parte, ultima, enBaul };
  });

  const tieneAlgo = resueltas.some((r) => r.ultima !== null || r.enBaul);
  if (!tieneAlgo) return null;

  return resueltas.map(({ parte, ultima, enBaul }) => {
    if (ultima) {
      return {
        key: ultima.id,
        label: PARTE_LABEL[parte],
        tone: ESTADO_TONE[ultima.status] ?? 'neutral',
        statusText: ESTADO_LABEL[ultima.status] ?? ultima.status,
        timestamp: ultima.validatedAt ?? ultima.createdAt ?? null,
        validationId: ultima.id,
        certificado: ultima.status === 'aprobado',
      };
    }
    if (enBaul) {
      return {
        key: `baul-${parte}`,
        label: PARTE_LABEL[parte],
        tone: 'info' as StatusTone,
        statusText: 'Acreditado por firma del baúl',
        timestamp: null,
        validationId: null,
        certificado: false,
      };
    }
    return {
      key: `sin-iniciar-${parte}`,
      label: PARTE_LABEL[parte],
      tone: 'neutral' as StatusTone,
      statusText: 'Sin iniciar',
      timestamp: null,
      validationId: null,
      certificado: false,
    };
  });
}

function FilaValidacion({
  fila,
  descargando,
  onDescargar,
}: {
  fila: FilaIdentidad;
  descargando: boolean;
  onDescargar: (validationId: string, nombre: string) => void;
}) {
  return (
    <li className="space-y-2">
      <div
        className="flex items-center justify-between gap-2 rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10"
      >
        <span className="min-w-0">
          <span className="block text-xs font-medium text-[#162744] dark:text-white">{fila.label}</span>
          {fila.timestamp ? (
            <span className="block text-xs text-[#162744]/70 dark:text-white/70">
              {formatFecha(fila.timestamp)}
            </span>
          ) : null}
        </span>
        <span className="flex shrink-0 items-center gap-2">
          <StatusBadge tone={fila.tone} label={fila.statusText} />
          {fila.certificado && fila.validationId ? (
            <button
              type="button"
              onClick={() => onDescargar(fila.validationId as string, fila.label)}
              disabled={descargando}
              aria-label={`Descargar certificado de ${fila.label}`}
              title="Descargar certificado"
              className="shrink-0 rounded-lg border p-1.5 transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-40 border-[#DFE5ED] dark:border-white/10"
              style={{ color: AZUL }}
            >
              <Download className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          ) : null}
        </span>
      </div>
      {fila.validationId ? (
        <IdentityValidationTrackingPanel validationId={fila.validationId} />
      ) : null}
    </li>
  );
}

export function TramiteDetalleIdentidad({ instanceId, tenantId, item }: SeccionDetalleProps) {
  const [validations, setValidations] = useState<BiometricValidation[]>([]);
  const [firmaBaulPartes, setFirmaBaulPartes] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [descargandoId, setDescargandoId] = useState<string | null>(null);
  const [descargaError, setDescargaError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const res = await tramitesClient.listBiometricExpediente(instanceId, tenantId);
        if (!cancelled) {
          setValidations(res.validations);
          setFirmaBaulPartes(res.firmaBaulPartes);
        }
      } catch (e: unknown) {
        if (!cancelled) {
          setError(
            e instanceof Error ? e.message : 'No se pudo cargar la validación de identidad del trámite.',
          );
          setValidations([]);
          setFirmaBaulPartes([]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [instanceId, tenantId, reloadKey]);

  if (loading) {
    return (
      <TarjetaDetalle titulo="Validación de identidad">
        <SeccionCargando etiqueta="Cargando la validación de identidad del trámite" />
      </TarjetaDetalle>
    );
  }

  if (error) {
    return (
      <TarjetaDetalle titulo="Validación de identidad">
        {/* Con contexto: esta sección comparte panel con la cronología y los archivos finales, y
            tres botones «Reintentar» a secas no se distinguen por lista de botones. */}
        <SeccionError
          mensaje={error}
          contexto="la validación de identidad"
          onReintentar={() => setReloadKey((k) => k + 1)}
        />
      </TarjetaDetalle>
    );
  }

  const filas = construirFilas(item.modalidad, validations, firmaBaulPartes);

  if (filas === null) {
    return (
      <TarjetaDetalle titulo="Validación de identidad">
        <SeccionVacia mensaje="Este trámite todavía no tiene validación de identidad iniciada para ninguna de sus partes." />
      </TarjetaDetalle>
    );
  }

  const descargar = async (validationId: string, nombre: string) => {
    setDescargaError(null);
    setDescargandoId(validationId);
    try {
      const { blob, filename } = await tramitesClient.downloadBiometricCertificado(
        instanceId,
        validationId,
        tenantId,
      );
      const objectUrl = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = objectUrl;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(objectUrl);
    } catch (e: unknown) {
      setDescargaError(
        e instanceof Error ? e.message : `No se pudo descargar el certificado de ${nombre}.`,
      );
    } finally {
      setDescargandoId(null);
    }
  };

  return (
    <TarjetaDetalle titulo="Validación de identidad">
      <ul className="flex flex-col gap-2" aria-label="Validación de identidad por parte">
        {filas.map((fila) => (
          <FilaValidacion
            key={fila.key}
            fila={fila}
            descargando={descargandoId === fila.validationId}
            onDescargar={(validationId, nombre) => void descargar(validationId, nombre)}
          />
        ))}
      </ul>
      {descargaError ? (
        <p className="mt-2 text-xs" style={{ color: '#C2410C' }} role="alert">
          {descargaError}
        </p>
      ) : null}
    </TarjetaDetalle>
  );
}
