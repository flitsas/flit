'use client';

import { Suspense } from 'react';
import { notFound, useParams, useRouter, useSearchParams } from 'next/navigation';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * Track B — /tramites/nuevo/[modalidad]: PASO 1 del wizard SIN trámite creado (CF-02, HU
 * #10883 AC3). Entrar aquí ya no da de alta ningún registro: el operador consulta el vehículo
 * contra el preview desacoplado y, al pulsar "Continuar", el wizard crea el trámite y esta página
 * navega a /tramites/[instanceId] (replace: el back del navegador no vuelve a este paso ya
 * consumido). Modalidad inválida → notFound().
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

  // R3 (HU #10539) — "Iniciar traspaso" siembra el vehículo del trámite de origen. Ya no se
  // persiste al crear (no hay instancia todavía): solo prellena el campo del paso 1, y el valor
  // definitivo lo fija la consulta que el operador dispare.
  const seedVin = searchParams.get('seedVin')?.trim() || undefined;
  const seedPlaca = searchParams.get('seedPlaca')?.trim() || undefined;

  return (
    <TramiteWizard
      modalidad={modalidad}
      title={modalidad === 'traspaso' ? 'Traspaso estándar' : 'Matrícula inicial'}
      seedVin={seedVin}
      seedPlaca={seedPlaca}
      onCreated={(summary) => {
        // Propaga el tenant REAL de la instancia recién creada (?t=) para que la página destino
        // fije activeTramitesTenant y las llamadas per-instance usen el MISMO tenant que la
        // creación. Sin esto, el SuperAdmin cae en jwtTenantId() (su propio tenant) ≠ el de la
        // instancia → 404 "Procedure instance not found." hasta re-entrar desde la tabla.
        router.replace(`/tramites/${summary.id}?t=${summary.tenantId}`);
      }}
      onExit={() => router.push('/tramites')}
    />
  );
}
