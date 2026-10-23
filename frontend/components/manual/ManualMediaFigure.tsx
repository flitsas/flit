import type { ManualMedia } from "@/lib/manual/types";

/** Ruta pública del SVG compilado de un diagrama (fuente .mmd, HU #13014). */
export function diagramSrc(id: string): string {
  return `/manual/diagrams/${id}.svg`;
}

/**
 * Captura de pantalla o diagrama dentro de un artículo del manual (HU #13012, Épica #12755).
 * El borde y el fondo salen de los tokens del manual, así que la figura respeta el tema
 * claro/oscuro sin estilos propios por tema.
 */
export function ManualMediaFigure({ media }: { media: ManualMedia }) {
  const src = media.kind === "image" ? media.src : diagramSrc(media.id);

  return (
    <figure
      className="mt-4 overflow-hidden rounded-xl border"
      // Backdrop blanco fijo: capturas y diagramas se generan sobre la UI clara, y así siguen
      // legibles cuando el manual está en tema oscuro (el borde y el caption sí siguen al tema).
      style={{ borderColor: "var(--manual-border)", background: "#ffffff" }}
    >
      {/* eslint-disable-next-line @next/next/no-img-element -- assets estáticos locales de tamaño variable; next/image no aporta aquí */}
      <img
        src={src}
        alt={media.alt}
        loading="lazy"
        className={media.kind === "diagram" ? "mx-auto block max-w-full p-4" : "block w-full"}
      />
      {media.caption && (
        <figcaption
          className="border-t px-4 py-2 text-[13px] leading-6"
          style={{ borderColor: "var(--manual-border)", color: "var(--manual-text-soft)" }}
        >
          {media.caption}
        </figcaption>
      )}
    </figure>
  );
}
