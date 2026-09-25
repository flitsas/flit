'use client';

import { RefreshCw } from 'lucide-react';

/**
 * Epic #12686 (HU #12808) — el sondeo trajo un conteo distinto al de la tabla para la tarjeta
 * elegida. La tabla no se recarga sola; este aviso la recarga a pedido.
 */
export function AvisoHayCambios({ onActualizar }: { onActualizar: () => void }) {
  return (
    <div role="status" className="flex justify-end">
      <button
        type="button"
        onClick={onActualizar}
        className="inline-flex items-center gap-1.5 rounded-lg border border-[#557EFF]/40 bg-[#557EFF]/10 px-3 py-1.5 text-xs font-semibold text-[#3A5FD9] transition hover:bg-[#557EFF]/15 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:text-[#8FA9FF]"
      >
        <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
        Hay cambios — Actualizar
      </button>
    </div>
  );
}
