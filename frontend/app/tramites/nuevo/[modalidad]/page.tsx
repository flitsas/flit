'use client';

import { Suspense, useEffect, useState } from 'react';
import { notFound, useParams, useRouter, useSearchParams } from 'next/navigation';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';
import { tramitesClient } from '@/lib/api/tramites-client';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * Track B — /tramites/nuevo/[modalidad]: PASO 1 del wizard SIN trámite creado (CF-02, HU
 * #10883 AC3). Entrar aquí ya no da de alta ningún registro: el operador consulta el vehículo
 * contra el preview desacoplado y, al pulsar "Continuar", el wizard crea el trámite y esta página
 * navega a /tramites/[instanceId] (replace: el back del navegador no vuelve a este paso ya
 * consumido). Modalidad inválida → notFound().
 *
 * Si la compañía tiene bloqueada la familia del trámite, se muestra el aviso y no se abre el wizard.
 */
export default function NuevoTramitePage() {
  const params = useParams<{ modalidad: string }>();
  const modalidad = params.modalidad;

  if (modalidad !== 'matricula_inicial' && modalidad !== 'traspaso') {
    notFound();
  }

  // Suspense: useSearchParams() (seed de traspaso, HU #10539) exige un límite de Suspense
  // para no romper el prerender en `next build`.
  return (
    <Suspense fallback={null}>
      <PasoConsulta modalidad={modalidad as WizardModalidad} />
    </Suspense>
  );
}

function PasoConsulta({ modalidad }: { modalidad: WizardModalidad }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [gate, setGate] = useState<'loading' | 'allowed' | 'blocked'>('loading');

  const seedVin = searchParams.get('seedVin')?.trim() || undefined;
  const seedPlaca = searchParams.get('seedPlaca')?.trim() || undefined;

  useEffect(() => {
    let active = true;
    void tramitesClient
      .getConsultationConfig()
      .then((cfg) => {
        if (!active) return;
        const block = cfg.blockProcedureFamily;
        const blocked =
          modalidad === 'matricula_inicial'
            ? (block?.matriculas ?? false)
            : (block?.traspaso ?? false);
        setGate(blocked ? 'blocked' : 'allowed');
      })
      .catch(() => {
        // Si no se puede leer la config, no bloqueamos el flujo (el backend sí corta al crear).
        if (active) setGate('allowed');
      });
    return () => {
      active = false;
    };
  }, [modalidad]);

  if (gate === 'loading') {
    return (
      // Mismo velo que las consultas al RUNT: mientras se resuelve el gate no hay nada más en
      // pantalla, y una línea suelta no se leía como una espera.
      <CarLoaderModal label="Verificando permisos de la compañía…" />
    );
  }

  if (gate === 'blocked') {
    const label =
      modalidad === 'traspaso' ? 'trámites de traspaso' : 'trámites de matrículas';
    return (
      <div className="space-y-4 rounded-2xl border px-4 py-6" role="alert">
        <p className="text-sm font-semibold text-[#162744] dark:text-white">
          No se puede iniciar este trámite
        </p>
        <p className="text-xs opacity-70">
          La compañía tiene bloqueada la creación de {label}. Contacta al administrador para
          habilitarla en la configuración de la compañía.
        </p>
        <button
          type="button"
          onClick={() => router.push('/tramites')}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
        >
          Volver a trámites
        </button>
      </div>
    );
  }

  return (
    <TramiteWizard
      modalidad={modalidad}
      title={modalidad === 'traspaso' ? 'Traspaso estándar' : 'Matrícula inicial'}
      seedVin={seedVin}
      seedPlaca={seedPlaca}
      onCreated={(summary) => {
        router.replace(`/tramites/${summary.id}?t=${summary.tenantId}`);
      }}
      onExit={() => router.push('/tramites')}
    />
  );
}
