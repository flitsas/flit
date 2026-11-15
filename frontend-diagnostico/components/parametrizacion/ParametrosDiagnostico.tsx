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
  /**
   * `vertical` (por defecto): Información general, Alcance y Modo apilados. `horizontal` (FE-3, pantalla de creación a una
   * columna): Información general · Modo · Alcance lado a lado desde md (2 columnas, Alcance debajo a ancho completo) y en
   * 3 columnas desde xl, con Alcance al doble de ancho por sus tres grupos.
   */
  disposicion?: "vertical" | "horizontal";
}

const SEPARADOR_XL_CLS = "xl:border-l xl:border-[color:var(--di-divider)] xl:pl-6";

/**
 * Parámetros del diagnóstico (HU #14085): único componente de parametrización, que reemplaza a la versión compacta del
 * Express y al formulario completo del lote (HU #13752). Pinta el nombre, el alcance y el modo con sus reglas de siempre
 * (useParametrizacion); quien lo usa decide qué hacer con la cabecera validada y pone su acción en `children`.
 */
export function ParametrosDiagnostico({ p, nombre, onNombre, disabled = false, children, disposicion = "vertical" }: ParametrosDiagnosticoProps) {
  const informacion = (
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
  );
  const alcance = (columnas: boolean) => (
    <SeccionAlcance
      habilitadas={p.contratadas}
      disponibles={p.disponibles}
      seleccionadas={p.seleccionadas}
      obligatorias={p.obligatorias}
      onAlternar={p.alternar}
      error={p.errores.alcance}
      disabled={disabled}
      compacta
      columnas={columnas}
    />
  );
  const modo = (
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
  );

  if (disposicion === "horizontal") {
    return (
      <div className="flex flex-col gap-5">
        <div
          data-disposicion="horizontal"
          className="grid grid-cols-1 gap-6 md:grid-cols-2 xl:grid-cols-[minmax(14rem,1fr)_minmax(16rem,1fr)_minmax(0,2fr)]"
        >
          <div data-zona="informacion" className="min-w-0">
            {informacion}
          </div>
          <div data-zona="modo" className={`min-w-0 ${SEPARADOR_XL_CLS}`}>
            {modo}
          </div>
          <div data-zona="alcance" className={`min-w-0 md:col-span-2 xl:col-span-1 ${SEPARADOR_XL_CLS}`}>
            {alcance(true)}
          </div>
        </div>
        {children}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-5">
      {informacion}
      {alcance(false)}
      {modo}
      {children}
    </div>
  );
}
