'use client';

import { useEffect, useState } from 'react';
import { FileText } from 'lucide-react';
import { WizardModal } from './WizardModal';
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
  open: openProp,
  onOpenChange,
}: {
  modalidad: WizardModalidad;
  transitOfficeId?: string;
  /**
   * Modo controlado, SIN el enlace: el disparador vive fuera. Lo necesita el carril de consulta del
   * paso 1, cuyo `backdrop-filter` crea bloque contenedor para los descendientes `fixed` — el panel
   * tiene que renderizarse fuera del carril o queda posicionado respecto al icono.
   */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}) {
  const [openState, setOpenState] = useState(false);
  const controlado = openProp !== undefined;
  const open = controlado ? openProp : openState;
  const setOpen = (next: boolean) => {
    if (!controlado) setOpenState(next);
    onOpenChange?.(next);
  };
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
      {!controlado && (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="inline-flex items-center gap-1.5 text-xs font-semibold underline-offset-2 hover:underline"
          style={{ color: '#557EFF' }}
        >
          <FileText className="h-3.5 w-3.5" aria-hidden />
          Ver documentos a tener listos
        </button>
      )}

      {/* Lista de viñetas en modal centrado, como la propuesta. Lo que producción añade sobre el
          diseño —si el documento es obligatorio y su descripción de catálogo— se encaja dentro de
          la misma viñeta: la marca de opcional junto al nombre y la descripción en una línea
          atenuada debajo, sin romper la lectura de arriba abajo. */}
      {open && (
        <WizardModal title="Documentos a tener listos" onClose={() => setOpen(false)}>
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
            <ul className="space-y-2" aria-label="Lista informativa de documentos">
              {items.map((doc) => (
                <li key={doc.documentTypeId} className="flex items-start gap-2">
                  <span
                    aria-hidden="true"
                    className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full"
                    style={{ background: '#557EFF' }}
                  />
                  <div className="min-w-0">
                    <p className="text-xs" style={{ color: '#162744' }}>
                      {doc.nombre}
                      {!doc.obligatorio && <span className="opacity-55"> (opcional)</span>}
                    </p>
                    {doc.descripcion && (
                      <p className="text-xs leading-snug opacity-60">{doc.descripcion}</p>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </WizardModal>
      )}
    </>
  );
}
