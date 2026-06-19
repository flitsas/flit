'use client';

import { useRef, useState } from 'react';
import { useProcedureDocuments } from '@/hooks/useProcedureDocuments';
import type {
  ChecklistItemView,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
}

/** MIME permitidos por el contrato. */
export const ALLOWED_MIME = [
  'application/pdf',
  'image/jpeg',
  'image/png',
  'image/webp',
] as const;

/** Tamaño máximo: 20 MB. */
export const MAX_SIZE_BYTES = 20 * 1024 * 1024;

const ALLOWED_LABEL = 'PDF, JPG, PNG o WEBP';

/**
 * Valida mime y tamaño antes de subir. Pura y testeable de forma aislada.
 * Devuelve un mensaje de error claro, o null si el archivo es aceptable.
 */
export function validateFile(file: File): string | null {
  if (!(ALLOWED_MIME as readonly string[]).includes(file.type)) {
    return `Tipo de archivo no permitido. Usa ${ALLOWED_LABEL}.`;
  }
  if (file.size > MAX_SIZE_BYTES) {
    return 'El archivo supera el máximo de 20 MB.';
  }
  return null;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * Caja de subida por ítem del checklist. Presentacional + un input de archivo
 * oculto; valida cliente-side antes de delegar la subida al hook.
 */
function DocumentSlot({
  item,
  attachment,
  uploading,
  deleting,
  onUpload,
  onRemove,
}: {
  item: ChecklistItemView;
  attachment: ProcedureAttachment | undefined;
  uploading: boolean;
  deleting: boolean;
  onUpload: (file: File) => void;
  onRemove: (attachmentId: string) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  const done = item.satisfied || !!attachment;
  const busy = uploading || deleting;

  const handlePick = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // permite re-seleccionar el mismo archivo
    if (!file) return;
    const err = validateFile(file);
    setLocalError(err);
    if (err) return;
    onUpload(file);
  };

  return (
    <li
      className="rounded-xl border p-3"
      style={{ borderColor: done ? '#8CC63F' : '#DFE5ED' }}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-1.5">
            {done && (
              <span style={{ color: '#8CC63F' }} aria-hidden="true">
                ✓
              </span>
            )}
            <span className="text-xs font-semibold">{item.label}</span>
            {item.obligatorio ? (
              <span
                className="rounded px-1.5 py-0.5 text-[9px] font-bold uppercase"
                style={{ background: 'rgba(255,78,0,0.10)', color: '#FF4E00' }}
              >
                Obligatorio
              </span>
            ) : (
              <span className="text-[10px] opacity-50 font-normal">
                (opcional)
              </span>
            )}
          </div>
          {attachment && (
            <p className="mt-1 text-[11px] opacity-70 truncate">
              {attachment.filename} · {formatSize(attachment.sizeBytes)}
            </p>
          )}
        </div>

        <div className="flex shrink-0 items-center gap-2">
          <input
            ref={inputRef}
            type="file"
            accept={ALLOWED_MIME.join(',')}
            onChange={handlePick}
            className="hidden"
            aria-label={`Subir ${item.label}`}
          />
          <button
            type="button"
            onClick={() => inputRef.current?.click()}
            disabled={busy}
            className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:cursor-not-allowed disabled:opacity-60"
            style={{ borderColor: '#557EFF', color: '#557EFF' }}
          >
            {uploading ? 'Subiendo…' : attachment ? 'Reemplazar' : 'Subir'}
          </button>
          {attachment && (
            <button
              type="button"
              onClick={() => onRemove(attachment.id)}
              disabled={busy}
              className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:cursor-not-allowed disabled:opacity-60"
              style={{ borderColor: '#FF4E00', color: '#FF4E00' }}
              aria-label={`Borrar ${item.label}`}
            >
              {deleting ? 'Borrando…' : 'Borrar'}
            </button>
          )}
        </div>
      </div>

      {localError && (
        <p
          className="mt-1.5 text-[10px]"
          style={{ color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {localError}
        </p>
      )}
    </li>
  );
}

/**
 * Grid de subida de documentos guiado por el checklist del trámite.
 * Reutilizable: dado un `instanceId`, carga el checklist (qué docTipos exige
 * la tipología) y los adjuntos, marca ✓ los satisfechos, valida mime/tamaño
 * antes de subir y resume "faltan N obligatorios / completo".
 */
export function DocumentChecklist({ instanceId }: Props) {
  const { state, upload, remove, clearError } =
    useProcedureDocuments(instanceId);
  const { checklist, attachments, uploadingTipo, deletingId } = state;

  const attachmentByTipo = new Map<string, ProcedureAttachment>();
  for (const a of attachments) {
    if (!attachmentByTipo.has(a.tipo)) attachmentByTipo.set(a.tipo, a);
  }

  const items = checklist?.items ?? [];

  return (
    <section
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14] mt-4"
      style={{ borderColor: '#DFE5ED' }}
      aria-label="Documentos del trámite"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <div>
          <h4 className="text-sm font-bold">Documentos requeridos</h4>
          <p className="text-[11px] opacity-60">
            Adjunta los documentos que exige el trámite ({ALLOWED_LABEL}, máx
            20 MB).
          </p>
        </div>
        {checklist && (
          <span
            className="shrink-0 rounded-full px-3 py-1 text-[11px] font-bold"
            style={
              checklist.completo
                ? { background: 'rgba(140,198,63,0.15)', color: '#8CC63F' }
                : { background: 'rgba(249,172,0,0.15)', color: '#F9AC00' }
            }
            role="status"
            aria-live="polite"
          >
            {checklist.completo
              ? 'Documentos completos'
              : `Faltan ${checklist.faltanObligatorios} obligatorio${
                  checklist.faltanObligatorios === 1 ? '' : 's'
                }`}
          </span>
        )}
      </div>

      {state.error && (
        <div
          className="rounded-xl p-3 text-xs border mb-3 flex items-center justify-between gap-3"
          style={{
            borderColor: '#FF4E00',
            background: 'rgba(255,78,0,0.06)',
            color: '#FF4E00',
          }}
          role="alert"
          aria-live="polite"
        >
          <span>{state.error}</span>
          <button
            type="button"
            onClick={clearError}
            className="font-bold"
            aria-label="Descartar error"
          >
            ×
          </button>
        </div>
      )}

      {items.length === 0 ? (
        <p className="text-[11px] opacity-60">
          {state.loading
            ? 'Cargando documentos requeridos…'
            : 'Este trámite no requiere documentos.'}
        </p>
      ) : (
        <ul className="space-y-2" aria-label="Checklist de documentos">
          {items.map((item) => {
            const tipo = item.docTipo ?? item.key;
            const attachment = attachmentByTipo.get(tipo);
            return (
              <DocumentSlot
                key={item.key}
                item={item}
                attachment={attachment}
                uploading={uploadingTipo === tipo}
                deleting={!!attachment && deletingId === attachment.id}
                onUpload={(file) => void upload(tipo, file)}
                onRemove={(id) => void remove(id)}
              />
            );
          })}
        </ul>
      )}
    </section>
  );
}
