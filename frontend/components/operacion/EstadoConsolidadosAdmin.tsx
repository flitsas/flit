'use client';

import { useEffect, useState } from 'react';
import { IndicadorVigenciaConsolidado } from '@/components/shared/IndicadorVigenciaConsolidado';
import { InlineAlert } from '@/components/atom/InlineAlert';
import {
  SeccionCargando,
  SeccionError,
  SeccionVacia,
} from '@/components/operacion/detalle/primitivos';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import {
  COPY_ORIGEN_CONSOLIDADO,
  describirVigenciaConsolidado,
  esConsolidadoManual,
  type DocumentoConsolidado,
} from '@/lib/tramites/vigencia-consolidado';
import { mensajeErrorConsolidadoAmigable } from '@/lib/tramites/errores-consolidado';

/** Rótulos visibles que distinguen los dos PDFs del trámite (AC1). */
export const ROTULO_DOCUMENTO_ADMIN: Record<DocumentoConsolidado, string> = {
  wizard: 'Consolidado del wizard',
  maestro: 'Consolidado maestro',
};

const SIN_DATO = 'Sin información de vigencia para este documento.';

/** Respaldo cuando la consulta del detalle falla sin un código conocido. */
export const COPY_ESTADO_CONSOLIDADOS_FALLIDO = 'No se pudo consultar el estado de los consolidados.';

export interface EstadoConsolidadosAdminProps {
  instanceId: string;
  /** SuperAdmin viendo otra compañía: viaja como `X-Tenant-Id` (mismo criterio que las acciones). */
  tenantId?: string;
  /** Cambia tras limpiar/cargar para volver a leer el detalle (AC3). */
  recarga?: number;
}

interface Vigencias {
  wizard: ConsolidadoVigencia | null;
  maestro: ConsolidadoVigencia | null;
}

/**
 * HU #12794 (Épica #12760) — estado de los DOS consolidados (wizard y maestro) en la vista de
 * administración del SuperAdmin (modal «Gestionar consolidado»): vigencia + sello de tiempo
 * (AC1), marca y advertencia de carga manual (AC2) y relectura del detalle tras limpiar/cargar
 * (AC3, vía `recarga`). Lee `GET /api/v1/tramites/instances/{id}` (`consolidadoWizard` /
 * `consolidadoMaestro`, HU #12791) y reutiliza `IndicadorVigenciaConsolidado` (HU #12792).
 *
 * Cuatro estados: cargando (`SeccionCargando`), error con reintento (`SeccionError`), vacío (el
 * backend no informa ninguna de las dos vigencias) y lleno.
 *
 * Uso de ejemplo:
 *   <EstadoConsolidadosAdmin instanceId={item.id} tenantId={tenantId} recarga={n} />
 */
export function EstadoConsolidadosAdmin({
  instanceId,
  tenantId,
  recarga = 0,
}: EstadoConsolidadosAdminProps) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [vigencias, setVigencias] = useState<Vigencias | null>(null);
  const [reintento, setReintento] = useState(0);

  useEffect(() => {
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    setError(null);
    Promise.resolve(tramitesClient.getInstance(instanceId, tenantId))
      .then((detail) => {
        if (cancelled) return;
        setVigencias({
          wizard: detail?.consolidadoWizard ?? null,
          maestro: detail?.consolidadoMaestro ?? null,
        });
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        // Security B2 (Épica #12760): nunca el `message` crudo del backend; copy amigable.
        setError(mensajeErrorConsolidadoAmigable(e, COPY_ESTADO_CONSOLIDADOS_FALLIDO));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [instanceId, tenantId, recarga, reintento]);

  const tituloId = `estado-consolidados-${instanceId}`;

  return (
    <section aria-labelledby={tituloId} className="space-y-2" data-testid="estado-consolidados-admin">
      <p id={tituloId} className="text-xs font-semibold text-[#162744] dark:text-white">
        Estado actual de los consolidados
      </p>
      {loading ? <SeccionCargando etiqueta="Consultando el estado de los consolidados" filas={2} /> : null}
      {!loading && error ? (
        <SeccionError
          mensaje={error}
          contexto="el estado de los consolidados"
          onReintentar={() => setReintento((k) => k + 1)}
        />
      ) : null}
      {!loading && !error && !hayDato(vigencias) ? (
        <SeccionVacia mensaje="Este trámite no informa la vigencia de sus consolidados." />
      ) : null}
      {!loading && !error && vigencias && hayDato(vigencias) ? (
        <ul className="space-y-3">
          <FilaConsolidado documento="wizard" vigencia={vigencias.wizard} />
          <FilaConsolidado documento="maestro" vigencia={vigencias.maestro} />
        </ul>
      ) : null}
    </section>
  );
}

function hayDato(v: Vigencias | null): boolean {
  return !!v && (!!describirVigenciaConsolidado(v.wizard) || !!describirVigenciaConsolidado(v.maestro));
}

function FilaConsolidado({
  documento,
  vigencia,
}: {
  documento: DocumentoConsolidado;
  vigencia: ConsolidadoVigencia | null;
}) {
  const conDato = !!describirVigenciaConsolidado(vigencia, { documento });
  const manual = esConsolidadoManual(vigencia);
  return (
    <li className="space-y-1" data-testid={`estado-consolidado-${documento}`} data-manual={manual}>
      <p className="text-[11px] font-semibold uppercase tracking-wide text-[#59677D] dark:text-white/70">
        {ROTULO_DOCUMENTO_ADMIN[documento]}
      </p>
      {conDato ? (
        <IndicadorVigenciaConsolidado vigencia={vigencia} documento={documento}>
          {manual ? (
            <InlineAlert tone="info" compact title={COPY_ORIGEN_CONSOLIDADO.manual}>
              {COPY_ORIGEN_CONSOLIDADO.advertenciaManual}
            </InlineAlert>
          ) : null}
        </IndicadorVigenciaConsolidado>
      ) : (
        <SeccionVacia mensaje={SIN_DATO} />
      )}
    </li>
  );
}
