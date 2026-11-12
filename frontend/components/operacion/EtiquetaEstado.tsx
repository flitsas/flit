import type { ReactNode } from 'react';
import { ETIQUETA_ICONO_CLS, ETIQUETA_PUNTO_CLS, etiquetaKit } from '@flit/ui/StatusBadge';
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
  const kit = etiquetaKit({ c: chip.border, ct: chip.color }, { icono: Boolean(icono), className: `${estadoChipClass(estado)} ${className}` });
  return (
    <span {...kit}>
      {icono ? (
        // eslint-disable-next-line @next/next/no-img-element -- disco estático del kit
        <img src={icono} alt="" aria-hidden="true" width={20} height={20} className={ETIQUETA_ICONO_CLS} />
      ) : (
        <span aria-hidden="true" className={ETIQUETA_PUNTO_CLS} />
      )}
      {children}
    </span>
  );
}
