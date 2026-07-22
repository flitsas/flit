'use client';

import { useState } from 'react';
import type { ProcedureTypeSummary, PublicationStatus } from '@/lib/api/types/procedure-parametrization';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { EmptyState } from './states/EmptyState';
import { LoadingState } from './states/LoadingState';
import { ErrorState } from './states/ErrorState';

const STATUS_STYLES: Record<PublicationStatus, { tone: StatusTone; label: string }> = {
  draft: { tone: 'info', label: 'Borrador' },
  published: { tone: 'success', label: 'Publicado' },
  archived: { tone: 'neutral', label: 'Archivado' },
};

const FAMILY_LABELS: Record<string, string> = {
  MATRICULAS: 'Matrículas',
  TRASPASO: 'Traspaso',
  OTROS: 'Otros Trámites',
};

interface ProcedureTypeListProps {
  items: ProcedureTypeSummary[];
  status: 'idle' | 'loading' | 'success' | 'error';
  error: string | null;
  onNew: () => void;
  onEdit: (id: string) => void;
  onReload: () => void;
  onPublish: (id: string) => Promise<ProcedureTypeSummary>;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  publishSuccessMessage?: string | null;
}

export function ProcedureTypeList({
  items,
  status,
  error,
  onNew,
  onEdit,
  onReload,
  onPublish,
  selectedId,
  onSelect,
  publishSuccessMessage,
}: ProcedureTypeListProps) {
  const [publishingId, setPublishingId] = useState<string | null>(null);
  const [publishError, setPublishError] = useState<string | null>(null);

  const handlePublish = async (id: string) => {
    setPublishingId(id);
    setPublishError(null);
    try {
      await onPublish(id);
    } catch (err) {
      setPublishError(err instanceof Error ? err.message : 'Error al publicar');
    } finally {
      setPublishingId(null);
    }
  };

  if (status === 'loading') return <LoadingState />;
  if (status === 'error' && error) return <ErrorState message={error} onRetry={onReload} />;
  if (status === 'success' && items.length === 0) return <EmptyState onNew={onNew} />;

  return (
    <div className="flex-1 min-h-0 flex flex-col gap-3 overflow-hidden">
      {publishSuccessMessage && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#00DBD5', background: 'rgba(0,219,213,0.08)', color: '#00DBD5' }}
          role="status"
          aria-live="polite"
        >
          {publishSuccessMessage}
        </div>
      )}

      {publishError && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {publishError}
        </div>
      )}

      <div
        className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-xl shrink-0"
        style={{ background: '#DFE5ED', color: '#162744' }}
        role="row"
        aria-label="Encabezado de tabla de parametrizaciones"
      >
        <div className="col-span-3">Familia</div>
        <div className="col-span-3">Código</div>
        <div className="col-span-2">Estado</div>
        <div className="col-span-4 text-right">Acciones</div>
      </div>

      <div
        className="flex-1 min-h-0 overflow-y-auto space-y-2"
        role="list"
        aria-label="Lista de parametrizaciones"
      >
        {items.map((item) => {
          const statusStyle = STATUS_STYLES[item.publicationStatus];
          const isDraft = item.publicationStatus === 'draft';
          const isSelected = selectedId === item.id;
          return (
            <div
              key={item.id}
              onClick={() => isDraft && onSelect(isSelected ? null : item.id)}
              onKeyDown={(e) => {
                if (isDraft && (e.key === 'Enter' || e.key === ' ')) {
                  e.preventDefault();
                  onSelect(isSelected ? null : item.id);
                }
              }}
              role={isDraft ? 'button' : 'listitem'}
              tabIndex={isDraft ? 0 : undefined}
              aria-pressed={isDraft ? isSelected : undefined}
              aria-label={
                isDraft
                  ? `${item.code}, borrador${isSelected ? ', seleccionado' : ''}`
                  : `${item.code}, publicado`
              }
              className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs transition"
              style={{
                borderColor: isSelected ? '#00DBD5' : '#DFE5ED',
                boxShadow: isSelected ? '0 0 0 1px #00DBD5' : undefined,
                cursor: isDraft ? 'pointer' : 'default',
              }}
            >
              <div className="col-span-3 font-medium">
                {FAMILY_LABELS[item.family] ?? item.family}
              </div>
              <div className="col-span-3 font-mono font-bold opacity-80">{item.code}</div>
              <div className="col-span-2">
                <StatusBadge label={statusStyle.label} tone={statusStyle.tone} />
              </div>
              <div className="col-span-4 flex items-center justify-end gap-2">
                {isDraft && (
                  <>
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        onEdit(item.id);
                      }}
                      className="text-[10px] font-semibold px-2 py-1 rounded-lg border"
                      style={{ borderColor: '#557EFF', color: '#557EFF' }}
                      aria-label={`Editar ${item.name}`}
                    >
                      Editar
                    </button>
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        void handlePublish(item.id);
                      }}
                      disabled={publishingId === item.id}
                      className="text-[10px] font-semibold px-2 py-1 rounded-lg border disabled:opacity-50"
                      style={{ borderColor: '#00DBD5', color: '#00DBD5' }}
                      aria-label={`Publicar ${item.name}`}
                    >
                      {publishingId === item.id ? 'Publicando…' : 'Publicar'}
                    </button>
                  </>
                )}
                {item.publicationStatus === 'published' && (
                  <span
                    className="text-[10px] opacity-50 px-2 py-1"
                    aria-label={`${item.name} está publicado, no se puede editar estructura`}
                  >
                    Solo lectura
                  </span>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
