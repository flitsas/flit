import { useId, type ReactNode } from "react";

// Copia local de la forma de frontend-diagnostico/components/estadisticas/graficas/Figura.tsx (D-UI-GRAFICAS: se copia,
// no se importa ni se mueve a packages/ui; hueco H-1 de spec-visual §5).

/** Título de sección del dashboard: `text-base font-semibold text-flit-primary`. */
export const TITULO_SECCION_CLS = "text-base font-semibold text-flit-primary";
/** Tarjeta de sección del kit, compartida por el dashboard y los reportes. */
export const SECCION_CLS = "flex flex-col gap-4 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-4";
export const SUBTITULO_CLS = "text-xs text-[color:var(--di-text-secondary)]";

/**
 * Contenedor accesible de una gráfica: `<figure aria-labelledby>` cuyo nombre es el título visible del `<figcaption>`. El
 * nivel del encabezado lo elige la sección que la contiene.
 */
export function Figura({
  titulo,
  subtitulo,
  nivel = 3,
  children,
  className = "",
}: {
  titulo: string;
  subtitulo?: string;
  nivel?: 2 | 3;
  children: ReactNode;
  className?: string;
}) {
  const id = useId();
  const Encabezado = nivel === 2 ? "h2" : "h3";
  return (
    <figure aria-labelledby={`${id}-titulo`} className={`m-0 flex flex-col gap-3 ${className}`}>
      <figcaption className="flex flex-col gap-0.5">
        <Encabezado id={`${id}-titulo`} className={TITULO_SECCION_CLS}>
          {titulo}
        </Encabezado>
        {subtitulo ? <span className={SUBTITULO_CLS}>{subtitulo}</span> : null}
      </figcaption>
      {children}
    </figure>
  );
}

/** Leyenda en texto con muestra de color: nunca solo color (WCAG 1.4.1). */
export function Leyenda({ items }: { items: Array<{ nombre: string; color: string; texto?: string }> }) {
  return (
    <ul aria-label="Leyenda" className="m-0 flex list-none flex-wrap gap-x-4 gap-y-1 p-0 text-xs text-[color:var(--di-text-secondary)]">
      {items.map((i) => (
        <li key={i.nombre} className="inline-flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-2.5 w-2.5 shrink-0 rounded-sm" style={{ background: i.color }} />
          {i.texto ?? i.nombre}
        </li>
      ))}
    </ul>
  );
}
