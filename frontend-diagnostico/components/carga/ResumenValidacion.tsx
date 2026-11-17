"use client";

import { ArrowRight, PencilLine } from "lucide-react";
import type { ResumenValidacion as Resumen } from "@/lib/carga/api";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ERROR_CLS } from "@/components/parametrizacion/estilos";
import { EstimadoConsultas } from "./EstimadoConsultas";

export interface ResumenValidacionProps {
  resumen: Resumen;
  onCorregir?: () => void;
  onContinuar: () => void;
  confirmando?: boolean;
}

/** Motivo por el que no se puede continuar: el aviso de la API o, si no llegó, el que se deduce de los conteos. */
export function motivoBloqueo(resumen: Resumen): string | null {
  if (resumen.puede_continuar) return null;
  if (resumen.avisos.length) return resumen.avisos.map((a) => a.mensaje).join(" ");
  if (resumen.validos === 0) return "No hay vehículos válidos para continuar.";
  return `Hay ${resumen.validos} vehículos válidos y el límite por lote es ${resumen.limite_vehiculos_lote}.`;
}

// Conteo con su semántica: verde válido, rojo/naranja error y gris para lo consolidado.
const CONTEOS = [
  { clave: "validos", etiqueta: "Válidos", tono: "success" },
  { clave: "erroneos", etiqueta: "Erróneos", tono: "danger" },
  { clave: "duplicados", etiqueta: "Duplicados", tono: "neutral" },
] as const;

/**
 * Resumen de Validación de la carga (HU #13754, F02-C7/C8/C10/C11): los conteos de válidos, erróneos y duplicados, el
 * estimado por fuente cuando la API lo trae y las acciones «Corregir ahora» y «Continuar solo con los válidos». Sin
 * válidos o por encima del límite, continuar queda deshabilitado con el motivo visible y asociado al botón.
 */
export function ResumenValidacion({ resumen, onCorregir, onContinuar, confirmando = false }: ResumenValidacionProps) {
  const motivo = motivoBloqueo(resumen);
  return (
    <section aria-labelledby="resumen-titulo" className={`${TARJETA_CLS} flex flex-col gap-4`}>
      <div className="flex flex-col gap-1">
        <h2 id="resumen-titulo" className="text-lg font-semibold text-flit-primary dark:text-white">
          Resumen de validación
        </h2>
        <p className={AYUDA_CLS}>
          {resumen.total_filas} filas leídas · límite por lote: {resumen.limite_vehiculos_lote} vehículos
        </p>
      </div>

      <dl className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        {CONTEOS.map((c) => (
          <div
            key={c.clave}
            className="flex flex-col gap-1 rounded-xl border px-4 py-3"
            style={{ borderColor: `var(--badge-${c.tono}-border)`, background: `var(--badge-${c.tono}-bg)` }}
          >
            <dt className="text-xs font-semibold uppercase tracking-wider" style={{ color: `var(--badge-${c.tono}-fg)` }}>
              {c.etiqueta}
            </dt>
            <dd className="text-2xl font-semibold text-flit-primary dark:text-white">{resumen[c.clave]}</dd>
          </div>
        ))}
      </dl>

      <EstimadoConsultas estimado={resumen.estimado_consultas} />

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-end">
        {motivo && (
          <p id="resumen-motivo" className={`${ERROR_CLS} sm:mr-auto`}>
            {motivo}
          </p>
        )}
        {onCorregir && resumen.erroneos > 0 && (
          <button type="button" onClick={onCorregir} className={BOTON_SECUNDARIO_CLS}>
            <PencilLine className="h-4 w-4" aria-hidden="true" />
            Corregir ahora
          </button>
        )}
        <button
          type="button"
          onClick={onContinuar}
          disabled={motivo !== null || confirmando}
          aria-describedby={motivo ? "resumen-motivo" : undefined}
          className={BOTON_PRIMARIO_CLS}
        >
          {confirmando ? "Confirmando…" : "Continuar solo con los válidos"}
          <ArrowRight className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    </section>
  );
}
