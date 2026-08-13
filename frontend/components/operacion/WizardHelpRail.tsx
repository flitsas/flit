'use client';

import { useState, type ReactNode } from 'react';
import { FileText, ListChecks } from 'lucide-react';
import { OtSidePanel } from '@/components/admin/transit-offices/OtSidePanel';
import { ActiveDeedsList, useActiveDeeds } from './ActiveDeedsCollapse';
import { ProcedureDocsPreviewInformativo } from './ProcedureDocsPreviewInformativo';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * Carril de consulta del primer paso, en la disposición de la propuesta: dos iconos anclados al
 * borde derecho que abren, cada uno en su panel, las escrituras vigentes de la compañía y la guía
 * de documentos a tener listos.
 *
 * Los dos contenidos son material de CONSULTA, no del trámite: no se rellenan, no se validan y no
 * condicionan el avance. Ocupando sitio en la columna empujaban hacia abajo lo único que el paso
 * pide de verdad —elegir el tipo y consultar el vehículo—, y el gestor tenía que plegarlos cada vez
 * para llegar a lo suyo. En el carril siguen a un clic sin costar altura.
 */
export function WizardHelpRail({
  modalidad,
  transitOfficeId,
  tenantId,
}: {
  modalidad: WizardModalidad;
  transitOfficeId?: string;
  tenantId?: string;
}) {
  const [escrituras, setEscrituras] = useState(false);
  // El panel desmonta su contenido al cerrarse: el estado de carga vive aquí para no repetir la
  // consulta cada vez que el gestor lo abre.
  const deedsState = useActiveDeeds(tenantId, escrituras);

  return (
    <div
      // `fixed` y no `sticky`: el carril acompaña al gestor durante todo el paso, que puede ser
      // largo cuando la consulta trae el pre-vuelo entero. El contenedor del paso reserva el
      // espacio con `pr-16` para que nunca tape contenido.
      className="fixed right-4 top-1/3 z-30 flex flex-col gap-2 rounded-2xl border bg-white/90 p-1.5 shadow-[0_8px_24px_rgba(15,23,20,0.12)] backdrop-blur-md dark:bg-[#0B0F14]/90"
      role="group"
      aria-label="Consultas del trámite"
    >
      <RailButton
        label="Escrituras vigentes"
        icon={<FileText className="h-4 w-4" aria-hidden="true" />}
        onClick={() => setEscrituras(true)}
      />
      <ProcedureDocsPreviewInformativo
        modalidad={modalidad}
        transitOfficeId={transitOfficeId}
        renderTrigger={(open) => (
          <RailButton
            label="Documentos a tener listos"
            icon={<ListChecks className="h-4 w-4" aria-hidden="true" />}
            onClick={open}
          />
        )}
      />

      <OtSidePanel
        open={escrituras}
        title="Escrituras vigentes de la compañía"
        ariaLabel="Escrituras vigentes de la compañía"
        onClose={() => setEscrituras(false)}
        width="xl"
      >
        <ActiveDeedsList {...deedsState} />
      </OtSidePanel>
    </div>
  );
}

/**
 * Botón del carril: icono con el rótulo en un tooltip que aparece también al enfocar con teclado.
 * El nombre accesible va en `aria-label`, no en el tooltip, para que no dependa de que el tooltip
 * llegue a pintarse.
 */
function RailButton({
  label,
  icon,
  onClick,
}: {
  label: string;
  icon: ReactNode;
  onClick: () => void;
}) {
  return (
    <div className="group relative">
      <button
        type="button"
        onClick={onClick}
        aria-label={label}
        className="grid h-9 w-9 place-items-center rounded-xl transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        style={{ color: '#557EFF' }}
      >
        {icon}
      </button>
      <span
        aria-hidden="true"
        className="pointer-events-none absolute right-full top-1/2 mr-2 -translate-y-1/2 whitespace-nowrap rounded-lg px-2 py-1 text-xs font-semibold text-white opacity-0 transition group-hover:opacity-100 group-focus-within:opacity-100"
        style={{ background: '#162744' }}
      >
        {label}
      </span>
    </div>
  );
}
