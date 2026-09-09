'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  estadoDeIdentidad,
  hitosDeIdentidad,
  type HitoIdentidad,
  type TonoIdentidad,
} from '@/lib/tramites/identidad-lectura';
import type { BiometricValidation, IdentityAuditEvent } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12186 — la validación de identidad de UNA persona, contada para quien gestiona el trámite.
 *
 * <p>No construye ningún dato nuevo: es presentación sobre la bitácora que ya se consultaba. Lo que
 * cambia es qué se ve primero. Delante, quién se validó, en qué quedó y cuándo; detrás, la bitácora
 * técnica de siempre, intacta, para cuando algo falla de verdad y soporte necesita diagnosticar.</p>
 *
 * <p><b>Ninguna mención a servicios externos en la capa visible</b> (decisión de producto del
 * Feature #12180): eso lo hace valer `lib/tramites/identidad-lectura.ts`, que es lo único que
 * alimenta esta parte.</p>
 */
export function IdentidadLecturaHumana({
  validation,
  rolLabel,
}: {
  validation: BiometricValidation;
  /** «Comprador», «Vendedor»… El rol lo sabe quien abre el modal, no la validación. */
  rolLabel?: string | null;
}) {
  const [eventos, setEventos] = useState<IdentityAuditEvent[]>([]);
  const [cargando, setCargando] = useState(true);

  const cargar = useCallback(async () => {
    try {
      const res = await tramitesClient.getBiometricAuditByValidation(validation.id);
      return res.events ?? [];
    } catch {
      // Sin bitácora la lectura no se cae: el estado y el motivo salen de la validación misma, y
      // son lo que responde la pregunta. Los hitos son el detalle, no el titular.
      return [];
    }
  }, [validation.id]);

  // No hace falta reponer `cargando` al cambiar de validación: el padre monta un componente por
  // validación (`key={v.id}`), así que otra validación es otra instancia y arranca en `true`.
  useEffect(() => {
    let cancelado = false;
    void (async () => {
      const res = await cargar();
      if (cancelado) return;
      setEventos(res);
      setCargando(false);
    })();
    return () => {
      cancelado = true;
    };
  }, [cargar]);

  const estado = estadoDeIdentidad(validation);
  const hitos = hitosDeIdentidad(validation, eventos);

  return (
    <section
      aria-label={`Validación de identidad de ${validation.name}`}
      className="rounded-2xl border border-[#DFE5ED] bg-white dark:border-white/10 dark:bg-[#162744]"
    >
      <header className="flex flex-wrap items-baseline gap-x-2 gap-y-1 border-b border-[#DFE5ED] px-4 py-3 dark:border-white/10">
        <span className="text-sm font-bold text-[#162744] dark:text-white">{validation.name}</span>
        <span className="text-xs text-[#162744]/55 dark:text-white/50">
          {[validation.documentType, validation.documentNumber].filter(Boolean).join(' ')}
          {rolLabel ? ` · ${rolLabel.toLocaleLowerCase('es')}` : ''}
        </span>
        <span className="ml-auto">
          <Pildora label={estado.label} tono={estado.tono} />
        </span>
      </header>

      {/* Qué pasó y, si hay algo que hacer, qué. Un rechazo que no dice por qué obliga a llamar a
          soporte; con el motivo y la salida, el gestor resuelve solo. */}
      {estado.explicacion || estado.accion ? (
        <div className="border-b border-[#DFE5ED] px-4 py-3 dark:border-white/10">
          {estado.explicacion ? (
            <p className="text-xs leading-relaxed text-[#162744]/85 dark:text-white/75">
              {estado.explicacion}
            </p>
          ) : null}
          {estado.accion ? (
            <p className="mt-1 text-xs leading-relaxed text-[#162744]/60 dark:text-white/50">
              {estado.accion}
            </p>
          ) : null}
        </div>
      ) : null}

      <div className="px-4 py-3">
        {cargando ? (
          <p className="text-xs text-[#162744]/55 dark:text-white/50" role="status" aria-live="polite">
            Cargando el historial…
          </p>
        ) : hitos.length === 0 ? (
          <p className="text-xs text-[#162744]/55 dark:text-white/50">
            Todavía no hay movimientos que mostrar.
          </p>
        ) : (
          <ol
            aria-label="Historial de la validación"
            className="relative space-y-3 border-l pl-4"
            style={{ borderColor: '#DFE5ED' }}
          >
            {hitos.map((hito, i) => (
              <Hito key={hito.id} hito={hito} vigente={i === 0} />
            ))}
          </ol>
        )}
      </div>

      {/*
       * NO hay bitácora técnica aquí.
       *
       * La hubo: un desplegable «Bitácora técnica (soporte)» al pie de este panel. Se quitó porque
       * este modal lo abre CUALQUIERA que pueda ver el listado —no hay permiso que lo acote—, y lo
       * que la bitácora enseña es el nombre del proveedor de biometría, los códigos HTTP y los
       * reintentos de integración. Eso no es información del gestor: es diagnóstico, y ponerlo a un
       * clic de la fila lo convierte en parte de la lectura normal del trámite.
       *
       * No se pierde: `IdentityValidationTrackingPanel` sigue vivo y sigue montado en el expediente
       * (`detalle/TramiteDetalleIdentidad`), en el paso de biometría y en los drawers de identidad,
       * que es donde soporte lo consulta. Si algún día debe volver a este panel, tiene que venir
       * detrás de un permiso —`usePermissions` / `hasPermission`—, no abierto.
       */}
    </section>
  );
}

const TONO_CLS: Record<TonoIdentidad, string> = {
  ok: 'bg-[#8CC63F]/15 text-[#3F6212]',
  pendiente: 'bg-[#F9AC00]/15 text-[#92400E]',
  alerta: 'bg-[#FF4E00]/12 text-[#C2410C]',
  neutro: 'bg-[#557EFF]/12 text-[#1E40AF]',
};

function Pildora({ label, tono }: { label: string; tono: TonoIdentidad }) {
  return (
    <span
      className={`inline-flex items-center whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold ${TONO_CLS[tono]}`}
    >
      {label}
    </span>
  );
}

const TONO_PUNTO: Record<TonoIdentidad, string> = {
  ok: '#8CC63F',
  pendiente: '#F9AC00',
  alerta: '#FF4E00',
  neutro: '#557EFF',
};

function Hito({ hito, vigente }: { hito: HitoIdentidad; vigente: boolean }) {
  return (
    <li className="relative">
      <span
        className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full"
        style={{
          background: TONO_PUNTO[hito.tono],
          // El vigente lleva halo; los anteriores, no. La lista está en orden inverso, así que el
          // vigente es el primero. El color por sí solo no distingue nada: el texto ya lo dice.
          boxShadow: vigente ? `0 0 0 3px ${TONO_PUNTO[hito.tono]}33` : undefined,
        }}
        aria-hidden="true"
      />
      <p className="text-xs font-semibold text-[#162744] dark:text-white">{hito.titulo}</p>
      {hito.detalle ? (
        <p className="mt-0.5 text-xs text-[#162744]/70 dark:text-white/60">{hito.detalle}</p>
      ) : null}
      <p className="mt-0.5 font-mono text-[11px] text-[#162744]/50 dark:text-white/40">
        {fecha(hito.cuando)}
      </p>
    </li>
  );
}

function fecha(iso: string): string {
  try {
    return new Date(iso).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' });
  } catch {
    return iso;
  }
}
