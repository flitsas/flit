"use client";

import { useParams, useSearchParams } from "next/navigation";
import { TrazabilidadScreen } from "@/components/logqx/TrazabilidadScreen";

/**
 * Trazabilidad de una radicación Quipux (HU #11789/#11790, ruta `/log-qx/{submissionId}`).
 *
 * Ruta PROPIA y no una pestaña del detalle del trámite (ADR-0051, D3): así el gate de `logqx.read`
 * se aplica sobre una ruta dedicada, sin condicionar una pantalla que ven otros roles. Mismo patrón
 * que `/tramites/[instanceId]`.
 *
 * El query string trae los filtros con los que venía la bandeja, para reconstruirla al volver: el
 * agente que estaba mirando «los fallidos de esta semana» no debe perder esa lista por abrir uno.
 */
export default function LogQxTrazabilidadPage() {
  const params = useParams<{ submissionId: string }>();
  const searchParams = useSearchParams();

  const qs = searchParams.toString();
  const volverHref = `/?m=log-qx${qs ? `&${qs}` : ""}`;

  return <TrazabilidadScreen submissionId={params.submissionId} volverHref={volverHref} />;
}
