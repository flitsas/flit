'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Loader2, RefreshCw } from 'lucide-react';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { formatFecha } from '@/lib/format/date';
import {
  BULK_TRAMITES_TEMPLATES,
  bulkTramitesClient,
  etiquetaOutcome,
  mensajeErrorCargaMasiva,
  type BulkTramitesBatchDetail,
  type BulkTramitesBatchSummary,
} from '@/lib/api/bulk-tramites-client';
import { controlCls } from './tramites-control-styles';

interface Props {
  /** Se recarga cuando cambia: el modal lo sube al encolar un lote nuevo. */
  refreshKey?: number;
  /** Cerrar el modal antes de navegar a un trámite. */
  onNavegar?: () => void;
}

const TITULO_TIPO = new Map(BULK_TRAMITES_TEMPLATES.map((t) => [t.tipo, t.titulo]));

/**
 * Resumen de los lotes de carga masiva (HU #12524). Vive dentro del modal de /tramites porque es
 * donde el usuario dejó el lote: al volver, entra por el mismo botón y encuentra en qué quedó.
 *
 * <p>Un lote en proceso se dice como tal en vez de enseñar un resumen a medias: con las filas aún
 * en cola, «0 creados» se leería como un lote fallido cuando en realidad no ha empezado.</p>
 */
