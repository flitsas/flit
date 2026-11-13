"use client";

import { useId } from "react";
import { SelectorSegmentado } from "@flit/ui/SelectorSegmentado";
import { AYUDA_CLS } from "@/components/configuracion/estilos";
import type { ModoCreacion } from "./borradores";

const MODOS: Array<{ valor: ModoCreacion; label: string }> = [
  { valor: "express", label: "Express" },
  { valor: "masivo", label: "Masivo" },
];

export interface SelectorModoProps {
  modo: ModoCreacion;
  onModo: (modo: ModoCreacion) => void;
  /** Mientras se envía algo o con la cabecera del lote creada: el selector no cambia de modo. */
  bloqueado?: boolean;
  /** Por qué está bloqueado, visible y asociado al grupo. */
  explicacion?: string | null;
}

/**
 * Selector Express | Masivo de Nuevo diagnóstico (HU #14562, AC16): el SelectorSegmentado del kit 2026 («Tipo de
 * diagnóstico», radiogroup con tabulación itinerante y flechas). La explicación visible bajo el selector va asociada al
 * radiogroup con aria-describedby, para que el bloqueo por la cabecera creada se anuncie.
 * HU #14562 · M10: la asociación la hace el kit (`describedBy`) y el bloqueo usa `bloqueado`, que deja el selector
 * enfocable (aria-disabled), para que teclado y lector lleguen a él y oigan el motivo.
 */
export function SelectorModo({ modo, onModo, bloqueado = false, explicacion }: SelectorModoProps) {
  const id = useId();
  const descripcionId = `${id}-descripcion`;

  return (
    <div className="flex flex-col gap-1.5 lg:items-end">
      <SelectorSegmentado
        etiqueta="Tipo de diagnóstico"
        opciones={MODOS}
        valor={modo}
        onCambio={(valor) => {
          if (!bloqueado) onModo(valor);
        }}
        bloqueado={bloqueado}
        describedBy={descripcionId}
      />
      <p id={descripcionId} className={`${AYUDA_CLS} max-w-sm lg:text-right`}>
        {explicacion ?? "Express: un vehículo por placa o VIN. Masivo: varios vehículos desde la plantilla Excel."}
      </p>
    </div>
  );
}
