/*
 * Discos de aviso del Juego de íconos FLIT (PDF definitivo, «Estados y trámites»): el mismo SVG de color que pinta
 * `InlineAlert`, para usarlo suelto junto a un mensaje (error bajo un campo, página que no cargó, fila fallida).
 *  - error     → disco rojo con X (todo lo negativo: rechazado, error, fallo, inválido).
 *  - alerta    → triángulo naranja (precaución; NO es un error).
 *  - info      → disco azul con «i».
 *  - ok        → disco verde con check (todo lo positivo).
 *  - pendiente → disco naranja con «!» (en espera de una acción).
 * Sin "use client": es una imagen estática, sirve igual desde catálogos que se evalúan en el servidor.
 */
import type { CSSProperties } from "react";

export type TipoAviso = "error" | "alerta" | "info" | "ok" | "pendiente";

const ARCHIVO: Record<TipoAviso, string> = {
  error: "aviso-error",
  alerta: "aviso-alerta",
  info: "aviso-info",
  ok: "aviso-ok",
  pendiente: "estado-pendiente",
};

export interface IconoAvisoProps {
  tipo: TipoAviso;
  className?: string;
  style?: CSSProperties;
  /** Nombre accesible; sin él es decorativo (el texto de al lado ya dice el mensaje). */
  etiqueta?: string;
}

export function IconoAviso({ tipo, className, style, etiqueta }: IconoAvisoProps) {
  return (
    // eslint-disable-next-line @next/next/no-img-element -- disco estático del kit
    <img
      src={`/assets/kit/${ARCHIVO[tipo]}.svg`}
      alt={etiqueta ?? ""}
      aria-hidden={etiqueta ? undefined : true}
      className={`object-contain ${className ?? ""}`.trim()}
      style={style}
    />
  );
}

/** Mismas props que los componentes de `iconoComo` (se intercambian en los mapas de íconos); `size`/`strokeWidth` se ignoran. */
type PropsComo = {
  className?: string;
  style?: CSSProperties;
  size?: number | string;
  strokeWidth?: number | string;
  "aria-hidden"?: boolean | "true" | "false";
  "aria-label"?: string;
};

const COMO = new Map<TipoAviso, (p: PropsComo) => ReturnType<typeof IconoAviso>>();

/** El disco como COMPONENTE, para los sitios que esperan `Icon={…}` (mapas de estado, `icon` de acciones). */
export function avisoComo(tipo: TipoAviso) {
  let c = COMO.get(tipo);
  if (!c) {
    const Comp = ({ className, style, "aria-label": etiqueta }: PropsComo) => (
      <IconoAviso tipo={tipo} className={className} style={style} etiqueta={etiqueta} />
    );
    Comp.displayName = `Aviso(${tipo})`;
    c = Comp;
    COMO.set(tipo, c);
  }
  return c;
}
