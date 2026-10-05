'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertTriangle, ChevronLeft, ChevronRight, ImageOff, RefreshCw } from 'lucide-react';
import { Modal } from '@flit/ui/Modal';
import { useWizardFocusTrap } from '@/components/operacion/use-wizard-focus-trap';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import { MANUAL_IMAGE_KINDS, type ManualImageKind } from '@/lib/api/types/manual-review';

/**
 * Las 4 capturas de una validación manual (HU-C6): rostro, anverso, reverso y firma, con visor
 * ampliable y navegación por teclado. Las imágenes son protegidas: se piden con el JWT en la cabecera
 * (`client.getManualImage`), se muestran con una URL de blob y esa URL se revoca al cerrar o cambiar de
 * registro. Nunca se guarda ni se muestra una URL permanente.
 */

/** Título visible y `alt` funcional: describen la captura, nunca a la persona. */
export const MANUAL_IMAGE_TEXT: Record<ManualImageKind, { title: string; alt: string }> = {
  rostro: { title: 'Rostro', alt: 'fotografía de rostro' },
  anverso: { title: 'Documento (anverso)', alt: 'documento anverso' },
  reverso: { title: 'Documento (reverso)', alt: 'documento reverso' },
  firma: { title: 'Firma', alt: 'firma' },
};

type Estado =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; url: string };

type Estados = Record<ManualImageKind, Estado>;

/** Carga las capturas disponibles y administra el ciclo de vida de sus URLs de blob. */
function useManualImages(
  client: ManualReviewClient,
  id: string,
  images: { kind: ManualImageKind; available: boolean }[],
) {
  const [estados, setEstados] = useState<Estados>(() => inicial(images));
  const urls = useRef<Partial<Record<ManualImageKind, string>>>({});
  const disponibles = images
    .filter((i) => i.available)
    .map((i) => i.kind)
    .join(',');

  const revocar = useCallback((kind?: ManualImageKind) => {
    const kinds = kind ? [kind] : (Object.keys(urls.current) as ManualImageKind[]);
    for (const k of kinds) {
      const u = urls.current[k];
      if (u) URL.revokeObjectURL(u);
      delete urls.current[k];
    }
  }, []);

  const cargar = useCallback(
    async (kind: ManualImageKind, signal?: AbortSignal) => {
      setEstados((s) => ({ ...s, [kind]: { status: 'loading' } }));
      try {
        const blob = await client.getManualImage(id, kind, signal);
        if (signal?.aborted) return;
        revocar(kind);
        const url = URL.createObjectURL(blob);
        urls.current[kind] = url;
        setEstados((s) => ({ ...s, [kind]: { status: 'ready', url } }));
      } catch {
        if (signal?.aborted) return;
        setEstados((s) => ({ ...s, [kind]: { status: 'error' } }));
      }
    },
    [client, id, revocar],
  );

  useEffect(() => {
    const ctrl = new AbortController();
    const kinds = disponibles ? (disponibles.split(',') as ManualImageKind[]) : [];
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reinicio al cambiar de registro + carga asíncrona
    setEstados(inicial(MANUAL_IMAGE_KINDS.map((kind) => ({ kind, available: kinds.includes(kind) }))));
    kinds.forEach((k) => void cargar(k, ctrl.signal));
    return () => {
      ctrl.abort();
      revocar();
    };
  }, [id, disponibles, cargar, revocar]);

  return { estados, reintentar: (kind: ManualImageKind) => void cargar(kind) };
}

function inicial(images: { kind: ManualImageKind; available: boolean }[]): Estados {
  const out = {} as Estados;
  for (const kind of MANUAL_IMAGE_KINDS) {
    out[kind] = images.find((i) => i.kind === kind)?.available ? { status: 'loading' } : { status: 'unavailable' };
  }
  return out;
}

