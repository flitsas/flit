'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Modal } from '@/components/atom/Modal';
import { OperacionView } from '@/components/operacion/OperacionView';
import { NuevoTramiteSelector } from '@/components/operacion/NuevoTramiteSelector';

/**
 * Track B — /tramites: listado de operación (KPIs + tabs + tabla).
 *
 * "Nuevo trámite" abre la elección del trámite EN UN MODAL sobre el listado, no navegando a otra
 * pantalla. Es el patrón que FLIT ya usa para lo que se lanza desde un listado —consulta de vehículo,
 * invitar colaborador—: elegir el tipo es una decisión corta, no un destino. Y sobre todo, cancelar
 * deja de costar el estado de la vista: filtros, página y scroll siguen ahí detrás, que es
 * exactamente lo que se perdía al navegar y volver.
 *
 * La ruta `/tramites/nuevo` se conserva para enlaces directos y para el botón atrás; las dos
 * presentaciones montan el MISMO `NuevoTramiteSelector`, así que no hay dos comportamientos que
 * mantener sincronizados.
 */
export default function TramitesPage() {
  const router = useRouter();
  const [eligiendo, setEligiendo] = useState(false);

  return (
    <>
      <OperacionView onNewTramite={() => setEligiendo(true)} />

      <Modal
        open={eligiendo}
        onClose={() => setEligiendo(false)}
        title="Selecciona el tipo de trámite"
        description="Define el trámite principal que se radicará con este expediente."
        size="lg"
      >
        <NuevoTramiteSelector
          // La cabecera del diálogo ya dice título y subtítulo, y de ella cuelga el
          // `aria-labelledby`: repetirlos dentro dejaría dos encabezados seguidos iguales.
          tituloEnContenedor
          onElegir={(code) => router.push(`/tramites/nuevo/${encodeURIComponent(code)}`)}
          onCancelar={() => setEligiendo(false)}
        />
      </Modal>
    </>
  );
}
