'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Plus } from 'lucide-react';
import { ModuleTitle } from '@/components/atom/modules/ModuleTitle';
import { ProcedureTypeList } from '@/components/superadmin/ProcedureTypeList';
import { ParametrizationWizard } from '@/components/superadmin/ParametrizationWizard';
import { useProcedureTypes } from '@/hooks/useProcedureTypes';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

/**
 * Configurador de tipos de trámite (FE-07, HU #10842). Punto de entrada real del
 * Feature 08 en el front: monta el listado (ProcedureTypeList) y el asistente
 * (ParametrizationWizard, con los pasos FE-01…FE-06) que antes existían pero no
 * estaban cableados a ninguna ruta. Usa el mismo chrome que los demás módulos admin
 * (Gestión documental): back "Volver al inicio", ModuleTitle con la acción primaria
 * FUERA de la caja del título, y la tarjeta contenedora rounded-2xl border bg-white/60.
 */
type View = { mode: 'list' } | { mode: 'new' } | { mode: 'edit'; id: string };

export default function AdminProcedureTypesPage() {
  const router = useRouter();
  const { items, status, error, reload, publish } = useProcedureTypes();
  const [view, setView] = useState<View>({ mode: 'list' });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [publishSuccess, setPublishSuccess] = useState<string | null>(null);

  const openNew = () => {
    setPublishSuccess(null);
    setView({ mode: 'new' });
  };

  const handleExit = (saved?: boolean) => {
    setView({ mode: 'list' });
    setSelectedId(null);
    if (saved) {
      setPublishSuccess('Parametrización guardada.');
      void reload();
    }
  };

  const handlePublish = async (id: string): Promise<ProcedureTypeSummary> => {
    const updated = await publish(id);
    setPublishSuccess(`Tipo ${updated.code} publicado.`);
    return updated;
  };

  // Vista asistente: el ParametrizationWizard ocupa todo el alto (gestiona su propio scroll).
  if (view.mode !== 'list') {
    return (
      <div className="h-full w-full">
        <ParametrizationWizard
          editingId={view.mode === 'edit' ? view.id : null}
          onExit={handleExit}
        />
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col gap-4 px-6 pt-6 pb-10">
      <button
        type="button"
        onClick={() => router.push('/')}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: '#557EFF' }}
      >
        <ArrowLeft className="h-3.5 w-3.5" /> Volver al inicio
      </button>

      <div className="flex flex-wrap items-start justify-between gap-3">
        <ModuleTitle
          title="Parametrización de trámites"
          subtitle="Configura los tipos de trámite: identidad, aristas, fuentes, documentos, pasos y campos."
        />
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={openNew}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white shadow-sm"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
            aria-label="Crear un nuevo tipo de trámite"
          >
            <Plus className="h-4 w-4" /> Nuevo tipo
          </button>
        </div>
      </div>

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <ProcedureTypeList
          items={items}
          status={status}
          error={error}
          onNew={openNew}
          onEdit={(id) => {
            setPublishSuccess(null);
            setView({ mode: 'edit', id });
          }}
          onReload={reload}
          onPublish={handlePublish}
          selectedId={selectedId}
          onSelect={setSelectedId}
          publishSuccessMessage={publishSuccess}
        />
      </div>
    </div>
  );
}
