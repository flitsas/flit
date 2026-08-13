'use client';

import { useRef, useState } from 'react';
import { FolderOpen, Upload } from 'lucide-react';
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
 * Zona de carga masiva: arrastrar, elegir archivos, elegir una carpeta o soltar un .zip. Se pinta
 * ENCIMA del checklist y no lo reemplaza — cargar campo a campo sigue siendo el camino de siempre para
 * quien ya sabe qué archivo va en cada casilla.
 */
export function BatchDropzone({ onFiles, busy = false, disabled = false }: Props) {
  const filesRef = useRef<HTMLInputElement>(null);
  const folderRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  // Contador en vez de un booleano: `dragleave` también se dispara al pasar por los hijos, y con un
  // booleano el resaltado parpadea mientras el operador mueve el cursor por dentro de la zona.
  const dragDepth = useRef(0);

  const inactivo = disabled || busy;
  // Aviso local: el filtro por extensión ocurre aquí, así que si se lo come todo hay que decirlo o el
  // operador suelta una carpeta entera y no pasa absolutamente nada.
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
    e.target.value = ''; // permite re-seleccionar lo mismo
    entregar(filtrarUtiles(plano ? soloPrimerNivel(seleccion) : seleccion), seleccion.length);
  };

  return (
    <div
      onDrop={(e) => void handleDrop(e)}
      onDragOver={(e) => e.preventDefault()}
      onDragEnter={handleDragEnter}
      onDragLeave={handleDragLeave}
      className="rounded-2xl border border-dashed p-5 text-center transition-colors"
      style={{
        borderColor: dragging ? '#557EFF' : 'var(--color-border)',
        background: dragging ? 'rgba(85,126,255,0.06)' : undefined,
        opacity: disabled ? 0.6 : 1,
      }}
      aria-label="Carga masiva de documentos"
    >
      <Upload
        className="mx-auto h-6 w-6"
        style={{ color: '#557EFF' }}
        aria-hidden="true"
      />

      <p className="mt-2 text-xs font-semibold">
        {busy
          ? 'Analizando los documentos…'
          : 'Arrastra aquí para importar'}
      </p>

      <p className="mt-1 text-xs opacity-60">
        {busy
          ? 'Estamos identificando qué documento hay en cada página. Puede tardar un momento.'
          : `O selecciona archivos, una carpeta o un .zip. Repartimos cada documento en su casilla y te lo mostramos antes de adjuntarlo. Máx ${BATCH_MAX_FILES} archivos · ${BATCH_MAX_TOTAL_BYTES / (1024 * 1024)} MB.`}
      </p>

      {descartados > 0 && !busy && (
        <p className="mt-1 text-xs font-medium" style={{ color: '#C23B22' }} role="alert">
          {descartados === 1
            ? 'Ese archivo no es de un tipo que podamos leer. Admitimos PDF, JPG, PNG o .zip.'
            : `Ninguno de los ${descartados} archivos es de un tipo que podamos leer. Admitimos PDF, JPG, PNG o .zip.`}
        </p>
      )}

      <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
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
          // webkitdirectory no está en los tipos de React: es el único modo de ofrecer un selector de
          // carpeta en navegador. El árbol se recorta a un nivel en soloPrimerNivel.
          {...{ webkitdirectory: '', directory: '' }}
          onChange={(e) => handlePick(e, true)}
          className="hidden"
          aria-label="Seleccionar carpeta"
        />

        <button
          type="button"
          onClick={() => filesRef.current?.click()}
          disabled={inactivo}
          className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-60"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
        >
          <Upload className="h-3.5 w-3.5" aria-hidden="true" />
          Seleccionar archivos
        </button>

        <button
          type="button"
          onClick={() => folderRef.current?.click()}
          disabled={inactivo}
          className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-60"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
        >
          <FolderOpen className="h-3.5 w-3.5" aria-hidden="true" />
          Seleccionar carpeta
        </button>
      </div>
    </div>
  );
}
