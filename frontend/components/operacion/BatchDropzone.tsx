'use client';

import { useRef, useState } from 'react';
import { FolderOpen } from 'lucide-react';
import {
  archivosDesdeArrastre,
  filtrarUtiles,
  soloPrimerNivel,
} from '@/lib/batch-files';
import {
  BATCH_ACCEPT,
  BATCH_MAX_FILES,
  BATCH_MAX_TOTAL_BYTES,
} from '@/hooks/useProcedureBatchUpload';

interface Props {
  /** Entrega los archivos ya filtrados. El hook valida topes y decide si sigue. */
  onFiles: (files: File[]) => void;
  /** Análisis en curso: la zona se bloquea y anuncia el progreso. */
  busy?: boolean;
  disabled?: boolean;
}

/**
 * Zona de carga masiva (prototipo Lovable Traspaso / Matrícula): borde punteado, copy de arrastre
 * y dos CTAs — sólido «Seleccionar archivos» + outline «Seleccionar carpeta».
 */
export function BatchDropzone({ onFiles, busy = false, disabled = false }: Props) {
  const filesRef = useRef<HTMLInputElement>(null);
  const folderRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const dragDepth = useRef(0);
  const inactivo = disabled || busy;
  const [descartados, setDescartados] = useState(0);

  const entregar = (files: File[], seleccionados: number) => {
    if (inactivo) return;
    setDescartados(files.length === 0 && seleccionados > 0 ? seleccionados : 0);
    if (files.length > 0) onFiles(files);
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    dragDepth.current = 0;
    setDragging(false);
    if (inactivo) return;
    const { utiles, total } = await archivosDesdeArrastre(e.dataTransfer);
    entregar(utiles, total);
  };

  const handleDragEnter = (e: React.DragEvent) => {
    e.preventDefault();
    dragDepth.current += 1;
    if (!inactivo) setDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    dragDepth.current = Math.max(0, dragDepth.current - 1);
    if (dragDepth.current === 0) setDragging(false);
  };

  const handlePick = (e: React.ChangeEvent<HTMLInputElement>, plano: boolean) => {
    const seleccion = Array.from(e.target.files ?? []);
    e.target.value = '';
    entregar(filtrarUtiles(plano ? soloPrimerNivel(seleccion) : seleccion), seleccion.length);
  };

  return (
    <div
      onDrop={(e) => void handleDrop(e)}
      onDragOver={(e) => e.preventDefault()}
      onDragEnter={handleDragEnter}
      onDragLeave={handleDragLeave}
      className="rounded-xl border-2 border-dashed bg-white p-10 text-center shadow-sm transition-all duration-200 hover:shadow-md dark:bg-[#162744]"
      style={{
        borderColor: dragging ? '#557EFF' : '#E2E8F0',
        background: dragging ? '#F0F5FF' : undefined,
        opacity: disabled ? 0.6 : 1,
      }}
      aria-label="Carga masiva de documentos"
    >
      <p className="text-[13px] font-semibold" style={{ color: '#162744' }}>
        {busy
          ? 'Analizando los documentos…'
          : 'Arrastra aquí tus archivos, carpeta o archivo .zip.'}
      </p>
      <p className="mt-1 text-[12px] opacity-70">
        {busy
          ? 'Estamos identificando qué documento hay en cada página. Puede tardar un momento.'
          : 'El sistema organizará automáticamente cada documento.'}
      </p>
      {!busy && (
        <p className="mt-1 text-[11px] opacity-60">
          Máx {BATCH_MAX_FILES} archivos · {BATCH_MAX_TOTAL_BYTES / (1024 * 1024)} MB.
        </p>
      )}

      {descartados > 0 && !busy && (
        <p className="mt-2 text-xs font-medium" style={{ color: '#C23B22' }} role="alert">
          {descartados === 1
            ? 'Ese archivo no es de un tipo que podamos leer. Admitimos PDF, JPG, PNG o .zip.'
            : `Ninguno de los ${descartados} archivos es de un tipo que podamos leer. Admitimos PDF, JPG, PNG o .zip.`}
        </p>
      )}

      <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
        <input
          ref={filesRef}
          type="file"
          multiple
          accept={BATCH_ACCEPT}
          onChange={(e) => handlePick(e, false)}
          className="hidden"
          aria-label="Seleccionar archivos"
        />
        <input
          ref={folderRef}
          type="file"
          multiple
          {...{ webkitdirectory: '', directory: '' }}
          onChange={(e) => handlePick(e, true)}
          className="hidden"
          aria-label="Seleccionar carpeta"
        />

        <button
          type="button"
          onClick={() => filesRef.current?.click()}
          disabled={inactivo}
          className="inline-flex h-11 items-center justify-center rounded-xl px-6 text-[13px] font-semibold text-white transition disabled:cursor-not-allowed disabled:opacity-60"
          style={{ background: '#557EFF' }}
        >
          Seleccionar archivos
        </button>

        <button
          type="button"
          onClick={() => folderRef.current?.click()}
          disabled={inactivo}
          className="inline-flex h-11 items-center justify-center gap-1.5 rounded-xl border bg-white px-6 text-[13px] font-semibold transition hover:bg-[#EFF6FF] disabled:cursor-not-allowed disabled:opacity-60 dark:bg-transparent"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
        >
          <FolderOpen className="h-4 w-4" aria-hidden="true" />
          Seleccionar carpeta
        </button>
      </div>
    </div>
  );
}
