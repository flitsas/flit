import { Info } from 'lucide-react';
import { ETIQUETA_CARGADO_POR_FLITO } from '@/lib/tramites/flito';

/**
 * HU #13266 (AC1/AC4) — etiqueta de un adjunto que cargó FLITO (`provider = flito`).
 *
 * Mismo patrón de nota con icono que la instrucción de cargue del checklist (HU #12067): icono `Info`
 * en el azul plano de la app y texto en el color del cuerpo. El significado lo lleva el TEXTO, no el
 * color. Lleva `id` para que las acciones que quedan sobre el adjunto (previsualizar) la referencien
 * con `aria-describedby` y el lector de pantalla anuncie por qué no hay «Reemplazar» ni «Borrar».
 */
export function EtiquetaCargadoPorFlito({ id, className = '' }: { id?: string; className?: string }) {
  return (
    <p
      id={id}
      className={`flex items-start gap-1 text-xs leading-snug ${className}`}
      data-testid="etiqueta-cargado-por-flito"
    >
      <Info className="mt-px h-3 w-3 shrink-0" style={{ color: '#557EFF' }} aria-hidden="true" />
      <span className="opacity-80">{ETIQUETA_CARGADO_POR_FLITO}</span>
    </p>
  );
}