export function ManualImageGallery({
  client,
  id,
  images,
  onViewerChange,
}: {
  client: ManualReviewClient;
  id: string;
  images: { kind: ManualImageKind; available: boolean }[];
  /** Avisa si el visor está abierto, para que el modal padre no se cierre con el mismo Escape. */
  onViewerChange?: (open: boolean) => void;
}) {
  const { estados, reintentar } = useManualImages(client, id, images);
  const [ampliada, setAmpliada] = useState<ManualImageKind | null>(null);

  useEffect(() => {
    onViewerChange?.(ampliada !== null);
  }, [ampliada, onViewerChange]);

  return (
    <>
      <ul className="grid grid-cols-2 gap-3 lg:grid-cols-4" aria-label="Capturas del cliente">
        {MANUAL_IMAGE_KINDS.map((kind) => {
          const estado = estados[kind];
          const { title, alt } = MANUAL_IMAGE_TEXT[kind];
          return (
            <li key={kind} className="flex flex-col gap-1.5">
              <p className="text-xs font-semibold text-[#162744] dark:text-white">{title}</p>
              <div className="flex aspect-[4/3] items-center justify-center overflow-hidden rounded-xl border border-[#DFE5ED] bg-[#EEF5FF] dark:border-white/10 dark:bg-[#162744]">
                {estado.status === 'ready' ? (
                  <button
                    type="button"
                    onClick={() => setAmpliada(kind)}
                    aria-label={`Ampliar ${title.toLowerCase()}`}
                    className="h-full w-full focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-[#557EFF]"
                  >
                    {/* eslint-disable-next-line @next/next/no-img-element -- URL de blob de una imagen protegida */}
                    <img src={estado.url} alt={alt} className="h-full w-full object-contain" />
                  </button>
                ) : estado.status === 'loading' ? (
                  <p role="status" className="text-xs text-[#59677D] dark:text-white/70">
                    Cargando {title.toLowerCase()}…
                  </p>
                ) : estado.status === 'error' ? (
                  <div role="alert" className="flex flex-col items-center gap-2 px-2 text-center">
                    <AlertTriangle className="h-5 w-5 text-[#FF4E00]" aria-hidden />
                    <p className="text-xs font-medium">No se pudo cargar la imagen.</p>
                    <button
                      type="button"
                      onClick={() => reintentar(kind)}
                      aria-label={`Reintentar ${title.toLowerCase()}`}
                      className="inline-flex items-center gap-1 rounded-lg border border-[#DFE5ED] bg-white px-2.5 py-1 text-xs font-semibold text-[#162744] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
                    >
                      <RefreshCw className="h-3.5 w-3.5" aria-hidden /> Reintentar
                    </button>
                  </div>
                ) : (
                  <div className="flex flex-col items-center gap-1 text-[#59677D] dark:text-white/70">
                    <ImageOff className="h-5 w-5" aria-hidden />
                    <p className="text-xs">Sin captura</p>
                  </div>
                )}
              </div>
            </li>
          );
        })}
      </ul>
      {ampliada && (
        <ManualImageViewer
          estados={estados}
          current={ampliada}
          onChange={setAmpliada}
          onClose={() => setAmpliada(null)}
        />
      )}
    </>
  );
}

function ManualImageViewer({
  estados,
  current,
  onChange,
  onClose,
}: {
  estados: Estados;
  current: ManualImageKind;
  onChange: (kind: ManualImageKind) => void;
  onClose: () => void;
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  const navegables = MANUAL_IMAGE_KINDS.filter((k) => estados[k].status === 'ready');
  const index = Math.max(0, navegables.indexOf(current));
  const estado = estados[current];
  const { title, alt } = MANUAL_IMAGE_TEXT[current];

  const ir = useCallback(
    (delta: number) => {
      if (navegables.length < 2) return;
      onChange(navegables[(index + delta + navegables.length) % navegables.length]);
    },
    [navegables, index, onChange],
  );

  useWizardFocusTrap(panelRef, { active: true });

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight') {
        e.preventDefault();
        ir(1);
      } else if (e.key === 'ArrowLeft') {
        e.preventDefault();
        ir(-1);
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [ir]);

  const botonNav =
    'inline-flex h-10 w-10 items-center justify-center rounded-full border border-[#DFE5ED] bg-white text-[#162744] transition hover:bg-[#EEF5FF] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]';

  return (
    <Modal open onClose={onClose} title={title} size="xl" zClassName="z-[110]">
      <div ref={panelRef} tabIndex={-1} className="flex flex-col gap-3 outline-none">
        <div className="flex items-center justify-center gap-3">
          <button type="button" onClick={() => ir(-1)} disabled={navegables.length < 2} aria-label="Imagen anterior" className={botonNav}>
            <ChevronLeft className="h-5 w-5" aria-hidden />
          </button>
          <div className="flex min-h-[320px] flex-1 items-center justify-center overflow-hidden rounded-xl border border-[#DFE5ED] bg-[#EEF5FF]">
            {estado.status === 'ready' && (
              // eslint-disable-next-line @next/next/no-img-element -- URL de blob de una imagen protegida
              <img src={estado.url} alt={alt} className="max-h-[60vh] w-full object-contain" />
            )}
          </div>
          <button type="button" onClick={() => ir(1)} disabled={navegables.length < 2} aria-label="Imagen siguiente" className={botonNav}>
            <ChevronRight className="h-5 w-5" aria-hidden />
          </button>
        </div>
        <p className="text-center text-xs text-[#59677D] dark:text-white/70" role="status" aria-live="polite">
          {title} · {index + 1} de {navegables.length}. Usa las flechas del teclado para cambiar de imagen.
        </p>
        <div className="flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border border-[#DFE5ED] px-4 py-2 text-sm font-medium text-[#162744] transition hover:bg-[#162744]/[0.04] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] dark:text-white"
          >
            Cerrar imagen
          </button>
        </div>
      </div>
    </Modal>
  );
}
