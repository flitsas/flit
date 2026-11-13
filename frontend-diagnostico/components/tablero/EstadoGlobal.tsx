"use client";

import { useId } from "react";
import { BarraProgreso } from "@flit/ui/BarraProgreso";
import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";
import { ETIQUETA_PUNTO_CLS, etiquetaKit, type StatusTone } from "@flit/ui/StatusBadge";
import { TABLA_HEADER_CELL_CLS } from "@flit/ui/table-styles";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";
import { AYUDA_CLS } from "@/components/configuracion/estilos";
import { nombreFuente } from "@/lib/configuracion";
import { rotular } from "@/lib/rotulos";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import type { EstimadoVsConsumido as Estimado, Grilla } from "@/lib/tablero/api";

/** Estado del lote → tono semántico FLIT: gris creado, azul en proceso, verde completado, ámbar incompleto. */
export const ESTADOS_LOTE: Record<EstadoDiagnostico, { texto: string; tono: StatusTone }> = {
  CREADO: { texto: "Creado", tono: "neutral" },
  EN_PROCESO: { texto: "En proceso", tono: "info" },
  COMPLETADO: { texto: "Completado", tono: "success" },
  INCOMPLETO: { texto: "Incompleto", tono: "warning" },
};

/** Texto y tono del estado del lote: la etiqueta de la API primero; un estado desconocido, gris «Novedad sin clasificar». */
export function vistaEstadoLote(estado: EstadoDiagnostico, etiqueta?: string | null): { texto: string; tono: StatusTone } {
  const local = ESTADOS_LOTE[estado];
  const texto = rotular({ contexto: "estado del lote", codigo: estado, etiqueta, respaldo: (c) => ESTADOS_LOTE[c as EstadoDiagnostico]?.texto });
  return { texto, tono: local?.tono ?? "neutral" };
}

/**
 * Estado global y avance del lote (HU #13748, F04-C4/C5): chip del estado, barra de vehículos terminados y, si el lote sigue
 * con consultas abiertas, el aviso de que la grilla se actualiza sola. HU #14562 (auditoría m-5): el lote EN_PROCESO usa la
 * EtiquetaEnProceso del kit (punto pulsante, quieto con menos movimiento) y el avance, la BarraProgreso del kit.
 */
export function EstadoGlobal({ grilla, sondeando }: { grilla: Grilla; sondeando: boolean }) {
  const { texto, tono } = vistaEstadoLote(grilla.estado, grilla.estado_etiqueta);
  const { vehiculos, terminados } = grilla.avance;
  const avance = `${terminados} de ${vehiculos} vehículos terminados`;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        {grilla.estado === "EN_PROCESO" ? (
          <EtiquetaEnProceso etiqueta={texto} />
        ) : (
          <span role="status" aria-label={`Estado del lote: ${texto}`} {...etiquetaKit(tono)}>
            <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
            {texto}
          </span>
        )}
        {/* HU #14562 AC25: solo se anuncia cuando N cambia; el sondeo deja el mismo texto y no hay anuncio. */}
        <span aria-live="polite" className="text-sm font-medium text-flit-primary">{avance}</span>
        {sondeando && <span className={AYUDA_CLS}>Se actualiza sola cada 3 segundos.</span>}
      </div>
      <BarraProgreso etiqueta="Vehículos terminados" valor={terminados} maximo={vehiculos} textoValor={avance} />
    </div>
  );
}

/**
 * Estimado frente a consumido por fuente (HU #13748, F04-C13). La API solo lo manda con estimado AL_FINALIZAR y el lote
 * COMPLETADO o INCOMPLETO; sin la propiedad no se pinta nada. Con minimo=true (HU #13796) las estimadas se leen «al menos N»
 * con la misma ayuda accesible que ListaEstimado (HU #13797), enlazada por aria-describedby; sin la marca, el número tal cual
 * (ampliación de HU #13802, decisión del usuario 2026-10-22). Lo usan el tablero de la compañía y la grilla del backoffice.
 */
export function EstimadoVsConsumido({ estimado, plegable = false }: { estimado?: Estimado[]; /** HU #14562 AC33: tarjeta plegable (tablero). */ plegable?: boolean }) {
  const ayudaId = useId();
  if (!estimado?.length) return null;
  const hayMinimo = estimado.some((e) => e.minimo);
  const celda = "border-t border-[var(--table-row-border)] px-3 py-2 text-right tabular-nums";
  const cuerpo = (
    <>
      <p className={AYUDA_CLS}>Consultas previstas al lanzar el lote y las que se consumieron por fuente (las reutilizadas no cuentan).</p>
      <table className="w-full max-w-xl border-collapse text-sm">
        <thead>
          <tr>
            {["Fuente", "Estimadas", "Consumidas"].map((t, i) => (
              <th
                key={t}
                scope="col"
                className={i === 0 ? TABLA_HEADER_CELL_CLS : TABLA_HEADER_CELL_CLS.replace("text-left", "text-right")}
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
    </>
  );
  if (plegable) {
    return (
      <section aria-label="Estimado frente a consumido">
        <TarjetaPlegable titulo="Estimado frente a consumido" nivelTitulo={2}>
          <div className="flex flex-col gap-2">{cuerpo}</div>
        </TarjetaPlegable>
      </section>
    );
  }
  return (
    <section aria-labelledby="estimado-consumido" className="flex flex-col gap-2">
      <h2 id="estimado-consumido" className="text-sm font-semibold text-flit-primary">
        Estimado frente a consumido
      </h2>
      {cuerpo}
    </section>
  );
}
