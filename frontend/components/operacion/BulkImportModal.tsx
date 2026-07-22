'use client';

import { useRef, useState } from 'react';
import {
  AlertCircle,
  CheckCircle2,
  Download,
  FileSpreadsheet,
  Upload,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { ProcedureImportReport } from '@/lib/api/types/procedure-runtime';

interface Props {
  open: boolean;
  onClose: () => void;
  /** Se invoca tras una importación con al menos un trámite creado, para refrescar el listado. */
  onImported: () => void;
}

/** Traduce los códigos de error por fila del backend a un texto legible. */
function errorLabel(error: string | null): string {
  if (!error) return '';
  if (error.startsWith('seed_warning:'))
    return 'Creado (no se pudo sembrar placa/VIN)';
  const map: Record<string, string> = {
    oficina_no_encontrada: 'Código de oficina de tránsito no encontrado',
    matricula_inicial_no_habilitada:
      'La compañía no tiene habilitada la matrícula inicial',
    invalid_request: 'Falta la modalidad o el tipo (o vienen ambos sin código)',
    modalidad_not_available: 'No hay un tipo publicado para la modalidad',
    not_found: 'Tipo de trámite no encontrado',
    not_published: 'El tipo de trámite no está publicado',
    invalid_reference: 'Referencia inválida (compañía / usuario / tipo)',
    error_interno: 'Error interno al procesar la fila',
  };
  return map[error] ?? error;
}

function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

export function BulkImportModal({ open, onClose, onImported }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [loading, setLoading] = useState(false);
  const [downloadingTemplate, setDownloadingTemplate] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [report, setReport] = useState<ProcedureImportReport | null>(null);

  if (!open) return null;

  const handleDownloadTemplate = async () => {
    setDownloadingTemplate(true);
    setError(null);
    try {
      const blob = await tramitesClient.downloadImportTemplate();
      saveBlob(blob, 'plantilla-tramites.xlsx');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo descargar la plantilla.');
    } finally {
      setDownloadingTemplate(false);
    }
  };

  const reset = () => {
    setFile(null);
    setError(null);
    setReport(null);
    setLoading(false);
    if (inputRef.current) inputRef.current.value = '';
  };

  const close = () => {
    reset();
    onClose();
  };

  const handleImport = async () => {
    if (!file) return;
    setLoading(true);
    setError(null);
    setReport(null);
    try {
      const result = await tramitesClient.bulkImportInstances(file);
      setReport(result);
      if (result.created > 0) onImported();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo importar el archivo.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      role="dialog"
      aria-modal="true"
      aria-label="Importar trámites masivos"
      onClick={close}
    >
      <div
        className="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-2xl border bg-white p-6 shadow-xl dark:bg-[#0B0F14]"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Encabezado */}
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className="flex items-center gap-2 text-sm font-bold" style={{ color: '#162744' }}>
              <FileSpreadsheet className="h-4 w-4" style={{ color: '#557EFF' }} aria-hidden="true" />
              Importar trámites masivos
            </h2>
            <p className="mt-1 text-[11px] opacity-60">
              Sube un Excel (.xlsx) o CSV para crear trámites en estado borrador. Máximo 500 filas.
              Cada trámite queda como borrador; el resto se completa luego en el asistente.
            </p>
          </div>
          <button
            type="button"
            onClick={close}
            aria-label="Cerrar"
            className="grid h-8 w-8 shrink-0 place-items-center rounded-lg opacity-60 hover:opacity-100"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Plantilla Excel (.xlsx) generada por el backend. */}
        <button
          type="button"
          onClick={handleDownloadTemplate}
          disabled={downloadingTemplate}
          className="mt-4 inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-[11px] font-semibold disabled:opacity-50"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
        >
          <Download className="h-3.5 w-3.5" />
          {downloadingTemplate ? 'Descargando…' : 'Descargar plantilla Excel'}
        </button>

        {/* Selección de archivo */}
        <div className="mt-4">
          <input
            ref={inputRef}
            type="file"
            accept=".xlsx,.csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,text/csv"
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setError(null);
              setReport(null);
            }}
            className="block w-full text-[11px] file:mr-3 file:rounded-lg file:border-0 file:bg-[#557EFF] file:px-4 file:py-2 file:text-[11px] file:font-semibold file:text-white hover:file:opacity-95"
          />
          {file && (
            <p className="mt-2 text-[11px] opacity-60">
              Archivo: <span className="font-semibold">{file.name}</span>
            </p>
          )}
        </div>

        {/* Error de la operación (no de fila) */}
        {error && (
          <div
            className="mt-4 flex items-start gap-2 rounded-xl border px-3 py-2 text-[11px]"
            style={{ borderColor: 'rgba(255,78,0,0.3)', background: 'rgba(255,78,0,0.08)', color: '#c2410c' }}
            role="alert"
          >
            <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        {/* Reporte por fila */}
        {report && (
          <div className="mt-4">
            <div className="flex flex-wrap items-center gap-3 text-[11px] font-semibold">
              <span className="inline-flex items-center gap-1" style={{ color: '#0f766e' }}>
                <CheckCircle2 className="h-3.5 w-3.5" />
                {report.created} creado{report.created === 1 ? '' : 's'}
              </span>
              {report.failed > 0 && (
                <span className="inline-flex items-center gap-1" style={{ color: '#c2410c' }}>
                  <AlertCircle className="h-3.5 w-3.5" />
                  {report.failed} con error
                </span>
              )}
              <span className="opacity-50">de {report.total}</span>
            </div>

            <div className="mt-3 overflow-x-auto rounded-xl border">
              <table className="w-full text-left text-[11px]">
                <thead>
                  <tr className="border-b opacity-60">
                    <th className="px-3 py-2 font-semibold">Fila</th>
                    <th className="px-3 py-2 font-semibold">Dato</th>
                    <th className="px-3 py-2 font-semibold">Resultado</th>
                    <th className="px-3 py-2 font-semibold">Referencia / motivo</th>
                  </tr>
                </thead>
                <tbody>
                  {report.results.map((r) => (
                    <tr key={r.row} className="border-b last:border-0">
                      <td className="px-3 py-2">{r.row}</td>
                      <td className="px-3 py-2">{r.input}</td>
                      <td className="px-3 py-2">
                        <span
                          className="rounded-full px-2 py-0.5 text-[10px] font-semibold"
                          style={
                            r.status === 'created'
                              ? { background: 'rgba(15,118,110,0.12)', color: '#0f766e' }
                              : { background: 'rgba(255,78,0,0.12)', color: '#c2410c' }
                          }
                        >
                          {r.status === 'created' ? 'Creado' : 'Error'}
                        </span>
                      </td>
                      <td className="px-3 py-2">
                        {r.status === 'created'
                          ? r.referenceNumber ?? '—'
                          : errorLabel(r.error)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Acciones */}
        <div className="mt-6 flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={close}
            className="rounded-xl border px-4 py-2 text-[11px] font-semibold"
            style={{ color: '#162744' }}
          >
            {report ? 'Cerrar' : 'Cancelar'}
          </button>
          <button
            type="button"
            onClick={handleImport}
            disabled={!file || loading}
            className="inline-flex items-center gap-1.5 rounded-xl px-5 py-2 text-[11px] font-semibold text-white transition hover:opacity-95 disabled:opacity-50"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            <Upload className={`h-3.5 w-3.5 ${loading ? 'animate-pulse' : ''}`} />
            {loading ? 'Importando…' : 'Importar'}
          </button>
        </div>
      </div>
    </div>
  );
}
