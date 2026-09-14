'use client';

import { useRef, useState } from 'react';
import { Download, FileSpreadsheet, Upload } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { download } from '@/components/consultas/export';
import { XLSX_MIME } from '@/lib/xlsx';
import {
  BULK_TRAMITES_MAX_ROWS,
  BULK_TRAMITES_TEMPLATES,
  bulkTramitesClient,
  type BulkTramitesBatchAccepted,
  type BulkTramitesTemplateType,
} from '@/lib/api/bulk-tramites-client';
import { controlCls } from './tramites-control-styles';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { CargaMasivaResultados } from './CargaMasivaResultados';

interface Props {
  open: boolean;
  onClose: () => void;
  /** Se dispara cuando el lote queda encolado, para que el listado refresque. */
  onEncolado?: (resultado: BulkTramitesBatchAccepted) => void;
}

/**
 * Carga masiva de trámites (HU #12521). Un solo modal con los dos pasos que pidió el PO: elegir el
 * tipo y descargar su plantilla, y subir el archivo diligenciado.
 *
 * <p>El modal NO espera a que el lote se procese: el backend responde en cuanto lo encola
 * (HU #12523) y aquí se avisa que puede cerrarse y seguir trabajando. Esperar sería justo lo que se
 * quería evitar — 50 filas son 50 consultas al RUNT.</p>
 */