export function CargaMasivaResultados({ refreshKey = 0, onNavegar }: Props) {
  const router = useRouter();
  const [lotes, setLotes] = useState<BulkTramitesBatchSummary[]>([]);
  const [detalle, setDetalle] = useState<BulkTramitesBatchDetail | null>(null);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const items = await bulkTramitesClient.listarLotes();
      setLotes(items);
      if (items.length > 0) {
        setDetalle(await bulkTramitesClient.detalleLote(items[0]!.id));
      } else {
        setDetalle(null);
      }
    } catch {
      setError('No se pudieron cargar los lotes.');
    } finally {
      setCargando(false);
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: el "Cargando lotes…" es intencional
    void cargar();
  }, [cargar, refreshKey]);

  const verDetalle = async (batchId: string) => {
    setError(null);
    try {
      setDetalle(await bulkTramitesClient.detalleLote(batchId));
    } catch {
      setError('No se pudo cargar el detalle del lote.');
    }
  };

  const abrirTramite = (procedureInstanceId: string) => {
    onNavegar?.();
    router.push(`/tramites/${procedureInstanceId}`);
  };

  if (cargando) {
    return (
      <p className="flex items-center gap-2 text-xs text-[#162744]/70 dark:text-white/60">
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
        Cargando lotes…
      </p>
    );
  }

  if (error) {
    return <InlineAlert tone="warning" title="No se pudo consultar">{error}</InlineAlert>;
  }

  if (lotes.length === 0) {
    return (
      <p className="text-xs text-[#162744]/70 dark:text-white/60" data-testid="carga-masiva-sin-lotes">
        Todavía no has cargado ningún archivo. Cuando lo hagas, aquí verás en qué quedó cada fila.
      </p>
    );
  }

  const enProceso = detalle?.batch.status !== 'completed';

  return (
    <div className="flex flex-col gap-4" data-testid="carga-masiva-resultados">
      <div className="flex items-center justify-between gap-2">
        <label className="flex min-w-0 flex-1 items-center gap-2 text-xs text-[#162744] dark:text-white">
          <span className="shrink-0 font-semibold">Lote</span>
          <select
            value={detalle?.batch.id ?? ''}
            onChange={(e) => void verDetalle(e.target.value)}
            aria-label="Lote de carga masiva"
            data-testid="carga-masiva-selector-lote"
            className="h-9 min-w-0 flex-1 rounded-xl border border-[#DFE5ED] bg-white px-2 text-xs dark:border-white/15 dark:bg-[#0B0F14] dark:text-white"
          >
            {lotes.map((lote) => (
              <option key={lote.id} value={lote.id}>
                {`${TITULO_TIPO.get(lote.templateType) ?? lote.templateType} · ${formatFecha(lote.createdAt)} · ${lote.sourceFilename}`}
              </option>
            ))}
          </select>
        </label>

        <button
          type="button"
          onClick={() => void cargar()}
          className={controlCls(false)}
          data-testid="carga-masiva-actualizar"
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          Actualizar
        </button>
      </div>

      {detalle ? (
        <>
          {enProceso ? (
            <InlineAlert tone="info" title="El lote sigue en proceso">
              <span data-testid="carga-masiva-en-proceso">
                {`Van ${detalle.batch.totalRows - detalle.batch.counts.pendientes} de ${detalle.batch.totalRows} filas. `}
                Puedes cerrar esta ventana y volver más tarde; el resultado completo aparece aquí
                cuando termine.
              </span>
            </InlineAlert>
          ) : (
            <p className="text-xs text-[#162744] dark:text-white" data-testid="carga-masiva-contadores">
              {`${detalle.batch.counts.created} creados · ${detalle.batch.counts.createdPending} por retomar · ${detalle.batch.counts.notCreated} no creados`}
            </p>
          )}

          <div className="max-h-64 overflow-y-auto">
            <table className="w-full text-left text-xs">
              <thead className="sticky top-0 bg-white text-[#162744]/70 dark:bg-[#0B0F14] dark:text-white/60">
                <tr>
                  <th className="py-1.5 pr-2 font-semibold">Fila</th>
                  <th className="py-1.5 pr-2 font-semibold">Vehículo</th>
                  <th className="py-1.5 pr-2 font-semibold">Resultado</th>
                  <th className="py-1.5 font-semibold">Motivo</th>
                </tr>
              </thead>
              <tbody>
                {detalle.rows.map((fila) => (
                  <tr
                    key={fila.rowNumber}
                    className="border-t border-[#DFE5ED] align-top dark:border-white/10"
                    data-testid={`carga-masiva-fila-${fila.rowNumber}`}
                  >
                    <td className="py-1.5 pr-2 text-[#162744] dark:text-white">{fila.rowNumber}</td>
                    <td className="py-1.5 pr-2 text-[#162744] dark:text-white">
                      {fila.identificador ?? '—'}
                    </td>
                    <td className="py-1.5 pr-2">
                      {fila.procedureInstanceId ? (
                        <button
                          type="button"
                          onClick={() => abrirTramite(fila.procedureInstanceId!)}
                          className="text-left font-semibold text-[#3B4FD6] underline-offset-2 hover:underline dark:text-[#8FA8FF]"
                          data-testid={`carga-masiva-abrir-${fila.rowNumber}`}
                        >
                          {etiquetaOutcome(fila.outcome)}
                        </button>
                      ) : (
                        <span className="text-[#162744] dark:text-white">
                          {etiquetaOutcome(fila.outcome)}
                        </span>
                      )}
                    </td>
                    <td className="py-1.5 text-[#162744]/70 dark:text-white/60">
                      {fila.motivo ? mensajeMotivo(fila.motivo) : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      ) : null}
    </div>
  );
}

/**
 * Motivos por fila. Los códigos que ya traduce el cliente (los del archivo completo) se reusan;
 * el resto se enseña tal cual antes que inventar una traducción que se desactualice — el catálogo
 * de errores del wizard es grande y cambia.
 */
function mensajeMotivo(codigo: string): string {
  const propios: Record<string, string> = {
    organismo_transito_no_habilitado:
      'El organismo de tránsito no es uno de los habilitados para tu empresa.',
    porcentajes_no_suman_100: 'Los porcentajes de propiedad no suman 100.',
    porcentaje_en_cero: 'Hay un propietario con porcentaje en 0.',
    sin_actores_en_la_fila: 'La fila no traía ningún actor: complétalo en el trámite.',
    error_inesperado: 'Ocurrió un error inesperado al procesar la fila.',
  };

  return propios[codigo] ?? mensajeErrorCargaMasiva(codigo);
}
