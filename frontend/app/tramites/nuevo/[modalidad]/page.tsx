'use client';

import { useEffect, useRef } from 'react';
import { notFound, useParams, useRouter } from 'next/navigation';
import { useProcedureInstance } from '@/hooks/useProcedureInstance';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * Track B — /tramites/nuevo/[modalidad]: crea UNA instancia draft y redirige a
 * /tramites/[instanceId] (replace, para que el back del browser no vuelva aquí
 * y re-cree). Modalidad inválida → notFound(). Guard anti doble-create por ref
 * (StrictMode re-invoca efectos en dev).
 */
export default function NuevoTramitePage() {
  const params = useParams<{ modalidad: string }>();
  const modalidad = params.modalidad;

  if (modalidad !== 'matricula_inicial' && modalidad !== 'traspaso') {
    notFound();
  }

  return <CrearInstancia modalidad={modalidad as WizardModalidad} />;
}

function CrearInstancia({ modalidad }: { modalidad: WizardModalidad }) {
  const router = useRouter();
  const { state, start } = useProcedureInstance();
  const startedRef = useRef(false);

  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;
    void (async () => {
      const summary = await start({ modalidad });
      // Propaga el tenant REAL de la instancia recién creada (?t=) para que la página
      // destino fije activeTramitesTenant y el primer field-values use el MISMO tenant que
      // el create. Sin esto, el SuperAdmin cae en jwtTenantId() (su propio tenant) ≠ el de
      // la instancia → 404 "Procedure instance not found." hasta re-entrar desde la tabla.
      if (summary) router.replace(`/tramites/${summary.id}?t=${summary.tenantId}`);
    })();
  }, [modalidad, start, router]);

  if (state.error) {
    return (
      <div
        className="flex flex-col items-center justify-center gap-3 py-16 text-center"
        role="alert"
      >
        <p className="text-sm font-bold">No se pudo iniciar el trámite</p>
        <p className="text-xs opacity-60 max-w-xs">{state.error}</p>
        <div className="flex items-center gap-2">
          <button
            onClick={() => {
              startedRef.current = false;
              void (async () => {
                const summary = await start({ modalidad });
                if (summary) router.replace(`/tramites/${summary.id}?t=${summary.tenantId}`);
              })();
            }}
            className="px-5 py-2.5 rounded-xl text-xs font-semibold text-white"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            Reintentar
          </button>
          <button
            onClick={() => router.push('/tramites')}
            className="px-5 py-2.5 rounded-xl text-xs font-semibold border"
            style={{ borderColor: '#162744', color: '#162744' }}
          >
            Volver a Trámites
          </button>
        </div>
      </div>
    );
  }

  return (
    <div
      className="flex flex-col items-center justify-center gap-3 py-16 text-center"
      aria-busy="true"
      aria-label="Creando el trámite"
    >
      <div
        className="h-10 w-10 rounded-full border-2 border-t-transparent animate-spin"
        style={{ borderColor: '#557EFF', borderTopColor: 'transparent' }}
      />
      <p className="text-xs opacity-60">Iniciando trámite…</p>
    </div>
  );
}