export function CargaMasivaModal({ open, onClose, onEncolado }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [tipo, setTipo] = useState<BulkTramitesTemplateType>('matricula');
  const [archivo, setArchivo] = useState<File | null>(null);
  const [descargando, setDescargando] = useState(false);
  const [subiendo, setSubiendo] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [encolado, setEncolado] = useState<BulkTramitesBatchAccepted | null>(null);
  const [pestana, setPestana] = useState<'cargar' | 'resultados'>('cargar');
  // Sube al encolar: obliga al panel de resultados a releer y traer el lote recién creado.
  const [lotesKey, setLotesKey] = useState(0);

  const reiniciar = () => {
    setArchivo(null);
    setError(null);
    setEncolado(null);
    if (inputRef.current) inputRef.current.value = '';
  };

  const cerrar = () => {
    reiniciar();
    onClose();
  };

  const handleDescargar = async () => {
    setError(null);
    setDescargando(true);
    try {
      const { blob, filename } = await bulkTramitesClient.descargarPlantilla(tipo);
      download(blob, filename, XLSX_MIME);
    } catch {
      setError('No se pudo descargar la plantilla. Inténtalo de nuevo.');
    } finally {
      setDescargando(false);
    }
  };

  const handleArchivo = (e: React.ChangeEvent<HTMLInputElement>) => {
    const elegido = e.target.files?.[0] ?? null;
    setError(null);
    setEncolado(null);

    // El backend vuelve a validar todo; esto solo evita el viaje cuando ni la extensión cuadra.
    if (elegido && !elegido.name.toLowerCase().endsWith('.xlsx')) {
      setArchivo(null);
      setError('El archivo debe ser un Excel con extensión .xlsx.');
      return;
    }

    setArchivo(elegido);
  };

  const handleSubir = async () => {
    if (!archivo) return;
    setError(null);
    setSubiendo(true);
    try {
      const resultado = await bulkTramitesClient.subirLote(tipo, archivo);
      setEncolado(resultado);
      setArchivo(null);
      if (inputRef.current) inputRef.current.value = '';
      setLotesKey((k) => k + 1);
      onEncolado?.(resultado);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo subir el archivo.');
    } finally {
      setSubiendo(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={cerrar}
      title="Carga masiva de trámites"
      titleClassName="text-[22px] font-bold text-[#557EFF] dark:text-[#557EFF]"
      description={`Descarga la plantilla del trámite que vas a cargar, diligénciala y súbela. Hasta ${BULK_TRAMITES_MAX_ROWS} trámites por archivo.`}
      size="lg"
    >
      <div className="flex flex-col gap-6" data-testid="carga-masiva-modal">
        {/* Dos pestañas y no dos pantallas: el usuario que vuelve a ver en qué quedó su lote entra
            por el mismo botón que usó para cargarlo. */}
        <div
          role="tablist"
          aria-label="Carga masiva"
          className="flex gap-1 border-b border-[#DFE5ED] dark:border-white/10"
        >
          {(
            [
              ['cargar', 'Cargar archivo'],
              ['resultados', 'Resultados'],
            ] as const
          ).map(([clave, titulo]) => (
            <button
              key={clave}
              type="button"
              role="tab"
              aria-selected={pestana === clave}
              onClick={() => setPestana(clave)}
              data-testid={`carga-masiva-tab-${clave}`}
              className={`-mb-px border-b-2 px-3 pb-2 text-xs font-semibold transition ${
                pestana === clave
                  ? 'border-[#557EFF] text-[#3B4FD6] dark:text-[#8FA8FF]'
                  : 'border-transparent text-[#162744]/60 hover:text-[#162744] dark:text-white/50 dark:hover:text-white'
              }`}
            >
              {titulo}
            </button>
          ))}
        </div>

        {pestana === 'resultados' ? (
          <CargaMasivaResultados refreshKey={lotesKey} onNavegar={cerrar} />
        ) : (
          <>
        {/* Paso 1 — plantilla */}
        <section className="flex flex-col gap-3">
          <h3 className="text-sm font-semibold text-[#162744] dark:text-white">
            1. Elige el trámite y descarga su plantilla
          </h3>

          <div className="grid gap-2 sm:grid-cols-3">
            {BULK_TRAMITES_TEMPLATES.map((opcion) => {
              const activo = opcion.tipo === tipo;
              return (
                <button
                  key={opcion.tipo}
                  type="button"
                  onClick={() => {
                    setTipo(opcion.tipo);
                    reiniciar();
                  }}
                  aria-pressed={activo}
                  data-testid={`carga-masiva-tipo-${opcion.tipo}`}
                  className={`flex flex-col items-start gap-1 rounded-xl border p-3 text-left transition ${
                    activo
                      ? 'border-[#557EFF] bg-[#EFF6FF] dark:bg-[#557EFF]/10'
                      : 'border-[#DFE5ED] bg-white hover:bg-[#F8FAFC] dark:border-white/15 dark:bg-[#0B0F14]'
                  }`}
                >
                  <span className="flex items-center gap-1.5 text-xs font-semibold text-[#162744] dark:text-white">
                    <FileSpreadsheet className="h-3.5 w-3.5 text-[#557EFF]" aria-hidden="true" />
                    {opcion.titulo}
                  </span>
                  <span className="text-[11px] leading-snug text-[#162744]/70 dark:text-white/60">
                    {opcion.descripcion}
                  </span>
                </button>
              );
            })}
          </div>

          <button
            type="button"
            onClick={() => void handleDescargar()}
            disabled={descargando}
            className={`${controlCls(false)} self-start`}
            data-testid="carga-masiva-descargar"
          >
            <Download className={`h-3.5 w-3.5 ${descargando ? 'animate-pulse' : ''}`} aria-hidden="true" />
            {descargando ? 'Descargando…' : 'Descargar plantilla'}
          </button>

          <p className="text-[11px] leading-snug text-[#162744]/70 dark:text-white/60">
            La plantilla trae una hoja de instrucciones y listas desplegables. No cambies ni muevas
            las columnas de la primera fila: son el contrato que valida el sistema al subir.
          </p>
        </section>

        {/* Paso 2 — archivo */}
        <section className="flex flex-col gap-3 border-t border-[#DFE5ED] pt-5 dark:border-white/10">
          <h3 className="text-sm font-semibold text-[#162744] dark:text-white">
            2. Sube el archivo diligenciado
          </h3>

          <input
            ref={inputRef}
            type="file"
            accept=".xlsx"
            onChange={handleArchivo}
            disabled={subiendo}
            aria-label="Archivo Excel de carga masiva"
            data-testid="carga-masiva-archivo"
            className="block w-full text-xs text-[#162744] file:mr-3 file:rounded-xl file:border file:border-[#DFE5ED] file:bg-white file:px-3 file:py-2 file:text-xs file:font-semibold file:text-[#1E293B] hover:file:bg-[#EFF6FF] dark:text-white dark:file:border-white/15 dark:file:bg-[#0B0F14] dark:file:text-white"
          />

          {error ? (
            <InlineAlert tone="warning" title="No se pudo procesar el archivo">
              {error}
            </InlineAlert>
          ) : null}

          {encolado ? (
            <InlineAlert tone="success" title="El lote quedó en proceso">
              <span data-testid="carga-masiva-encolado">
                {`Se recibieron ${encolado.totalRows} filas. `}
                {encolado.rowsWithStructuralErrors > 0
                  ? `${encolado.rowsWithStructuralErrors} quedaron en error antes de procesarse. `
                  : ''}
                Puedes cerrar esta ventana y seguir usando la aplicación: el resultado de cada fila
                queda en la pestaña Resultados cuando termine.
              </span>
            </InlineAlert>
          ) : null}
        </section>

        <div className="flex justify-end gap-2">
          <button type="button" onClick={cerrar} className={controlCls(false)}>
            {encolado ? 'Cerrar' : 'Cancelar'}
          </button>
          <button
            type="button"
            onClick={() => void handleSubir()}
            disabled={!archivo || subiendo}
            data-testid="carga-masiva-subir"
            className="inline-flex h-9 items-center gap-1.5 rounded-xl px-4 text-xs font-semibold text-white transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-45"
            style={{ background: WIZARD_CTA_GRADIENT }}
          >
            <Upload className={`h-3.5 w-3.5 ${subiendo ? 'animate-pulse' : ''}`} aria-hidden="true" />
            {subiendo ? 'Subiendo…' : 'Procesar archivo'}
          </button>
        </div>
          </>
        )}
      </div>
    </Modal>
  );
}
