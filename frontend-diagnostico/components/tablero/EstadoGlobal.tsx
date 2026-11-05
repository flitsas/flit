"use client";

import { useId, type CSSProperties } from "react";
import type { StatusTone } from "@flit/ui/StatusBadge";
import { AYUDA_CLS } from "@/components/configuracion/estilos";
import { nombreFuente } from "@/lib/configuracion";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import type { EstimadoVsConsumido as Estimado, Grilla } from "@/lib/tablero/api";

/** Estado del lote → tono semántico FLIT: gris creado, azul en proceso, verde completado, ámbar incompleto. */
export const ESTADOS_LOTE: Record<EstadoDiagnostico, { texto: string; tono: StatusTone }> = {
  CREADO: { texto: "Creado", tono: "neutral" },
  EN_PROCESO: { texto: "En proceso", tono: "info" },
  COMPLETADO: { texto: "Completado", tono: "success" },
  INCOMPLETO: { texto: "Incompleto", tono: "warning" },
};

const chip = (tono: StatusTone): CSSProperties => ({
  background: `var(--badge-${tono}-bg)`,
  color: `var(--badge-${tono}-fg)`,
  borderColor: `var(--badge-${tono}-border)`,
});

/**
 * Estado global y avance del lote (HU #13748, F04-C4/C5): chip del estado, barra de vehículos terminados y, si el lote sigue
 * con consultas abiertas, el aviso de que la grilla se actualiza sola.
 */
export function EstadoGlobal({ grilla, sondeando }: { grilla: Grilla; sondeando: boolean }) {
  const { texto, tono } = ESTADOS_LOTE[grilla.estado] ?? { texto: grilla.estado, tono: "neutral" as const };
  const { vehiculos, terminados } = grilla.avance;
  const porcentaje = vehiculos > 0 ? Math.round((terminados / vehiculos) * 100) : 0;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        <span
          role="status"
          aria-label={`Estado del lote: ${texto}`}
          className="inline-flex items-center rounded-full border px-2.5 py-1 text-xs font-semibold whitespace-nowrap"
          style={chip(tono)}
        >
          {texto}
        </span>
        <span className="text-sm font-medium text-flit-primary dark:text-white">{`${terminados} de ${vehiculos} vehículos terminados`}</span>
        {sondeando && <span className={AYUDA_CLS}>Se actualiza sola cada 3 segundos.</span>}
      </div>
      <div
        role="progressbar"
        aria-label="Vehículos terminados"
        aria-valuemin={0}
        aria-valuemax={vehiculos}
        aria-valuenow={terminados}
        className="h-2 w-full overflow-hidden rounded-full bg-[var(--color-flit-gray)] dark:bg-white/10"
      >
        <div className="h-full rounded-full transition-[width]" style={{ width: `${porcentaje}%`, background: "var(--nav-activo)" }} />
      </div>
    </div>
  );
}

/**
 * Estimado frente a consumido por fuente (HU #13748, F04-C13). La API solo lo manda con estimado AL_FINALIZAR y el lote
 * COMPLETADO o INCOMPLETO; sin la propiedad no se pinta nada. Con minimo=true (HU #13796) las estimadas se leen «al menos N»
 * con la misma ayuda accesible que ListaEstimado (HU #13797), enlazada por aria-describedby; sin la marca, el número tal cual
 * (ampliación de HU #13802, decisión del usuario 2026-10-22). Lo usan el tablero de la compañía y la grilla del backoffice.
 */
export function EstimadoVsConsumido({ estimado }: { estimado?: Estimado[] }) {
  const ayudaId = useId();
  if (!estimado?.length) return null;
  const hayMinimo = estimado.some((e) => e.minimo);
  const celda = "border-t border-[var(--table-row-border)] px-3 py-2 text-right tabular-nums";
  return (
    <section aria-labelledby="estimado-consumido" className="flex flex-col gap-2">
      <h2 id="estimado-consumido" className="text-sm font-semibold text-flit-primary dark:text-white">
        Estimado frente a consumido
      </h2>
      <p className={AYUDA_CLS}>Consultas previstas al lanzar el lote y las que se consumieron por fuente (las reutilizadas no cuentan).</p>
      <table className="w-full max-w-xl border-collapse text-sm">
        <thead>
          <tr>
            {["Fuente", "Estimadas", "Consumidas"].map((t, i) => (
              <th
                key={t}
                scope="col"
                className={`px-3 py-2 text-[10px] font-semibold uppercase tracking-wider ${i === 0 ? "text-left" : "text-right"}`}
                style={{ background: "var(--table-header-bg)", color: "var(--table-header-fg)" }}
              >
                {t}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {estimado.map((e) => (
            <tr key={e.fuente}>
              <th scope="row" className="border-t border-[var(--table-row-border)] px-3 py-2 text-left font-medium">
                {nombreFuente(e.fuente)}
              </th>
              <td className={celda} aria-describedby={e.minimo ? ayudaId : undefined}>
                {e.minimo && <span className="text-xs font-semibold">al menos </span>}
                {e.estimadas}
              </td>
              <td className={celda}>{e.consumidas}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {hayMinimo && (
        <p id={ayudaId} className={AYUDA_CLS}>
          «Al menos» indica que el número estimado es un mínimo: las fuentes de propietario y locatario consultan a cada persona
          del vehículo, y al lanzar el lote algún vehículo no tenía sus personas registradas.
        </p>
      )}
    </section>
  );
}
