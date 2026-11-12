"use client";

// HU #14076 AC3/AC4 — Vista previa del borrador: cómo se vería el contenido en el portal si se publicara hoy, sin
// publicarlo. Es un componente simple sobre el contenido del borrador (solo lo ACTIVO, en su orden): reutiliza los
// textos y el orden, no el código del portal. Un enlace legal sin documento oficial se indica como tal y el portal
// conserva su aviso actual.
import { useCallback, useEffect, useState } from "react";
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
      className="flex flex-col gap-6 rounded-2xl border border-border bg-secondary p-4"
    >
      <section aria-label="Banners" className="flex flex-col gap-3">
        {banners.length === 0 ? (
          <p className="text-xs text-muted-foreground">No hay banners activos.</p>
        ) : (
          banners.map((b) => (
            <figure key={b.id} className="flex flex-col gap-1">
              <BannerImagen id={b.id} alt={b.textoAlternativo} className="h-32 w-full max-w-xl rounded-xl object-cover" />
              <figcaption className="text-sm font-semibold text-flit-primary">{b.titulo}</figcaption>
            </figure>
          ))
        )}
      </section>

      <section aria-label="Preguntas frecuentes" className="flex flex-col gap-2">
        <h3 className="text-sm font-bold text-flit-primary">Preguntas frecuentes</h3>
        {preguntas.length === 0 ? (
          <p className="text-xs text-muted-foreground">No hay preguntas activas.</p>
        ) : (
          preguntas.map((p) => (
            <details key={p.id} className="rounded-xl border border-border bg-card px-3 py-2">
              <summary className="cursor-pointer text-sm font-semibold text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand">
                {p.pregunta}
              </summary>
              {/* Texto plano: se muestra como texto, nunca como HTML. */}
              <p className="mt-2 whitespace-pre-line text-sm text-muted-foreground">{p.respuesta}</p>
            </details>
          ))
        )}
      </section>

      <section aria-label="Enlaces" className="flex flex-col gap-1">
        <h3 className="text-sm font-bold text-flit-primary">Enlaces</h3>
        <ul className="flex flex-col gap-1 text-sm">
          {contenido.enlaces.map((e) => (
            <li key={e.codigo}>
              <span className="font-semibold">{ENLACE_LABEL[e.codigo]}:</span>{" "}
              {e.url ? (
                <span className="break-all text-[color:var(--flit-brand-ink)]">{e.url}</span>
              ) : (
                <span className="text-muted-foreground">
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
      title="Así lo vería el ciudadano"
      description={
        borrador
          ? borrador.hayCambiosSinPublicar
            ? "Así quedaría con tus cambios. Todavía no está publicado."
            : "Es igual a lo que ya está publicado."
          : undefined
      }
      icono="ojo"
      size="xl"
    >
      {status === "loading" ? (
        <CarLoaderModal label="Cargando cómo quedaría…" />
      ) : (
        <UiStateBoundary status={status} errorMessage="No se pudo cargar la vista previa." onRetry={() => void load()}>
          {borrador && (
            <div className="flex flex-col gap-3">
              <p className="text-xs text-muted-foreground">
                {borrador.versionPublicada !== null ? "Ya hay una publicación en el portal." : "Todavía no has publicado nada."} Consultado el{" "}
                {formatFechaHora(new Date())}.
              </p>
              <PortalPreview contenido={borrador.contenido} />
            </div>
          )}
        </UiStateBoundary>
      )}
    </Modal>
  );
}
