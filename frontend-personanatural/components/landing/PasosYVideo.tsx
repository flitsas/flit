import { PlayCircle } from "lucide-react";
import { ASSETS } from "@/lib/assets";
import { PASOS_TRAMITE } from "@/lib/landing/contenido";
import { boton } from "./estilos";

export function PasosTramite() {
  return (
    <section className="dashboard-card pasos-tramite" aria-label="Cuatro simples pasos para tu trámite">
      <h2 className="dashboard-title flit-gradient-title flit-animated-title text-center">4 simples pasos para tu trámite</h2>
      <ol className="pasos-grid mt-4 grid grid-cols-2 gap-4 lg:grid-cols-4">
        {PASOS_TRAMITE.map(({ clave, titulo }, i) => (
          <li key={clave} className="paso-item paso-card group flex h-full flex-col items-center gap-2 text-center">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={ASSETS.pasos[clave]} alt="" aria-hidden className="paso-img h-14 w-auto transition-transform duration-300 group-hover:-translate-y-1 group-hover:scale-110" />
            <span className="paso-num" aria-hidden>{i + 1}</span>
            <span className="text-sm font-medium">{titulo}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}

// El video «Cómo radicar mi trámite» aún no se entrega: queda el marco del diseño con su aviso (es el mismo marcador del prototipo).
// Si la configuración publica la URL del video (VIDEO), se ofrece el enlace en pestaña nueva dentro del mismo marco (la CSP no admite incrustarlo).
export function VideoExplicativo({ url = null }: { url?: string | null }) {
  return (
    <section className="dashboard-card video-section" aria-label="Video: cómo radicar mi trámite">
      <h2 className="dashboard-title flit-gradient-title flit-animated-title text-center">¿Cómo radicar mi trámite?</h2>
      <div className="video-frame mt-4 flex flex-col items-center justify-center gap-3 rounded-[9px] border border-dashed border-border bg-muted" {...(url ? {} : { role: "img", "aria-label": "Video explicativo disponible próximamente" })}>
        <PlayCircle aria-hidden className="h-14 w-14 text-primary" />
        {url ? (
          <a href={url} target="_blank" rel="noopener noreferrer" className={boton("flit", "h-11 px-10", "none")}>Ver video (se abre en una pestaña nueva)</a>
        ) : (
          <p className="text-sm text-foreground/70">Video explicativo disponible próximamente</p>
        )}
      </div>
    </section>
  );
}
