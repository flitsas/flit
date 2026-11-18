"use client";

/*
 * Íconos del Manual UI Kit 2026 (propuesta-2). Reemplazan a lucide-react en toda la suite.
 *  - <Icono nombre="campana" />: glifo en currentColor (hereda el color del texto), 1em por defecto.
 *  - <Icono nombre="reportes" disco />: el `.kico` de la propuesta, disco --kit-hover con el glifo en azul al 56 %.
 *  - <IconoModulo clave="reportes" />: resuelve la clave de módulo/cifra con MAPA_ICONO (un "@x" pinta el SVG suelto).
 * Decorativo por defecto (aria-hidden). Con `etiqueta` pasa a role="img" con nombre accesible.
 */
import { useId, type CSSProperties } from "react";
import { cn } from "./cn";
import { GLIFOS, type NombreIcono } from "./iconos-kit";
import { MAPA_ICONO, rutaKit } from "./iconos-mapas";

export type { NombreIcono } from "./iconos-kit";

export interface IconoProps {
  nombre: NombreIcono;
  /** Lado en px o cualquier unidad CSS. Por defecto 1em (el tamaño del texto). */
  size?: number | string;
  className?: string;
  style?: CSSProperties;
  /** Disco del kit (.kico): fondo --kit-hover y glifo en azul. El `size` es el del disco. */
  disco?: boolean;
  /** Nombre accesible; sin él el ícono es decorativo. */
  etiqueta?: string;
}

const lado = (s: number | string | undefined, def: string) => (s === undefined ? def : typeof s === "number" ? `${s}px` : s);

export function Icono({ nombre, size, className, style, disco, etiqueta }: IconoProps) {
  const id = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  const g = GLIFOS[nombre] ?? GLIFOS.documento;
  const inner = g.inner.includes("__K__") ? g.inner.replace(/__K__/g, `k${id}`) : g.inner;
  const a11y = etiqueta ? { role: "img" as const, "aria-label": etiqueta } : { "aria-hidden": true as const };
  const svg = (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox={g.vb}
      width={disco ? "56%" : lado(size, "1em")}
      height={disco ? "56%" : lado(size, "1em")}
      focusable="false"
      className={disco ? undefined : cn("kit-icono", className)}
      style={disco ? undefined : style}
      {...(disco ? { "aria-hidden": true as const } : a11y)}
      dangerouslySetInnerHTML={{ __html: inner }}
    />
  );
  if (!disco) return svg;
  const d = lado(size, "40px");
  return (
    <span className={cn("kit-disco", className)} style={{ width: d, height: d, ...style }} {...a11y}>
      {svg}
    </span>
  );
}

export interface IconoModuloProps extends Omit<IconoProps, "nombre"> {
  /** Clave de módulo o cifra (MAPA_ICONO). Sin mapa cae en «documento», como la propuesta. */
  clave: string;
}

export function IconoModulo({ clave, size, className, style, disco, etiqueta }: IconoModuloProps) {
  const m = MAPA_ICONO[clave] ?? "documento";
  if (typeof m === "string" && m.startsWith("@")) {
    const d = lado(size, "40px");
    return (
      // eslint-disable-next-line @next/next/no-img-element -- SVG estático del kit, sin optimización de imagen
      <img src={rutaKit(m)} alt={etiqueta ?? ""} aria-hidden={etiqueta ? undefined : true} width={typeof size === "number" ? size : 40} height={typeof size === "number" ? size : 40} className={cn("kit-imagen", className)} style={{ width: d, height: d, ...style }} />
    );
  }
  return <Icono nombre={m as NombreIcono} size={size} className={className} style={style} disco={disco} etiqueta={etiqueta} />;
}

// `iconoComo` NO se reexporta aquí: este módulo es "use client" y desde el servidor sería una referencia de cliente.
// Se importa siempre de "@flit/ui/iconoComo".
