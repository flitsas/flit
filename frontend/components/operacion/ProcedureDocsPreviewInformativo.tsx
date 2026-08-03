'use client';

import { useEffect, useState } from 'react';
import { FileText } from 'lucide-react';
import { OtSidePanel } from '@/components/admin/transit-offices/OtSidePanel';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  DocumentoInformativoPreviewItem,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * Guía discreta de documentos del trámite: un enlace abre un panel lateral derecho
 * (OtSidePanel xl, sin scroll) con título, obligatoriedad y descripción en 2 columnas.
 */
export function ProcedureDocsPreviewInformativo({
  modalidad,
  transitOfficeId,
}: {
  modalidad: WizardModalidad;
  transitOfficeId?: string;
}) {
  const [open, setOpen] = useState(false);
  // El resultado se guarda junto a la llave que lo produjo: así `loading` se deriva del render
  // (llave pedida != llave cargada) y no hace falta un setState síncrono dentro del efecto.
  const [result, setResult] = useState<{
    key: string;
    items: DocumentoInformativoPreviewItem[] | null;
    error: string | null;
  } | null>(null);

  const key = `${modalidad}|${transitOfficeId ?? ''}`;
  const loaded = result?.key === key ? result : null;
  const loading = open && loaded === null;
  const items = loaded?.items ?? null;
  const error = loaded?.error ?? null;

  useEffect(() => {
    if (!open) return;
    let active = true;
    void tramitesClient
      .fetchDocumentRequirementsPreview(modalidad, transitOfficeId)
      .then((list) => {
        if (active) setResult({ key, items: list, error: null });
      })
      .catch(() => {
        if (active) {
          setResult({ key, items: null, error: 'No se pudo cargar la lista de documentos.' });
        }
      });
    return () => {
      active = false;
    };
  }, [open, key, modalidad, transitOfficeId]);

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="inline-flex items-center gap-1.5 text-[11px] font-semibold underline-offset-2 hover:underline"
        style={{ color: '#557EFF' }}
      >
        <FileText className="h-3.5 w-3.5" aria-hidden />
        Ver documentos a tener listos
      </button>

      <OtSidePanel
        open={open}
        title="Documentos a tener listos"
        ariaLabel="Guía de documentos del trámite"
        onClose={() => setOpen(false)}
        width="xl"
        scrollable={false}
      >
        <div className="flex h-full flex-col gap-3 overflow-hidden">
          <div
            className="shrink-0 rounded-xl border px-3 py-2"
            style={{ borderColor: '#DDE5F0', background: '#EEF5FF' }}
          >
            <p className="text-[11px] leading-snug" style={{ color: '#162744' }}>
              Esta es una guía de lo que deberías tener preparado para este trámite antes de
              continuar. No sustituye la carga de documentos más adelante en el asistente.
            </p>
          </div>

          {loading && (
            <p className="text-xs opacity-70" role="status" aria-live="polite">
              Cargando documentos…
            </p>
          )}
          {error && (
            <p className="text-xs" style={{ color: '#FF4E00' }} role="alert">
              {error}
            </p>
          )}
          {!loading && !error && items && items.length === 0 && (
            <p className="text-xs opacity-70" role="status">
              No hay documentos configurados para este trámite.
            </p>
          )}
          {!loading && !error && items && items.length > 0 && (
            <ul
              className="grid min-h-0 flex-1 grid-cols-2 content-start gap-2 overflow-hidden"
              aria-label="Lista informativa de documentos"
            >
              {items.map((doc) => (
                <li
                  key={doc.documentTypeId}
                  className="flex min-h-0 flex-col rounded-xl border px-2.5 py-2"
                  style={{ borderColor: '#DDE5F0' }}
                >
                  <div className="flex items-start justify-between gap-1.5">
                    <p
                      className="line-clamp-2 text-xs font-semibold leading-tight"
                      style={{ color: '#162744' }}
                      title={doc.nombre}
                    >
                      {doc.nombre}
                    </p>
                    <span
                      className="shrink-0 rounded-full px-1.5 py-0.5 text-[9px] font-semibold"
                      style={{
                        background: doc.obligatorio ? '#557EFF22' : '#94A3B822',
                        color: doc.obligatorio ? '#557EFF' : '#64748B',
                      }}
                    >
                      {doc.obligatorio ? 'Obligatorio' : 'Opcional'}
                    </span>
                  </div>
                  {doc.descripcion ? (
                    <p
                      className="mt-1 line-clamp-4 text-[10px] leading-snug"
                      style={{ color: '#59677D' }}
                      title={doc.descripcion}
                    >
                      {doc.descripcion}
                    </p>
                  ) : (
                    <p className="mt-1 text-[10px] opacity-50" style={{ color: '#7D8798' }}>
                      Sin descripción en el catálogo.
                    </p>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </OtSidePanel>
    </>
  );
}
