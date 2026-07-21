'use client';

import { Suspense, useCallback, useEffect, useRef } from 'react';
import { notFound, useParams, useRouter, useSearchParams } from 'next/navigation';
import { useProcedureInstance } from '@/hooks/useProcedureInstance';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  FieldValueInput,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * Track B — /tramites/nuevo/[modalidad]: crea UNA instancia draft y redirige a
 * /tramites/[instanceId] (replace, para que el back del browser no vuelva aquí
 * y re-cree). Modalidad inválida → notFound(). Guard anti doble-create por ref
 * (StrictMode re-invoca efectos en dev).
 */
export default function NuevoTramitePage() {
  const params = useParams<{ modalidad: string }>();
  const modalidad = params.modalidad;

  // FEATURE-08 / HU-FE-06 (AC-04): el segmento puede ser una modalidad legacy
  // (matricula_inicial/traspaso) o un procedureTypeCode publicado. Solo un segmento vacío es inválido.
  if (!modalidad) {
    notFound();
  }

  // Suspense: useSearchParams() (seed de traspaso, HU #10539) exige un límite de Suspense
  // para no romper el prerender en `next build`.
  return (
    <Suspense fallback={null}>
      <CrearInstancia segment={modalidad} />
    </Suspense>
  );
}

const KNOWN_MODALIDADES: readonly string[] = ['matricula_inicial', 'traspaso'];

function CrearInstancia({ segment }: { segment: string }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { state, start } = useProcedureInstance();
  const startedRef = useRef(false);

  // Crea la instancia y, si venimos de la ruta de traspaso con seed (R3/HU #10539: VIN ya
  // matriculado), persiste placa/VIN en el borrador con el tenant EXPLÍCITO de la instancia recién
  // creada (no el activo, que aún no se fijó) antes de redirigir. El seed es best-effort: si el PATCH
  // falla, el gestor captura placa/VIN a mano en el paso 1 (no se aborta la creación del trámite).
  const startFlow = useCallback(async () => {
    // FEATURE-08 / HU-FE-06: modalidad legacy → start por modalidad; cualquier otro segmento →
    // procedureTypeCode (tipo publicado del configurador dinámico).
    const summary = KNOWN_MODALIDADES.includes(segment)
      ? await start({ modalidad: segment as WizardModalidad })
      : await start({ procedureTypeCode: segment });
    if (!summary) return;
    const seedVin = searchParams.get('seedVin')?.trim();
    const seedPlaca = searchParams.get('seedPlaca')?.trim();
    const items: FieldValueInput[] = [];
    if (seedVin) items.push({ formFieldId: null, fieldKey: 'vin', valueText: seedVin, valueJson: null });
    if (seedPlaca) items.push({ formFieldId: null, fieldKey: 'plate', valueText: seedPlaca, valueJson: null });
    if (items.length > 0) {
      try {
        await tramitesClient.patchFieldValues(summary.id, items, summary.tenantId);
      } catch {
        // Seed best-effort: se ignora el fallo y se continúa a la instancia.
      }
    }
    // Propaga el tenant REAL de la instancia recién creada (?t=) para que la página
    // destino fije activeTramitesTenant y el primer field-values use el MISMO tenant que
    // el create. Sin esto, el SuperAdmin cae en jwtTenantId() (su propio tenant) ≠ el de
    // la instancia → 404 "Procedure instance not found." hasta re-entrar desde la tabla.
    router.replace(`/tramites/${summary.id}?t=${summary.tenantId}`);
  }, [segment, start, searchParams, router]);

  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;
    void startFlow();
  }, [startFlow]);

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
              startedRef.current = true;
              void startFlow();
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
