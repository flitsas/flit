'use client';

import { useState } from 'react';
import { Plus } from 'lucide-react';
import { ModuleTitle } from '@/components/atom/modules/ModuleTitle';
import { ProcedureTypeList } from '@/components/superadmin/ProcedureTypeList';
import { ParametrizationWizard } from '@/components/superadmin/ParametrizationWizard';
import { useProcedureTypes } from '@/hooks/useProcedureTypes';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

/**
 * Configurador de tipos de trámite (FE-07, HU #10842). Punto de entrada real del
 * Feature 08 en el front: monta el listado (ProcedureTypeList) y el asistente
 * (ParametrizationWizard, con los pasos FE-01…FE-06) que antes existían pero no
 * estaban cableados a ninguna ruta. Alterna entre listado y asistente con estado local;
 * el listado usa el hook useProcedureTypes (carga + publicar) ya existente.
 */
type View = { mode: 'list' } | { mode: 'new' } | { mode: 'edit'; id: string };

export default function AdminProcedureTypesPage() {
  const { items, status, error, reload, publish } = useProcedureTypes();
  const [view, setView] = useState<View>({ mode: 'list' });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [publishSuccess, setPublishSuccess] = useState<string | null>(null);

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
    <div className="mx-auto flex h-full w-full max-w-5xl flex-col gap-4 px-6 pt-5 pb-6">
      <ModuleTitle
        title="Parametrización de trámites"
        subtitle="Configura los tipos de trámite: identidad, aristas, fuentes, documentos, pasos y campos."
        action={
          <button
            type="button"
            onClick={() => {
              setPublishSuccess(null);
              setView({ mode: 'new' });
            }}
            className="flex items-center gap-1.5 rounded-xl px-4 py-2.5 text-xs font-semibold text-white"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
            aria-label="Crear un nuevo tipo de trámite"
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            Nuevo tipo
          </button>
        }
      />

      <ProcedureTypeList
        items={items}
        status={status}
        error={error}
        onNew={() => {
          setPublishSuccess(null);
          setView({ mode: 'new' });
        }}
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
  );
}
