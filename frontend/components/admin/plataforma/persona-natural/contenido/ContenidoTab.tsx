"use client";

// HU #14076 (Feature #14070, Épica #14021) — Pestaña «Contenido» de Plataforma > Persona Natural: preguntas frecuentes,
// banners y enlaces del portal con el flujo borrador → vista previa → publicar. Todo lo que se edita es el BORRADOR; el
// portal solo ve lo publicado. La barra de estado dice si hay cambios sin publicar (`hayCambiosSinPublicar`), abre la
// vista previa y el historial de versiones, y «Publicar» pide confirmación y muestra la fecha de publicación.
import { useCallback, useEffect, useState } from "react";
import { Eye, History, Loader2, Send } from "lucide-react";
import { useToast } from "@/components/admin/Toast";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { formatFechaHora } from "@/lib/format/date";
import { fetchBorradorPn, publicarContenidoPn, type BorradorContenidoPn } from "@/lib/api/admin-pn-contenido";
import { PN_CANCEL_BTN_CLS, PN_PRIMARY_BTN_CLS, PN_PRIMARY_BTN_STYLE } from "../pn-ui";
import { pnError } from "../pnErrors";
import { BannersSection } from "./BannersSection";
import { EnlacesSection } from "./EnlacesSection";
import { PreguntasSection } from "./PreguntasSection";
import { VersionesModal } from "./VersionesModal";
import { VistaPreviaModal } from "./VistaPreviaModal";

function PublicarDialog({
  borrador,
  onClose,
  onPublicar,
}: {
  borrador: BorradorContenidoPn;
  onClose: () => void;
  onPublicar: () => Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function confirmar() {
    setBusy(true);
    setError(null);
    try {
      await onPublicar();
    } catch (err) {
      setError(pnError(err).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title="Publicar el contenido"
      icon={Send}
      size="sm"
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} disabled={busy} className={PN_CANCEL_BTN_CLS}>
            Cancelar
          </button>
          <button type="button" onClick={() => void confirmar()} disabled={busy} className={PN_PRIMARY_BTN_CLS} style={PN_PRIMARY_BTN_STYLE}>
            {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
            Publicar ahora
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm text-flit-primary dark:text-white">
        <p>
          El portal pasará a mostrar el borrador actual: {borrador.contenido.preguntas.length}{" "}
          {borrador.contenido.preguntas.length === 1 ? "pregunta activa" : "preguntas activas"} y{" "}
          {borrador.contenido.banners.length} {borrador.contenido.banners.length === 1 ? "banner activo" : "banners activos"}.
        </p>
        <p className="text-xs text-[#59677D] dark:text-white/70">
          Se crea una versión nueva ({(borrador.versionPublicada ?? 0) + 1}); la anterior queda en el historial.
        </p>
        {error && (
          <p role="alert" className="text-sm font-semibold text-[#C2410C]">
            {error}
          </p>
        )}
      </div>
    </Modal>
  );
}

export function ContenidoTab() {
  const { show } = useToast();
  const [borrador, setBorrador] = useState<BorradorContenidoPn | null>(null);
  const [errorEstado, setErrorEstado] = useState(false);
  const [vista, setVista] = useState<"previa" | "publicar" | "versiones" | null>(null);

  const refrescar = useCallback(async (signal?: AbortSignal) => {
    try {
      const b = await fetchBorradorPn(signal);
      if (signal?.aborted) return;
      setBorrador(b);
      setErrorEstado(false);
    } catch {
      if (!signal?.aborted) setErrorEstado(true);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial del estado borrador/publicado
    void refrescar(controller.signal);
    return () => controller.abort();
  }, [refrescar]);

  const sinPublicar = borrador?.hayCambiosSinPublicar ?? false;

  return (
    <div className="flex flex-col gap-8">
      <section aria-label="Estado de publicación" className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-[#DFE5ED] bg-white px-4 py-3 dark:border-white/10 dark:bg-[#0B0F14]">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          {errorEstado ? (
            <>
              <StatusBadge label="No se pudo consultar el estado" tone="danger" />
              <button
                type="button"
                onClick={() => void refrescar()}
                className="rounded-md text-xs font-semibold text-[color:var(--flit-brand-ink)] underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
              >
                Reintentar
              </button>
            </>
          ) : borrador ? (
            <>
              <StatusBadge
                label={sinPublicar ? "Hay cambios sin publicar" : "Sin cambios por publicar"}
                tone={sinPublicar ? "warning" : "success"}
              />
              <span className="text-xs text-[#59677D] dark:text-white/70">
                {borrador.versionPublicada !== null ? `Versión publicada: ${borrador.versionPublicada}` : "Aún no se ha publicado nada"}
              </span>
            </>
          ) : (
            <span className="text-xs text-[#59677D] dark:text-white/70">Consultando el estado de publicación…</span>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button type="button" onClick={() => setVista("previa")} className={PN_CANCEL_BTN_CLS}>
            <Eye className="mr-1.5 inline h-4 w-4" aria-hidden="true" />
            Vista previa
          </button>
          <button type="button" onClick={() => setVista("versiones")} className={PN_CANCEL_BTN_CLS}>
            <History className="mr-1.5 inline h-4 w-4" aria-hidden="true" />
            Historial de versiones
          </button>
          <button
            type="button"
            onClick={() => setVista("publicar")}
            disabled={!sinPublicar}
            title={sinPublicar ? undefined : "No hay cambios por publicar"}
            className={PN_PRIMARY_BTN_CLS}
            style={PN_PRIMARY_BTN_STYLE}
          >
            <Send className="h-4 w-4" aria-hidden="true" />
            Publicar
          </button>
        </div>
      </section>

      <PreguntasSection onCambio={() => void refrescar()} />
      <BannersSection onCambio={() => void refrescar()} />
      <EnlacesSection onCambio={() => void refrescar()} />

      {vista === "previa" && <VistaPreviaModal onClose={() => setVista(null)} />}
      {vista === "versiones" && <VersionesModal onClose={() => setVista(null)} />}
      {vista === "publicar" && borrador && (
        <PublicarDialog
          borrador={borrador}
          onClose={() => setVista(null)}
          onPublicar={async () => {
            const publicada = await publicarContenidoPn();
            setVista(null);
            show(`Contenido publicado el ${formatFechaHora(publicada.publicadoEn)} (versión ${publicada.version}).`, "success");
            await refrescar();
          }}
        />
      )}
    </div>
  );
}
