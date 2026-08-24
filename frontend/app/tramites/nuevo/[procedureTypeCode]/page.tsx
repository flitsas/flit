'use client';

import { Suspense, useEffect, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { tramitesClient } from '@/lib/api/tramites-client';
import { TramiteWizard } from '@/components/operacion/TramiteWizard';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import type { ProcedureFamily, ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { FAMILY_LABEL } from '@/lib/api/types/procedure-parametrization';

/**
 * `/tramites/nuevo/[procedureTypeCode]` — paso 1 del asistente para un tipo concreto (ADR-0050).
 *
 * Antes la URL llevaba la modalidad y solo admitía dos valores fijos; el tipo se elegía dentro del
 * paso 1 con tarjetas escritas a mano. Ahora la elección ocurre en `/tramites/nuevo` contra el
 * catálogo y aquí llega el `code`, así que la ruta existe para cualquier tipo habilitado.
 *
 * El tipo se resuelve contra el catálogo en lugar de confiar en la URL: así un `code` inventado o
 * un tipo sin la barrera de operación encendida no abre el asistente.
 *
 * El asistente recibe el `code` y la familia: el primero decide cómo se conforma el recorrido y qué
 * trámite se crea; la segunda, solo el bloqueo por compañía. Aquí vivía un puente que traducía la
 * familia a una de las dos modalidades, así que cualquier tipo que no fuera traspaso —los diecisiete
 * de la familia OTROS incluidos— entraba al asistente disfrazado de matrícula inicial.
 */
export default function NuevoTramitePage() {
  return (
    // Suspense: useSearchParams() (seed de traspaso, HU #10539) exige un límite de Suspense
    // para no romper el prerender en `next build`.
    <Suspense fallback={null}>
      <PasoConsulta />
    </Suspense>
  );
}

function bloqueadaPorCompania(
  family: ProcedureFamily,
  block: { matriculas?: boolean; traspaso?: boolean; otros?: boolean } | null | undefined,
): boolean {
  if (family === 'TRASPASO') return block?.traspaso ?? false;
  if (family === 'OTROS') return block?.otros ?? false;
  return block?.matriculas ?? false;
}

type Gate =
  | { estado: 'loading' }
  | { estado: 'allowed'; tipo: ProcedureTypeSummary }
  | { estado: 'blocked'; family: ProcedureFamily }
  | { estado: 'not-found' };

function PasoConsulta() {
  const params = useParams<{ procedureTypeCode: string }>();
  const code = decodeURIComponent(params.procedureTypeCode ?? '');
  const router = useRouter();
  const searchParams = useSearchParams();
  const [gate, setGate] = useState<Gate>({ estado: 'loading' });

  const seedVin = searchParams.get('seedVin')?.trim() || undefined;
  const seedPlaca = searchParams.get('seedPlaca')?.trim() || undefined;

  useEffect(() => {
    let active = true;

    void Promise.all([
      tramitesClient.listPublishedProcedureTypes(),
      tramitesClient.getConsultationConfig().catch(() => null),
    ])
      .then(([tipos, cfg]) => {
        if (!active) return;

        const tipo = tipos.find((t) => t.code === code && t.wizardEnabled);
        if (!tipo) {
          setGate({ estado: 'not-found' });
          return;
        }

        setGate(
          bloqueadaPorCompania(tipo.family, cfg?.blockProcedureFamily)
            ? { estado: 'blocked', family: tipo.family }
            : { estado: 'allowed', tipo },
        );
      })
      .catch(() => {
        // Sin catálogo legible no se puede saber qué trámite es: mejor no abrir el asistente.
        if (active) setGate({ estado: 'not-found' });
      });

    return () => {
      active = false;
    };
  }, [code]);

  if (gate.estado === 'loading') {
    return <CarLoaderModal label="Verificando permisos de la compañía…" />;
  }

  if (gate.estado === 'not-found') {
    return (
      <div className="space-y-4 rounded-2xl border px-4 py-6" role="alert">
        <p className="text-sm font-semibold text-[#162744] dark:text-white">
          Este trámite no está disponible
        </p>
        <p className="text-xs opacity-70">
          El tipo de trámite no existe o todavía no está habilitado para crearse.
        </p>
        <VolverATramites onClick={() => router.push('/tramites/nuevo')} label="Elegir otro trámite" />
      </div>
    );
  }

  if (gate.estado === 'blocked') {
    return (
      <div className="space-y-4 rounded-2xl border px-4 py-6" role="alert">
        <p className="text-sm font-semibold text-[#162744] dark:text-white">
          No se puede iniciar este trámite
        </p>
        <p className="text-xs opacity-70">
          La compañía tiene bloqueada la creación de trámites de {FAMILY_LABEL[gate.family]}. Contacta
          al administrador para habilitarla en la configuración de la compañía.
        </p>
        <VolverATramites onClick={() => router.push('/tramites')} label="Volver a trámites" />
      </div>
    );
  }

  return (
    <TramiteWizard
      family={gate.tipo.family}
      procedureTypeCode={gate.tipo.code}
      title={gate.tipo.name}
      seedVin={seedVin}
      seedPlaca={seedPlaca}
      onCreated={(summary) => {
        router.replace(`/tramites/${summary.id}?t=${summary.tenantId}`);
      }}
      onExit={() => router.push('/tramites')}
    />
  );
}

function VolverATramites({ onClick, label }: { onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
      style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
    >
      {label}
    </button>
  );
}
