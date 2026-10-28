"use client";

// HU #14076 AC3/AC4 — Vista previa del borrador: cómo se vería el contenido en el portal si se publicara hoy, sin
// publicarlo. Es un componente simple sobre el contenido del borrador (solo lo ACTIVO, en su orden): reutiliza los
// textos y el orden, no el código del portal. Un enlace legal sin documento oficial se indica como tal y el portal
// conserva su aviso actual.
import { useCallback, useEffect, useState } from "react";
import { Eye } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Modal } from "@/components/atom/Modal";
import { formatFechaHora } from "@/lib/format/date";
import {
  ENLACES_LEGALES,
  ENLACE_LABEL,
  fetchBorradorPn,
  type BorradorContenidoPn,
  type ContenidoPn,
} from "@/lib/api/admin-pn-contenido";
import { BannerImagen } from "./BannerImagen";

export function PortalPreview({ contenido }: { contenido: ContenidoPn }) {
  const banners = [...contenido.banners].sort((a, b) => a.orden - b.orden);
  const preguntas = [...contenido.preguntas].sort((a, b) => a.orden - b.orden);
  return (
    <div
      data-testid="pn-portal-preview"
      className="flex flex-col gap-6 rounded-2xl border border-[#DFE5ED] bg-[#F4F8FF] p-4 dark:border-white/10 dark:bg-white/5"
    >
      <section aria-label="Banners" className="flex flex-col gap-3">
        {banners.length === 0 ? (
          <p className="text-xs text-[#59677D] dark:text-white/70">No hay banners activos.</p>
        ) : (
          banners.map((b) => (
            <figure key={b.id} className="flex flex-col gap-1">
              <BannerImagen id={b.id} alt={b.textoAlternativo} className="h-32 w-full max-w-xl rounded-xl object-cover" />
              <figcaption className="text-sm font-semibold text-flit-primary dark:text-white">{b.titulo}</figcaption>
            </figure>
          ))
        )}
      </section>

      <section aria-label="Preguntas frecuentes" className="flex flex-col gap-2">
        <h3 className="text-sm font-bold text-flit-primary dark:text-white">Preguntas frecuentes</h3>
        {preguntas.length === 0 ? (
          <p className="text-xs text-[#59677D] dark:text-white/70">No hay preguntas activas.</p>
        ) : (
          preguntas.map((p) => (
            <details key={p.id} className="rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 dark:border-white/10 dark:bg-[#0B0F14]">
              <summary className="cursor-pointer text-sm font-semibold text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand dark:text-white">
                {p.pregunta}
              </summary>
              {/* Texto plano: se muestra como texto, nunca como HTML. */}
              <p className="mt-2 whitespace-pre-line text-sm text-[#59677D] dark:text-white/78">{p.respuesta}</p>
            </details>
          ))
        )}
      </section>

      <section aria-label="Enlaces" className="flex flex-col gap-1">
        <h3 className="text-sm font-bold text-flit-primary dark:text-white">Enlaces</h3>
        <ul className="flex flex-col gap-1 text-sm">
          {contenido.enlaces.map((e) => (
            <li key={e.codigo}>
              <span className="font-semibold">{ENLACE_LABEL[e.codigo]}:</span>{" "}
              {e.url ? (
                <span className="break-all text-[color:var(--flit-brand-ink)]">{e.url}</span>
              ) : (
                <span className="text-[#59677D] dark:text-white/70">
                  {ENLACES_LEGALES.includes(e.codigo) ? "Sin documento oficial" : "Sin valor"}
                </span>
              )}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

export function VistaPreviaModal({ onClose }: { onClose: () => void }) {
  const [borrador, setBorrador] = useState<BorradorContenidoPn | null>(null);
  const [status, setStatus] = useState<UiStatus>("loading");

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const b = await fetchBorradorPn(signal);
      if (signal?.aborted) return;
      setBorrador(b);
      setStatus("ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  return (
    <Modal
      open
      onClose={onClose}
      title="Vista previa del portal"
      description={
        borrador
          ? borrador.hayCambiosSinPublicar
            ? "Así se vería con los cambios del borrador. Todavía no está publicado."
            : "El borrador es igual a lo publicado."
          : undefined
      }
      icon={Eye}
      size="xl"
    >
      {status === "loading" ? (
        <CarLoaderModal label="Cargando vista previa…" />
      ) : (
        <UiStateBoundary status={status} errorMessage="No se pudo cargar la vista previa." onRetry={() => void load()}>
          {borrador && (
            <div className="flex flex-col gap-3">
              <p className="text-xs text-[#59677D] dark:text-white/70">
                {borrador.versionPublicada !== null ? `Versión publicada: ${borrador.versionPublicada}.` : "Aún no se ha publicado ninguna versión."}
                {" "}Consultado el {formatFechaHora(new Date())}.
              </p>
              <PortalPreview contenido={borrador.contenido} />
            </div>
          )}
        </UiStateBoundary>
      )}
    </Modal>
  );
}
