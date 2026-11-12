/*
 * `iconoComo` vive aparte de Icono.tsx (que es "use client") para que los catálogos de datos que también se evalúan en
 * el servidor (navegación del dock, menú de productos, cuenta) puedan crear sus componentes de ícono al cargar el módulo.
 * El componente que devuelve pinta <Icono>, que sí es de cliente: eso es válido desde el servidor.
 */
import type { CSSProperties } from "react";
import { Icono } from "./Icono";
import type { NombreIcono } from "./iconos-kit";

/** Props que reciben los componentes de ícono que se pasan como valor (`icon={…}`, arrays de acciones, `LucideIcon`). */
export interface IconoComoProps {
  className?: string;
  style?: CSSProperties;
  size?: number | string;
  strokeWidth?: number | string;
  "aria-hidden"?: boolean | "true" | "false";
  "aria-label"?: string;
}

const COMO = new Map<NombreIcono, (p: IconoComoProps) => ReturnType<typeof Icono>>();

/**
 * Componente de ícono del kit para los sitios que esperan un COMPONENTE y no un elemento: `icon={iconoComo("lapiz")}`
 * en RowActions/ActionsMenu/Modal, arrays de acciones o tipos `LucideIcon`/`ComponentType<{ className }>`. Acepta y
 * descarta lo propio de lucide (`strokeWidth`); `aria-label` pasa a nombre accesible. Mismo componente por nombre
 * (estable entre renders).
 */
export function iconoComo(nombre: NombreIcono) {
  let c = COMO.get(nombre);
  if (!c) {
    const Comp = ({ className, style, size, "aria-label": etiqueta }: IconoComoProps) => (
      <Icono nombre={nombre} className={className} style={style} size={size} etiqueta={etiqueta} />
    );
    Comp.displayName = `Icono(${nombre})`;
    c = Comp;
    COMO.set(nombre, c);
  }
  return c;
}
