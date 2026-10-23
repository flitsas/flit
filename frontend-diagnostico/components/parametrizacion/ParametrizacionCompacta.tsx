"use client";

import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { ArrowRight } from "lucide-react";
import type { Configuracion } from "@/lib/configuracion";
import type { CabeceraEntrada } from "@/lib/diagnosticos/api";
import { nombreExpress } from "@/lib/diagnosticos/nombreExpress";
import { BOTON_PRIMARIO_CLS } from "@/components/configuracion/estilos";
import { SeccionAlcance } from "./SeccionAlcance";
import { SeccionInformacionGeneral } from "./SeccionInformacionGeneral";
import { SeccionModo } from "./SeccionModo";
import { useParametrizacion } from "./useParametrizacion";

export interface ParametrizacionCompactaProps {
  identificador: string;
  configuracion: Configuracion;
  onContinuar: (cabecera: CabeceraEntrada) => void;
  fecha?: Date;
  /** Deshabilita los campos mientras quien lo usa envía la cabecera (Express, #13718). */
  ocupado?: boolean;
  /** Campos propios de quien la usa, dentro del formulario y antes de Continuar (documento del locatario, #13755). */
  children?: ReactNode;
}

/**
 * Versión compacta de la parametrización para el Diagnóstico Express (HU #13752, F01-C10): el nombre llega autogenerado
 * con nombreExpress(identificador, fecha), es editable y sigue siendo obligatorio; alcance y modo con las mismas reglas
 * del formulario completo. No llama a la API de la cabecera: entrega la cabecera validada a quien la lanza.
 */
export function ParametrizacionCompacta({ identificador, configuracion, onContinuar, fecha, ocupado = false, children }: ParametrizacionCompactaProps) {
  const [nombreInicial] = useState(() => nombreExpress(identificador, fecha ?? new Date()));
  const p = useParametrizacion(configuracion, nombreInicial);
  const formRef = useRef<HTMLFormElement>(null);
  const [enfocar, setEnfocar] = useState(0);

  useEffect(() => {
    if (enfocar === 0) return;
    formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [enfocar]);

  function alEnviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!p.validar()) {
      setEnfocar((n) => n + 1);
      return;
    }
    onContinuar(p.cabecera());
  }

  return (
    <form ref={formRef} onSubmit={alEnviar} noValidate aria-label="Parametrización del diagnóstico express" className="flex flex-col gap-4">
      <SeccionInformacionGeneral nombre={p.nombre} onNombre={p.setNombre} error={p.errores.nombre} disabled={ocupado} compacta />
      <SeccionAlcance
        habilitadas={p.habilitadas}
        disponibles={p.disponibles}
        seleccionadas={p.seleccionadas}
        obligatorias={p.obligatorias}
        onAlternar={p.alternar}
        error={p.errores.alcance}
        disabled={ocupado}
        compacta
      />
      <SeccionModo
        modo={p.modo}
        onModo={p.setModo}
        tipoCodigo={p.tipoCodigo}
        onTipo={p.setTipo}
        tipos={p.tipos}
        onReintentarTipos={p.reintentarTipos}
        errorTipo={p.errores.tipo}
        disabled={ocupado}
        compacta
      />
      {children}
      <div className="flex justify-end">
        <button type="submit" disabled={ocupado} className={BOTON_PRIMARIO_CLS}>
          Continuar
          <ArrowRight className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    </form>
  );
}
