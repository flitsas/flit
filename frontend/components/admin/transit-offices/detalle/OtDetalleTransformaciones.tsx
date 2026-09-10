"use client";

import { ArrowRight } from "lucide-react";
import { OT_BLUE } from "./ot-detalle-visual";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import {
  transformacionesDeclaradas,
  type TransformacionVehiculo,
} from "@/lib/tramites/transformaciones-vehiculo";

/** Traduce el detalle del OT al contrato neutro del helper de transformaciones. */
export function transformacionesDelTramite(
  procedure: OtClientProcedure,
): TransformacionVehiculo[] {
  const flags = procedure.transformacionesDeclaradas;
  const runt = procedure.runtSnapshot;

  return transformacionesDeclaradas([
    {
      tipo: "color",
      valorRunt: runt?.color,
      valorEfectivo: procedure.color,
      declarado: flags?.color,
    },
    {
      tipo: "combustible",
      valorRunt: runt?.combustible,
      valorEfectivo: procedure.combustible,
      declarado: flags?.combustible,
    },
    {
      tipo: "carroceria",
      valorRunt: runt?.carroceria,
      valorEfectivo: procedure.carroceria,
      declarado: flags?.carroceria,
    },
  ]);
}

/** Un valor ausente se nombra; nunca se deja el hueco mudo ni se rellena con el de la otra cara. */
function Valor({ texto, ausente }: { texto: string | null; ausente: string }) {
  if (!texto) {
    return <span className="text-xs italic text-[#162744]/60 dark:text-white/60">{ausente}</span>;
  }
  return <span className="text-xs font-semibold text-[#162744] dark:text-white">{texto}</span>;
}

/**
 * Transformaciones del vehículo declaradas en el trámite, con las DOS caras a la vista: lo que el
 * RUNT tiene registrado y el valor nuevo (HU #11931).
 *
 * Sin esto el OT solo veía el valor efectivo —el nuevo— presentado como si fuera el dato oficial
 * del vehículo, que es exactamente la confusión que puede torcer una decisión de aprobación.
 */
export function OtDetalleTransformaciones({ procedure }: { procedure: OtClientProcedure }) {
  const transformaciones = transformacionesDelTramite(procedure);

  if (transformaciones.length === 0) {
    return null;
  }

  return (
    <section className="mt-4 border-t pt-3 border-[#DFE5ED] dark:border-white/10">
      <h5 className="text-xs font-semibold" style={{ color: OT_BLUE }}>
        Transformaciones declaradas frente al RUNT
      </h5>
      <ul className="mt-2 flex list-none flex-col gap-2 p-0">
        {transformaciones.map((t) => (
          <li
            key={t.tipo}
            className="rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10"
          >
            <p className="m-0 text-[10px] font-semibold uppercase tracking-wide text-[#162744]/70 dark:text-white/70">
              {t.label}
            </p>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <span className="flex flex-col">
                <span className="text-[10px] uppercase tracking-wide text-[#162744]/60 dark:text-white/60">
                  En el RUNT
                </span>
                <Valor texto={t.valorRunt} ausente="Sin dato del RUNT" />
              </span>
              <ArrowRight
                className="h-3.5 w-3.5 shrink-0"
                style={{ color: OT_BLUE }}
                aria-label="cambia a"
              />
              <span className="flex flex-col">
                <span className="text-[10px] uppercase tracking-wide text-[#162744]/60 dark:text-white/60">
                  Nuevo en el trámite
                </span>
                <Valor texto={t.valorNuevo} ausente="Sin valor nuevo capturado" />
              </span>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}
