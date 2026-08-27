'use client';

import { useEffect, useState } from 'react';
import { FileText, Info } from 'lucide-react';
import { WizardModal } from './WizardModal';
import { ALLOWED_MIME } from './DocumentChecklist';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DocumentCatalogCaption } from '@/components/shared/DocumentCatalogCaption';
import type {
  DocumentoInformativoPreviewItem,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * Formatos admitidos, DERIVADOS de la misma constante que gobierna el `accept` de la carga.
 *
 * Escribirlos a mano es como se desincronizan: la maqueta de esta pantalla decía «PDF, JPG o PNG» y
 * la carga acepta además WEBP, así que el aviso habría desanimado a subir un archivo válido.
 */
const FORMATOS_ADMITIDOS = ALLOWED_MIME.map((m) => m.split('/')[1].toUpperCase())
  .map((f) => (f === 'JPEG' ? 'JPG' : f))
  .join(', ')
  .replace(/, ([^,]*)$/, ' o $1');

/**
 * Tonos de los dos grupos, tomados de `color.badge` del token file — ya vienen con el contraste
 * comprobado, que es la razón para no usar los puros de marca aquí.
 *
 * Paleta invertida a propósito: los obligatorios usan el ámbar (antes de opcionales) y los
 * opcionales el azul (antes de obligatorios). El punto de viñeta ámbar usa `#B45309` y no el
 * `#F59E0B` puro, que no llega a 3:1 sobre blanco.
 */
const TONO_OBLIGATORIO = {
  bg: 'rgba(245, 158, 11, 0.15)',
  fg: '#B45309',
  border: 'rgba(245, 158, 11, 0.35)',
  punto: '#B45309',
};

const TONO_OPCIONAL = {
  bg: 'rgba(85, 126, 255, 0.14)',
  fg: '#3B4FD6',
  border: 'rgba(85, 126, 255, 0.35)',
  punto: '#557EFF',
};

/**
 * Un grupo de documentos —obligatorios u opcionales— en la forma de la propuesta: cabecera tintada
 * con icono y título de color, y debajo la lista con viñetas del mismo tono.
 *
 * El tono NO es decorativo: separa de un vistazo lo que el trámite exige de lo que solo suma. Antes
 * los dos grupos iban mezclados en una sola lista y la diferencia era un «(opcional)» atenuado al
 * final del nombre — lo más fácil de pasar por alto justo cuando el gestor está reuniendo papeles.
 */
function GrupoDocumentos({
  titulo,
  tono,
  documentos,
}: {
  titulo: string;
  /** Tonos de `color.badge`: ya vienen con el contraste comprobado sobre su propio fondo. */
  tono: { bg: string; fg: string; border: string; punto: string };
  documentos: DocumentoInformativoPreviewItem[];
}) {
  if (documentos.length === 0) return null;

  return (
    <section
      className="rounded-xl border p-3.5"
      style={{ background: tono.bg, borderColor: tono.border }}
      aria-label={titulo}
    >
      <div className="flex items-center gap-2">
        <span
          className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-white dark:bg-[#162744]"
          aria-hidden="true"
        >
          <FileText className="h-3.5 w-3.5" style={{ color: tono.fg }} />
        </span>
        <h4 className="text-xs font-bold" style={{ color: tono.fg }}>
          {titulo}
        </h4>
      </div>

      <hr className="my-2.5 border-0 border-t" style={{ borderColor: tono.border }} />

      <ul className="space-y-2">
        {documentos.map((doc) => (
          <li key={doc.documentTypeId} className="flex items-start gap-2">
            <span
              aria-hidden="true"
              className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full"
              style={{ background: tono.punto }}
            />
            <div className="min-w-0">
              <p className="text-xs" style={{ color: '#162744' }}>
                <DocumentCatalogCaption nombre={doc.nombre} codigo={doc.codigo} />
              </p>
              {/* La descripción del catálogo no está en la maqueta porque su ejemplo no tenía
                  ninguna; se conserva porque es dato real y es lo que desambigua dos documentos de
                  nombre parecido. */}
              {doc.descripcion && (
                <p className="text-xs leading-snug opacity-70">{doc.descripcion}</p>
              )}
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * Guía discreta de documentos del trámite: un enlace abre un panel lateral derecho
 * (OtSidePanel xl, sin scroll) con título, obligatoriedad y descripción en 2 columnas.
 */
export function ProcedureDocsPreviewInformativo({
  procedureTypeCode,
  transitOfficeId,
  open: openProp,
  onOpenChange,
}: {
  /** `code` del tipo en el catálogo (ADR-0050). */
  procedureTypeCode: string;
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

  const key = `${procedureTypeCode}|${transitOfficeId ?? ''}`;
  const loaded = result?.key === key ? result : null;
  const loading = open && loaded === null;
  const items = loaded?.items ?? null;
  const error = loaded?.error ?? null;

  useEffect(() => {
    if (!open) return;
    let active = true;
    void tramitesClient
      .fetchDocumentRequirementsPreview(procedureTypeCode, transitOfficeId)
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
  }, [open, key, procedureTypeCode, transitOfficeId]);

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

      {/* Los documentos se agrupan por obligatoriedad, en la forma de la propuesta: dos bloques
          tintados con su cabecera, y al pie el aviso de formato y la salida. */}
      {/* El título conserva la raíz del disparador que lo abre («Documentos a tener listos», en el
          carril), igual que «Escrituras vigentes» → «Escrituras vigentes de la compañía». La maqueta
          lo rotulaba «Documentación», que rompe ese par: el gestor pulsa una cosa y aterriza en
          otra. Lo que sí se toma de la maqueta es la ESTRUCTURA: los dos grupos tintados, el aviso
          de formato y la salida. */}
      {open && (
        <WizardModal title="Documentos a tener listos" onClose={() => setOpen(false)}>
          {loading && (
            <p className="text-xs opacity-70" role="status" aria-live="polite">
              Cargando documentos…
            </p>
          )}
          {error && (
            <p className="text-xs" style={{ color: '#C2410C' }} role="alert">
              {error}
            </p>
          )}
          {!loading && !error && items && items.length === 0 && (
            <p className="text-xs opacity-70" role="status">
              No hay documentos configurados para este trámite.
            </p>
          )}
          {!loading && !error && items && items.length > 0 && (
            <>
              <div className="space-y-3">
                <GrupoDocumentos
                  titulo="Documentos obligatorios"
                  tono={TONO_OBLIGATORIO}
                  documentos={items.filter((d) => d.obligatorio)}
                />
                <GrupoDocumentos
                  titulo="Documentos opcionales"
                  tono={TONO_OPCIONAL}
                  documentos={items.filter((d) => !d.obligatorio)}
                />
              </div>

              <div
                className="mt-4 flex flex-wrap items-center justify-between gap-3 border-0 border-t pt-3"
                style={{ borderColor: '#DFE5ED' }}
              >
                <p className="flex min-w-0 items-center gap-1.5 text-xs opacity-70">
                  <Info className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  Los documentos deben estar legibles y en formato {FORMATOS_ADMITIDOS}.
                </p>
                {/* «Entendido», no «Cerrar»: la X del modal ya lleva ese nombre, y el pie no repite la
                    salida sino que acusa haber leído la lista. Precedente: DocumentInUseDialog. */}
                <button
                  type="button"
                  onClick={() => setOpen(false)}
                  className="shrink-0 rounded-xl border bg-white px-4 py-2 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 hover:border-[#557EFF] dark:bg-[#162744]"
                  style={{ borderColor: '#DFE5ED', color: '#162744' }}
                >
                  Entendido
                </button>
              </div>
            </>
          )}
        </WizardModal>
      )}
    </>
  );
}
