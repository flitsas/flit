"use client";

import { CheckCircle2 } from "lucide-react";
import { nombreFuente, type Fuente } from "@/lib/configuracion";
import type { CeldaGrilla, ColumnasGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { CeldaSemaforo, type CeldaSemaforoProps } from "./CeldaSemaforo";

const GRUPOS: ReadonlyArray<{ clave: keyof ColumnasGrilla; nombre: string }> = [
  { clave: "por_vehiculo", nombre: "Por vehículo" },
  { clave: "por_propietario", nombre: "Por propietario" },
  { clave: "por_locatario", nombre: "Por locatario" },
];

/** Cabecera de tabla del sistema: barra gris de marca (--table-header-*), versalita de 10px. */
const CABECERA_CLS = "px-3 py-2.5 text-left text-[10px] font-semibold uppercase tracking-wider";
const CABECERA_ESTILO = { background: "var(--table-header-bg)", color: "var(--table-header-fg)" } as const;

const RESUELTAS: ReadonlySet<CeldaGrilla["estado"]> = new Set(["COMPLETADO", "NA", "ERROR"]);

export interface GrillaSemaforosProps extends Pick<CeldaSemaforoProps, "onReprocesada" | "onErrorReproceso"> {
  grilla: Grilla;
  /** Capacidad `reprocesarConsultas` de la sesión (admin): solo entonces hay «Reprocesar» en las celdas rojas. */
  puedeReprocesar: boolean;
}

function EncabezadoVehiculo({ vehiculo, total }: { vehiculo: FilaVehiculo; total: number }) {
  const resueltas = vehiculo.celdas.filter((c) => RESUELTAS.has(c.estado)).length;
  return (
    <th scope="row" className="px-3 py-2.5 text-left align-top font-normal">
      <span className="block font-mono text-sm font-bold uppercase tracking-wider text-flit-primary dark:text-white">{vehiculo.identificador}</span>
      <span className="mt-1 block text-xs text-[var(--nav-texto)] dark:text-slate-300">{`${resueltas} de ${total} consultas`}</span>
      {vehiculo.terminado && (
        <span
          className="mt-1 inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-semibold"
          style={{ background: "var(--badge-success-bg)", color: "var(--badge-success-fg)", borderColor: "var(--badge-success-border)" }}
        >
          <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
          Terminado
        </span>
      )}
    </th>
  );
}

/**
 * Grilla vehículo × fuente del tablero (HU #13748, F04-C1/C2/C8/C9/C10/C11): una fila por vehículo con su avance (terminado
 * apenas todas sus celdas están en verde o N/A, sin esperar a los demás) y columnas agrupadas Por vehículo, Por propietario y
 * Por locatario según el alcance del lote. El usuario no ve acciones; el admin, «Reprocesar» solo en las rojas.
 */
export function GrillaSemaforos({ grilla, puedeReprocesar, onReprocesada, onErrorReproceso }: GrillaSemaforosProps) {
  const grupos = GRUPOS.map((g) => ({ ...g, fuentes: grilla.columnas[g.clave] ?? [] })).filter((g) => g.fuentes.length > 0);
  const fuentes: Fuente[] = grupos.flatMap((g) => g.fuentes);

  return (
    <div className="overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10">
      <table aria-label="Semáforo de consultas del lote" className="w-full min-w-[640px] border-collapse text-sm">
        <thead>
          <tr>
            <th scope="col" rowSpan={2} className={`${CABECERA_CLS} align-bottom`} style={CABECERA_ESTILO}>
              Vehículo
            </th>
            {grupos.map((g) => (
              <th key={g.clave} scope="colgroup" colSpan={g.fuentes.length} className={`${CABECERA_CLS} border-l border-white/60 dark:border-white/10`} style={CABECERA_ESTILO}>
                {g.nombre}
              </th>
            ))}
          </tr>
          <tr>
            {fuentes.map((f) => (
              <th key={f} scope="col" className={`${CABECERA_CLS} border-l border-white/60 normal-case tracking-normal dark:border-white/10`} style={CABECERA_ESTILO}>
                {nombreFuente(f)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {grilla.vehiculos.map((v) => {
            const porFuente = new Map(v.celdas.map((c) => [c.fuente, c]));
            return (
              <tr key={v.id} data-vehiculo={v.id} className="border-t border-[var(--table-row-border)] bg-white dark:bg-transparent">
                <EncabezadoVehiculo vehiculo={v} total={fuentes.length} />
                {fuentes.map((f) => (
                  <CeldaSemaforo
                    key={f}
                    celda={porFuente.get(f)}
                    nombreFuente={nombreFuente(f)}
                    identificador={v.identificador}
                    puedeReprocesar={puedeReprocesar}
                    onReprocesada={onReprocesada}
                    onErrorReproceso={onErrorReproceso}
                  />
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
