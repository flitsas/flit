'use client';

import { useCallback, useEffect, useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';
import type {
  ConformationProfile,
  ProcedureStep,
  ProcedureTypeSummary,
  ValidationResult,
} from '@/lib/api/types/procedure-parametrization';

/**
 * Estado del configurador de tipos de trámite (ADR-0050).
 *
 * El catálogo y el detalle se cargan por separado a propósito: el listado son 21 filas que se ven de
 * una vez, y el perfil de un tipo son tres llamadas (perfil, pasos, validación) que solo tienen
 * sentido cuando alguien lo abre.
 */
export interface DetalleTipo {
  perfil: ConformationProfile;
  pasos: ProcedureStep[];
  validacion: ValidationResult | null;
}

export function useTiposTramite() {
  const [tipos, setTipos] = useState<ProcedureTypeSummary[]>([]);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const recargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      setTipos(await superadminClient.listProcedureTypes());
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo cargar el catálogo de tipos.');
    } finally {
      setCargando(false);
    }
  }, []);

  useEffect(() => {
    void recargar();
  }, [recargar]);

  return { tipos, cargando, error, recargar, setTipos };
}

/**
 * Carga el detalle de un tipo. La validación se pide siempre: es lo que dice si el tipo puede
 * habilitarse, y esperar a que el gestor pulse un botón para averiguarlo esconde justo el dato que
 * necesita para decidir.
 */
export function useDetalleTipo(id: string | null) {
  const [detalle, setDetalle] = useState<DetalleTipo | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const recargar = useCallback(async () => {
    if (!id) {
      setDetalle(null);
      return;
    }
    setCargando(true);
    setError(null);
    try {
      const [perfil, pasos, validacion] = await Promise.all([
        superadminClient.getConformationProfile(id),
        superadminClient.getSteps(id),
        superadminClient.validate(id).catch(() => null),
      ]);
      setDetalle({ perfil, pasos, validacion });
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo cargar la configuración del tipo.');
      setDetalle(null);
    } finally {
      setCargando(false);
    }
  }, [id]);

  useEffect(() => {
    void recargar();
  }, [recargar]);

  return { detalle, cargando, error, recargar };
}
