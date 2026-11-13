"use client";

import { useEffect, useId, useRef } from "react";
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
 */
export function SelectorModo({ modo, onModo, bloqueado = false, explicacion }: SelectorModoProps) {
  const id = useId();
  const descripcionId = `${id}-descripcion`;
  const contenedor = useRef<HTMLDivElement>(null);

  // SelectorSegmentado no expone aria-describedby: se asocia la explicación al radiogroup que pinta.
  useEffect(() => {
    contenedor.current?.querySelector('[role="radiogroup"]')?.setAttribute("aria-describedby", descripcionId);
  }, [descripcionId]);

  return (
    <div ref={contenedor} className="flex flex-col gap-1.5 lg:items-end">
      <SelectorSegmentado
        etiqueta="Tipo de diagnóstico"
        opciones={MODOS}
        valor={modo}
        onCambio={(valor) => {
          if (!bloqueado) onModo(valor);
        }}
        deshabilitado={bloqueado}
      />
      <p id={descripcionId} className={`${AYUDA_CLS} max-w-sm lg:text-right`}>
        {explicacion ?? "Express: un vehículo por placa o VIN. Masivo: varios vehículos desde la plantilla Excel."}
      </p>
    </div>
  );
}
