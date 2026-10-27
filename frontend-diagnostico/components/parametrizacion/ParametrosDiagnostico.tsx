"use client";

import type { ReactNode } from "react";
import { SeccionAlcance } from "./SeccionAlcance";
import { SeccionInformacionGeneral } from "./SeccionInformacionGeneral";
import { SeccionModo } from "./SeccionModo";
import type { Parametrizacion } from "./useParametrizacion";

export interface ParametrosDiagnosticoProps {
  /** Estado de useParametrizacion: alcance, modo y tipo compartidos por Express y Masivo. */
  p: Parametrizacion;
  nombre: string;
  onNombre: (nombre: string) => void;
  /** Deshabilita los campos mientras se envía o mientras el lote tiene un análisis a la vista. */
  disabled?: boolean;
  /** Estimado, avisos y botón de acción del modo, al final de la zona. */
  children?: ReactNode;
}

/**
 * Parámetros del diagnóstico (HU #14085): único componente de parametrización, que reemplaza a la versión compacta del
 * Express y al formulario completo del lote (HU #13752). Pinta el nombre, el alcance y el modo con sus reglas de siempre
 * (useParametrizacion); quien lo usa decide qué hacer con la cabecera validada y pone su acción en `children`.
 */
export function ParametrosDiagnostico({ p, nombre, onNombre, disabled = false, children }: ParametrosDiagnosticoProps) {
  return (
    <div className="flex flex-col gap-5">
      <SeccionInformacionGeneral
        nombre={nombre}
        onNombre={(valor) => {
          onNombre(valor);
          p.limpiarNombre();
        }}
        error={p.errores.nombre}
        disabled={disabled}
        compacta
      />
      <SeccionAlcance
        habilitadas={p.habilitadas}
        disponibles={p.disponibles}
        seleccionadas={p.seleccionadas}
        obligatorias={p.obligatorias}
        onAlternar={p.alternar}
        error={p.errores.alcance}
        disabled={disabled}
        compacta
      />
      <SeccionModo
        modo={p.modo}
        onModo={p.setModo}
        tipoCodigo={p.tipoCodigo}
        onTipo={p.setTipo}
        tipos={p.tipos}
        onReintentarTipos={p.reintentarTipos}
        errorModo={p.errores.modo}
        errorTipo={p.errores.tipo}
        disabled={disabled}
        compacta
      />
      {children}
    </div>
  );
}
