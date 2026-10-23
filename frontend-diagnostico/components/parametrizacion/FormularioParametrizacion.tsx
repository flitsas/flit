"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { Save } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import type { Configuracion } from "@/lib/configuracion";
import { crearDiagnostico, editarDiagnostico, type Diagnostico } from "@/lib/diagnosticos/api";
import { BOTON_PRIMARIO_CLS } from "@/components/configuracion/estilos";
import { SeccionAlcance } from "./SeccionAlcance";
import { SeccionInformacionGeneral } from "./SeccionInformacionGeneral";
import { SeccionModo } from "./SeccionModo";
import { useParametrizacion } from "./useParametrizacion";

export interface FormularioParametrizacionProps {
  configuracion: Configuracion;
  onGuardado?: (diagnostico: Diagnostico) => void;
}

type Envio = { tipo: "inactivo" } | { tipo: "guardando" } | { tipo: "guardado"; creado: boolean } | { tipo: "error"; mensaje: string };

/**
 * Formulario de parametrización (HU #13752): Información general, Alcance y Modo. Valida en pantalla el nombre y el
 * tipo de trámite antes de llamar a la API; crea la cabecera con POST /diagnosticos y, mientras siga en CREADO, la
 * edita con PUT /diagnosticos/{id} (un 409 avisa que ya no es editable). Los 422 se pintan en su campo y el resto de
 * rechazos en un aviso general, sin perder lo digitado.
 */
export function FormularioParametrizacion({ configuracion, onGuardado }: FormularioParametrizacionProps) {
  const p = useParametrizacion(configuracion);
  const [id, setId] = useState<string | null>(null);
  const [envio, setEnvio] = useState<Envio>({ tipo: "inactivo" });
  const [enfocar, setEnfocar] = useState(0);
  const formRef = useRef<HTMLFormElement>(null);
  const ocupado = envio.tipo === "guardando";

  // Tras un error de campo, el foco va al primer campo inválido.
  useEffect(() => {
    if (enfocar === 0) return;
    formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [enfocar]);

  async function guardar() {
    if (!p.validar()) {
      setEnvio({ tipo: "inactivo" });
      setEnfocar((n) => n + 1);
      return;
    }
    setEnvio({ tipo: "guardando" });
    try {
      const cabecera = p.cabecera();
      const guardado = id ? await editarDiagnostico(id, cabecera) : await crearDiagnostico(cabecera);
      setEnvio({ tipo: "guardado", creado: id === null });
      setId(guardado.id);
      onGuardado?.(guardado);
    } catch (e) {
      if (p.aplicarErrorApi(e)) {
        setEnvio({ tipo: "inactivo" });
        setEnfocar((n) => n + 1);
        return;
      }
      setEnvio({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo guardar el diagnóstico." });
    }
  }

  function alEnviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    void guardar();
  }

  return (
    <form ref={formRef} onSubmit={alEnviar} noValidate aria-label="Parametrización del diagnóstico" className="flex flex-col gap-4">
      <SeccionInformacionGeneral nombre={p.nombre} onNombre={p.setNombre} error={p.errores.nombre} disabled={ocupado} />
      <SeccionAlcance
        habilitadas={p.habilitadas}
        disponibles={p.disponibles}
        seleccionadas={p.seleccionadas}
        obligatorias={p.obligatorias}
        onAlternar={p.alternar}
        error={p.errores.alcance}
        disabled={ocupado}
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
        disabled={ocupado}
      />

      {envio.tipo === "error" && (
        <InlineAlert tone="error" title="No se guardó el diagnóstico">
          {envio.mensaje}
        </InlineAlert>
      )}
      {envio.tipo === "guardado" && (
        <InlineAlert tone="success">
          {envio.creado ? "Diagnóstico creado. Puedes ajustar la cabecera mientras siga en estado Creado." : "Cambios guardados."}
        </InlineAlert>
      )}

      <div className="flex justify-end">
        <button type="submit" disabled={ocupado} className={BOTON_PRIMARIO_CLS}>
          <Save className="h-4 w-4" aria-hidden="true" />
          {ocupado ? "Guardando…" : id ? "Guardar cambios" : "Crear diagnóstico"}
        </button>
      </div>
    </form>
  );
}
