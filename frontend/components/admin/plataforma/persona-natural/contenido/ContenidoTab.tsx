"use client";

// HU #14076 (Feature #14070, Épica #14021) — Pestaña «Contenido» de Plataforma > Persona Natural: preguntas frecuentes,
// banners y enlaces del portal con el flujo editar → ver cómo queda → publicar. Todo lo que se edita es un borrador: el
// ciudadano solo ve lo publicado. El panel de arriba dice si hay cambios sin publicar (`hayCambiosSinPublicar`), abre la
// vista previa y las publicaciones anteriores, y «Publicar cambios» pide confirmación explicando qué cambia para el
// ciudadano. Una publicación reemplaza las preguntas y los banners que el portal muestra por defecto y no se edita.
import { useCallback, useEffect, useState } from "react";
import { Eye, History, Send } from "lucide-react";
import { useToast } from "@/components/admin/Toast";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { formatFechaHora } from "@/lib/format/date";
import {
  fetchBorradorPn,
  fetchVersionesPn,
  publicarContenidoPn,
  type BorradorContenidoPn,
} from "@/lib/api/admin-pn-contenido";
import { PnButton, PnConfirmDialog } from "../pn-componentes";
import { BannersSection } from "./BannersSection";
import { EnlacesSection } from "./EnlacesSection";
import { PreguntasSection } from "./PreguntasSection";
import { VersionesModal } from "./VersionesModal";
import { VistaPreviaModal } from "./VistaPreviaModal";

function plural(n: number, uno: string, varios: string): string {
  return `${n} ${n === 1 ? uno : varios}`;
}

function PublicarDialog({
  borrador,
  onClose,
  onPublicar,
}: {
  borrador: BorradorContenidoPn;
  onClose: () => void;
  onPublicar: () => Promise<void>;
}) {
  const preguntas = borrador.contenido.preguntas.length;
  const banners = borrador.contenido.banners.length;
  return (
    <PnConfirmDialog
      title="Publicar los cambios"
      icon={Send}
      confirmLabel="Sí, publicar"
      confirmIcon={Send}
      onClose={onClose}
      onConfirm={onPublicar}
    >
      <p>
        Desde ahora el ciudadano verá en el portal {plural(preguntas, "pregunta visible", "preguntas visibles")} y{" "}
        {plural(banners, "banner visible", "banners visibles")}.
      </p>
      <InlineAlert tone="warning" title="Qué cambia para el ciudadano">
        Lo que publicas reemplaza las preguntas y los banners que el portal muestra por defecto. Una publicación no se
        puede editar: si después quieres cambiar algo, haz el cambio y publica otra vez. La anterior queda en las
        publicaciones anteriores.
      </InlineAlert>
      {preguntas === 0 && (
        <InlineAlert tone="warning" compact>
          No hay preguntas visibles: el ciudadano no verá preguntas frecuentes.
        </InlineAlert>
      )}
    </PnConfirmDialog>
  );
}

export function ContenidoTab() {
  const { show } = useToast();
  const [borrador, setBorrador] = useState<BorradorContenidoPn | null>(null);
  const [errorEstado, setErrorEstado] = useState(false);
  const [ultimaPublicacion, setUltimaPublicacion] = useState<string | null>(null);
  const [vista, setVista] = useState<"previa" | "publicar" | "versiones" | null>(null);

  const refrescar = useCallback(async (signal?: AbortSignal) => {
    try {
      const b = await fetchBorradorPn(signal);
      if (signal?.aborted) return;
      setBorrador(b);
      setErrorEstado(false);
      // La fecha de la última publicación sale del historial; si no se puede leer, el panel sigue sin la fecha.
      if (b.versionPublicada !== null) {
        const versiones = await fetchVersionesPn(signal).catch(() => []);
        if (!signal?.aborted) setUltimaPublicacion(versiones[0]?.publicadoEn ?? null);
      } else {
        setUltimaPublicacion(null);
      }
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
      <section
        aria-labelledby="pn-publicacion-titulo"
        className="flex flex-col gap-3 rounded-[18px] border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#162744]"
      >
        <div>
          <h2 id="pn-publicacion-titulo" className="text-base font-bold text-flit-primary dark:text-white">
            Publicación del contenido
          </h2>
          <p className="text-sm text-[#59677D] dark:text-white/78">
            Lo que cambies aquí no lo ve el ciudadano hasta que lo publiques. Antes de publicar puedes ver cómo quedaría.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-col gap-1 text-sm">
            {errorEstado ? (
              <div className="flex flex-wrap items-center gap-2">
                <StatusBadge label="No se pudo consultar el estado" tone="danger" />
                <PnButton variant="secondary" onClick={() => void refrescar()}>
                  Reintentar
                </PnButton>
              </div>
            ) : borrador ? (
              <>
                <div>
                  <StatusBadge
                    label={sinPublicar ? "Hay cambios sin publicar" : "Todo está publicado"}
                    tone={sinPublicar ? "warning" : "success"}
                  />
                </div>
                <p className="text-sm font-semibold text-flit-primary dark:text-white">Lo que ve hoy el ciudadano</p>
                <span className="text-xs text-[#59677D] dark:text-white/70">
                  {borrador.versionPublicada === null
                    ? "Todavía no has publicado nada: el portal muestra sus preguntas y banners de siempre (contenido por defecto)."
                    : `Contenido publicado por ti${ultimaPublicacion ? ` el ${formatFechaHora(ultimaPublicacion)}` : ""}, que reemplaza el contenido por defecto del portal.`}
                </span>
                {sinPublicar && borrador.versionPublicada !== null && (
                  <span className="text-xs text-[#59677D] dark:text-white/70">
                    Tus cambios nuevos todavía no se ven: el portal sigue con esa publicación.
                  </span>
                )}
              </>
            ) : (
              <span className="text-xs text-[#59677D] dark:text-white/70">Consultando el estado de publicación…</span>
            )}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <PnButton variant="secondary" icon={Eye} onClick={() => setVista("previa")}>
              Ver cómo quedaría
            </PnButton>
            <PnButton variant="secondary" icon={History} onClick={() => setVista("versiones")}>
              Publicaciones anteriores
            </PnButton>
            <PnButton
              variant="primary"
              icon={Send}
              onClick={() => setVista("publicar")}
              disabled={!sinPublicar}
              title={sinPublicar ? undefined : "No hay cambios por publicar"}
            >
              Publicar cambios
            </PnButton>
          </div>
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
            await publicarContenidoPn();
            setVista(null);
            show("Contenido publicado. El ciudadano ya lo ve en el portal.", "success");
            await refrescar();
          }}
        />
      )}
    </div>
  );
}
