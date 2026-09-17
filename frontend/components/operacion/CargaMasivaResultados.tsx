'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Loader2, RefreshCw } from 'lucide-react';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { formatFechaHora } from '@/lib/format/date';
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

  /**
   * Recarga la lista y el detalle. Conserva el lote que el usuario tenía elegido: «Actualizar» se
   * pulsa para ver cómo va ESE lote, y saltar al más reciente le cambiaba la selección debajo de
   * las manos. Solo cae al primero cuando no hay selección (carga inicial) o el elegido ya no está.
   */
  const cargar = useCallback(async (loteElegido?: string) => {
    setCargando(true);
    setError(null);
    try {
      const items = await bulkTramitesClient.listarLotes();
      setLotes(items);
      const objetivo = items.find((l) => l.id === loteElegido) ?? items[0];
      setDetalle(objetivo ? await bulkTramitesClient.detalleLote(objetivo.id) : null);
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
            {/* Con hora: dos cargas del mismo archivo el mismo día eran indistinguibles. */}
            {lotes.map((lote) => (
              <option key={lote.id} value={lote.id}>
                {`${TITULO_TIPO.get(lote.templateType) ?? lote.templateType} · ${formatFechaHora(lote.createdAt)} · ${lote.sourceFilename}`}
              </option>
            ))}
          </select>
        </label>

        <button
          type="button"
          onClick={() => void cargar(detalle?.batch.id)}
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
 *
 * <p>El motivo puede venir como `codigo:detalle` (p. ej. `conductor_no_encontrado:CC 123`): una
 * fila de traspaso trae hasta 8 personas y «conductor no encontrado» a secas no dice cuál. El
 * detalle se muestra tal cual, entre paréntesis.</p>
 */
export function mensajeMotivo(motivo: string): string {
  const propios: Record<string, string> = {
    organismo_transito_no_habilitado:
      'El organismo de tránsito no es uno de los habilitados para tu empresa.',
    porcentajes_no_suman_100: 'Los porcentajes de propiedad no suman 100.',
    porcentaje_en_cero: 'Hay un propietario con porcentaje en 0.',
    sin_actores_en_la_fila: 'La fila no traía ningún actor: complétalo en el trámite.',
    // Salió de las pruebas en DEV (Feature #12519): el wizard exige correo, celular, ciudad y
    // dirección de cada actor; sin ellos el trámite se creaba «a medias». Ahora la fila ni se procesa.
    datos_contacto_incompletos:
      'Faltan datos de contacto de un actor (correo, celular, ciudad y dirección son obligatorios). Corrige la fila y vuelve a cargarla.',
    vehiculo_no_encontrado: 'El RUNT no encontró el vehículo. Revisa la placa o el VIN.',
    consulta_vehiculo_fallida:
      'La consulta del vehículo al RUNT falló. Vuelve a cargar la fila más tarde.',
    conductor_no_encontrado:
      'El RUNT no encontró a la persona con ese documento. Revísalo y complétalo en el trámite.',
    consulta_conductor_fallida:
      'La consulta de la persona al RUNT falló. Retoma el trámite y vuelve a consultarla.',
    unsupported_document_type:
      'Ese tipo de documento no se consulta en el RUNT (personas jurídicas): complétalo en el trámite.',
    // HU #12538 — actor con NIT: la empresa sale de RUES y su firmante del directorio de
    // representantes legales de la compañía.
    empresa_no_encontrada: 'RUES no encontró una empresa con ese NIT. Revísalo y complétalo en el trámite.',
    consulta_empresa_fallida:
      'La consulta de la empresa a RUES falló. Retoma el trámite y vuelve a consultarla.',
    persona_juridica_sin_representante_registrado:
      'La empresa no tiene representante legal registrado en tu directorio. Regístralo en Representantes legales o complétalo en el trámite.',
    representante_no_registrado:
      'La cédula del representante no corresponde a ninguno registrado para esa empresa. Revísala o deja la columna vacía para usar el principal.',
    rl_email_requerido:
      'El representante legal registrado no tiene correo: agrégalo en Representantes legales o complétalo en el trámite.',
    error_inesperado: 'Ocurrió un error inesperado al procesar la fila.',
    // Códigos del paso 1 del wizard que una fila puede heredar. Son los mismos que el wizard
    // muestra con su propio texto; aquí se traducen para que el motivo no sea una constante.
    DUPLICATE_ACTIVE_PROCEDURE:
      'Ya existe un trámite en proceso para este vehículo en tu empresa. Retómalo o anúlalo antes de cargarlo de nuevo.',
    TRANSIT_OFFICE_REQUIRED: 'La matrícula exige un organismo de tránsito.',
    TRANSIT_OFFICE_NOT_AVAILABLE: 'El organismo de tránsito no está disponible para tu empresa.',
    VEHICLE_STATE_INVALID_FOR_TYPE: 'El estado del vehículo en el RUNT no permite este trámite.',
    VEHICLE_BODY_TYPE_MISSING: 'El RUNT no reporta el tipo de carrocería del vehículo.',
    identificador_requerido: 'La fila no trae placa ni VIN.',
    modalidad_not_available: 'Este tipo de trámite no está habilitado para tu empresa.',
    procedure_type_not_found: 'El tipo de trámite no existe en el catálogo.',
    create_failed: 'No se pudo crear el trámite.',
    tipo_de_plantilla_no_soportado: 'El tipo de plantilla del lote no se reconoce.',
  };

  const separador = motivo.indexOf(':');
  const codigo = separador === -1 ? motivo : motivo.slice(0, separador);
  const detalle = separador === -1 ? '' : motivo.slice(separador + 1).trim();
  const base = propios[codigo] ?? mensajeErrorCargaMasiva(codigo);
  return detalle ? `${base} (${detalle})` : base;
}
