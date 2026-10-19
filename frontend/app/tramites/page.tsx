'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Modal } from '@/components/atom/Modal';
import { OperacionView } from '@/components/operacion/OperacionView';
import { NuevoTramiteSelector } from '@/components/operacion/NuevoTramiteSelector';
import { useFamilyAccess } from '@/hooks/useFamilyAccess';
import { accessDeniedPath } from '@/lib/auth/guard';
import { CargaMasivaModal } from '@/components/operacion/CargaMasivaModal';

/**
 * Track B — /tramites: listado de operación (KPIs + tabs + tabla).
 *
 * "Nuevo trámite" abre el selector del mockup EN UN MODAL sobre el listado (no navega a otra
 * pantalla). Cancelar conserva filtros, página y scroll. `/tramites/nuevo` monta el mismo
 * selector para enlaces directos.
 */
export default function TramitesPage() {
  const router = useRouter();
  // HU #13665 (AC2) — sin Ver en ninguna familia no hay nada que listar: al inicio con «No tienes acceso a este módulo.».
  const { readable } = useFamilyAccess();
  const sinAcceso = readable.length === 0;
  useEffect(() => {
    if (sinAcceso) router.replace(accessDeniedPath('tramites'));
  }, [sinAcceso, router]);
  const [eligiendo, setEligiendo] = useState(false);
  const [cargaMasiva, setCargaMasiva] = useState(false);
  // Al encolar un lote el listado se recarga: las primeras filas pueden estar creadas ya.
  const [refreshKey, setRefreshKey] = useState(0);

  if (sinAcceso) return null;

  return (
    <>
      <OperacionView
        onNewTramite={() => setEligiendo(true)}
        onBulkUpload={() => setCargaMasiva(true)}
        refreshKey={refreshKey}
      />

      <Modal
        open={eligiendo}
        onClose={() => setEligiendo(false)}
        title="Nuevo trámite"
        titleClassName="text-[22px] font-bold text-[#557EFF] dark:text-[#557EFF]"
        description="Selecciona el trámite principal y completa su configuración. Al iniciar entrarás directamente al Paso 1."
        size="xl"
      >
        <NuevoTramiteSelector
          tituloEnContenedor
          onElegir={(code) => {
            setEligiendo(false);
            router.push(`/tramites/nuevo/${encodeURIComponent(code)}`);
          }}
          onCancelar={() => setEligiendo(false)}
        />
      </Modal>

      <CargaMasivaModal
        open={cargaMasiva}
        onClose={() => setCargaMasiva(false)}
        onEncolado={() => setRefreshKey((k) => k + 1)}
      />
    </>
  );
}
