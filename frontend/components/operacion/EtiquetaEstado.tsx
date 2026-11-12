import type { CSSProperties, ReactNode } from 'react';
import { ESTADO_ICONO, type EstadoTramite, estadoChipClass, estadoChipStyle } from '@/lib/tramites/estados';

/**
 * UI Kit 2026 — chip de estado escrito a mano (un `<span>` sin rol) con la forma de la etiqueta del kit
 * (`.kit-etiqueta`: contorno con punta y relleno al 10 %) y el disco del estado (`ESTADO_ICONO`) delante del texto.
 *
 * Es solo presentación: a diferencia de `StatusBadge` NO agrega `role="status"` ni `aria-label`, para que los chips
 * que ya eran un `<span>` mudo (cabeceras, líneas de tiempo) sigan leyéndose igual. El disco es decorativo.
 */
export function EtiquetaEstado({ estado, children, className = '' }: { estado: string; children: ReactNode; className?: string }) {
  const chip = estadoChipStyle(estado);
  const icono = ESTADO_ICONO[estado as EstadoTramite];
  const style = { '--c': chip.border, '--ct': chip.color } as CSSProperties;
  return (
    <span
      className={`${estadoChipClass(estado)} kit-etiqueta${icono ? ' kit-etiqueta--icono' : ''} whitespace-nowrap ${className}`}
      style={style}
    >
      {icono ? (
        // eslint-disable-next-line @next/next/no-img-element -- disco estático del kit
        <img src={icono} alt="" aria-hidden="true" width={20} height={20} className="kit-etiqueta__icono" />
      ) : (
        <span aria-hidden="true" className="kit-etiqueta__punto" />
      )}
      {children}
    </span>
  );
}
