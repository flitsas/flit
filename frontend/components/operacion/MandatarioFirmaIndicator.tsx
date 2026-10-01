'use client';

import { useEffect, useState } from 'react';
import { UserCheck } from 'lucide-react';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { MandateSignerPrevisto } from '@/lib/api/types/procedure-runtime';

/**
 * HU #13146 (ADR-0066) — quién firmará el mandato, en solo lectura, en el paso de resumen (FUR).
 *
 * El gestor ya no elige mandatario (lo define el organismo o el Super Admin): aquí solo se informa y
 * se avisa si falta. `estado` y `modo` los calcula el backend con el mismo evaluador que el gate de
 * radicación, así que pantalla y radicación no discrepan.
 */

const FORMA_FIRMA: Record<string, string> = {
  baul: 'Baúl de firmas',
  biometria: 'Validación de identidad',
};

const MOTIVO: Record<string, string> = {
  sin_mandatario_configurado: 'No hay un mandatario registrado para este organismo.',
  mandatario_eliminado: 'El mandatario que correspondía ya no está registrado.',
  mandatario_fuera_de_vigencia: 'El mandatario está fuera de su vigencia.',
  mandatario_inactivo: 'El mandatario está inactivo.',
  sin_validacion_aprobada: 'El mandatario no tiene una validación de identidad aprobada.',
  baul_sin_firma_vigente: 'El mandatario no tiene una firma vigente en el baúl.',
  firma_fisica_sin_migrar: 'La firma física del mandatario no se ha migrado.',
};

export function motivoMandatarioCopy(motivo?: string | null): string {
  return (motivo && MOTIVO[motivo]) || 'El mandatario no cumple los requisitos para firmar.';
}

/** Alerta de falta de mandatario: solo con mandatario ausente o con firma inválida y validación activa. */
export function mandatarioEnAlerta(data: MandateSignerPrevisto | null): boolean {
  return (
    !!data &&
    (data.estado === 'sin_mandatario' || data.estado === 'firma_invalida') &&
    (data.modo === 'block' || data.modo === 'warn')
  );
}

/** En modo block sin mandatario válido, radicar queda deshabilitado (el backend lo rechazaría con 409). */
export function mandatarioBloqueaRadicacion(data: MandateSignerPrevisto | null): boolean {
  return mandatarioEnAlerta(data) && data?.modo === 'block';
}

/**
 * Consulta el firmante previsto. Un fallo de consulta NO bloquea nada (AC4): devuelve `null` y decide
 * el backend al radicar. `refreshKey` vuelve a consultar cuando cambia algo que afecta al firmante
 * (estado del trámite, organismo).
 */
export function useMandatarioPrevisto(
  instanceId: string | null | undefined,
  enabled: boolean,
  refreshKey?: string | null,
): { data: MandateSignerPrevisto | null; loading: boolean } {
  const [data, setData] = useState<MandateSignerPrevisto | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!instanceId || !enabled) return;
    let cancelled = false;
    // Carga async al montar el paso (setState tras await), mismo patrón que el resto de secciones.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    void (async () => {
      try {
        const res = await tramitesClient.getMandateSigner(instanceId);
        if (!cancelled) setData(res && typeof res === 'object' && res.estado ? res : null);
      } catch {
        if (!cancelled) setData(null);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [instanceId, enabled, refreshKey]);

  return { data: enabled ? data : null, loading: enabled && loading };
}

export const MANDATARIO_ALERTA_ID = 'mandatario-firma-alerta';

export function MandatarioFirmaIndicator({
  data,
  loading,
  className,
}: {
  data: MandateSignerPrevisto | null;
  loading: boolean;
  className?: string;
}) {
  // Cargando: texto discreto, sin reservar un bloque que luego salte.
  if (loading && !data) {
    return (
      <p
        role="status"
        aria-busy="true"
        data-testid="mandatario-firma-cargando"
        className={`text-xs opacity-60 ${className ?? ''}`}
      >
        Consultando quién firmará el mandato…
      </p>
    );
  }

  // Vacío / no aplica / pendiente / error de consulta: nada que mostrar (AC4).
  if (!data) return null;

  if (data.estado === 'valido' && data.nombre) {
    const forma = data.formaFirma ? FORMA_FIRMA[data.formaFirma] : null;
    return (
      <div
        role="status"
        data-testid="mandatario-firma-valido"
        className={`flex items-center gap-2 rounded-xl border px-4 py-3 text-xs ${className ?? ''}`}
        style={{ borderColor: 'var(--badge-info-border)', background: 'var(--badge-info-bg)' }}
      >
        <UserCheck
          className="h-4 w-4 shrink-0"
          style={{ color: 'var(--badge-info-fg)' }}
          aria-hidden="true"
        />
        <span>
          <span className="font-semibold">Firmará:</span> {data.nombre}
          {forma ? ` / ${forma}` : ''}
        </span>
      </div>
    );
  }

  if (!mandatarioEnAlerta(data)) return null;

  const invalida = data.estado === 'firma_invalida';
  const block = data.modo === 'block';
  const titulo = invalida
    ? block
      ? 'Mandatario sin firma válida — no se puede radicar'
      : 'Mandatario sin firma válida'
    : block
      ? 'Sin mandatario configurado — no se puede radicar'
      : 'Sin mandatario configurado';

  return (
    <InlineAlert
      id={MANDATARIO_ALERTA_ID}
      tone={block ? 'error' : 'warning'}
      title={titulo}
      className={className}
    >
      <p data-testid={block ? 'mandatario-firma-bloqueo' : 'mandatario-firma-aviso'}>
        {motivoMandatarioCopy(data.motivo)}{' '}
        {block
          ? 'Pídele al organismo de tránsito o al administrador de tu compañía que lo configure para poder radicar.'
          : 'Puedes radicar; conviene pedirle al organismo de tránsito o al administrador de tu compañía que lo configure.'}
      </p>
    </InlineAlert>
  );
}

/** Mensaje del rechazo 409 del backend al radicar por mandatario (AC5); `null` si no es ese caso. */
export function mensajeRechazoMandatario(err: unknown): string | null {
  if (!err || typeof err !== 'object') return null;
  const { status, problem } = err as { status?: unknown; problem?: unknown };
  if (status !== 409 || !problem || typeof problem !== 'object') return null;
  const p = problem as { title?: unknown; detail?: unknown };
  const code = typeof p.title === 'string' ? p.title : '';
  if (code !== 'mandatario_no_configurado' && code !== 'mandatario_firma_invalida') return null;
  if (typeof p.detail === 'string' && p.detail.trim()) return p.detail;
  return code === 'mandatario_firma_invalida'
    ? 'No se puede radicar: la firma del mandatario no es válida. Pídele al organismo de tránsito o al administrador de tu compañía que la actualice.'
    : 'No se puede radicar: no hay mandatario configurado. Pídele al organismo de tránsito o al administrador de tu compañía que lo configure.';
}
