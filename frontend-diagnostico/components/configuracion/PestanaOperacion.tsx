"use client";

import { useState, type FormEvent } from "react";
import { Save } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { ApiError } from "@/lib/api";
import {
  ESTIMADOS,
  VISIBILIDADES,
  contratoCambioDespuesDeOperacion,
  guardarOperacion,
  nombreFuente,
  ordenarFuentes,
  type Configuracion,
  type EstimadoConsultas,
  type Fuente,
  type VisibilidadUsuario,
} from "@/lib/configuracion";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, ETIQUETA_CLS, MARCA_CLS, OPCION_CLS, TARJETA_CLS } from "./estilos";

type Estado = { tipo: "inactivo" } | { tipo: "guardando" } | { tipo: "guardado" } | { tipo: "error"; mensaje: string; reintentable: boolean };

/**
 * Nivel operativo de la compañía del token: visibilidad, fuentes por defecto (solo entre las habilitadas del contrato)
 * y estimado de consultas. Sin diagnostico.configuracion.editar todo queda deshabilitado. Un rechazo de la API (400, 403,
 * 422) se muestra sin perder lo digitado; un error del servidor (hoy también el conflicto de concurrencia, que responde
 * 500) ofrece Reintentar.
 */
export function PestanaOperacion({
  configuracion,
  puedeEditar,
  onGuardado,
}: {
  configuracion: Configuracion;
  puedeEditar: boolean;
  onGuardado?: (configuracion: Configuracion) => void;
}) {
  const habilitadas = ordenarFuentes(configuracion.contrato.fuentes_habilitadas);
  const [visibilidad, setVisibilidad] = useState<VisibilidadUsuario>(configuracion.operacion.visibilidad_usuario);
  const [porDefecto, setPorDefecto] = useState<Fuente[]>(configuracion.operacion.fuentes_por_defecto);
  const [estimado, setEstimado] = useState<EstimadoConsultas>(configuracion.operacion.estimado_consultas);
  const [estado, setEstado] = useState<Estado>({ tipo: "inactivo" });
  const deshabilitado = !puedeEditar || estado.tipo === "guardando";

  function alternar(fuente: Fuente) {
    setPorDefecto((actual) => (actual.includes(fuente) ? actual.filter((f) => f !== fuente) : ordenarFuentes([...actual, fuente])));
  }

  async function guardar() {
    if (!puedeEditar) return;
    setEstado({ tipo: "guardando" });
    try {
      const guardada = await guardarOperacion({
        visibilidad_usuario: visibilidad,
        fuentes_por_defecto: porDefecto.filter((f) => habilitadas.includes(f)),
        estimado_consultas: estimado,
      });
      setVisibilidad(guardada.operacion.visibilidad_usuario);
      setPorDefecto(guardada.operacion.fuentes_por_defecto);
      setEstimado(guardada.operacion.estimado_consultas);
      setEstado({ tipo: "guardado" });
      onGuardado?.(guardada);
    } catch (e) {
      const reintentable = !(e instanceof ApiError) || e.status >= 500;
      setEstado({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo guardar la configuración.", reintentable });
    }
  }

  function alEnviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    void guardar();
  }

  return (
    <form onSubmit={alEnviar} aria-labelledby="operacion-titulo" className={`${TARJETA_CLS} flex flex-col gap-5`} noValidate>
      <div>
        <h2 id="operacion-titulo" className="text-lg font-semibold text-flit-primary dark:text-white">
          Operación de la compañía
        </h2>
        <p className={AYUDA_CLS}>Cómo trabaja tu compañía con Diagnóstico dentro de lo contratado.</p>
      </div>

      {!puedeEditar && (
        <InlineAlert tone="info" title="Solo lectura">
          No tienes permiso para editar la operación. Pídele el cambio al administrador de tu compañía.
        </InlineAlert>
      )}
      {contratoCambioDespuesDeOperacion(configuracion) && (
        <InlineAlert tone="info" title="El contrato cambió">
          El backoffice FLIT actualizó el contrato después del último cambio de esta operación: si redujo las fuentes habilitadas, se quitaron las fuentes por
          defecto que dejaron de estar habilitadas. Revísalas.
        </InlineAlert>
      )}

      <fieldset className="flex flex-col gap-2">
        <legend className={ETIQUETA_CLS}>Visibilidad de los diagnósticos</legend>
        <div className="grid gap-2 sm:grid-cols-2">
          {VISIBILIDADES.map((v) => (
            <label key={v.valor} className={OPCION_CLS}>
              <input
                type="radio"
                name="visibilidad"
                value={v.valor}
                checked={visibilidad === v.valor}
                onChange={() => setVisibilidad(v.valor)}
                disabled={deshabilitado}
                className={MARCA_CLS}
              />
              {v.nombre}
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset className="flex flex-col gap-2">
        <legend className={ETIQUETA_CLS}>Fuentes por defecto</legend>
        <p className={AYUDA_CLS}>Se proponen al crear un diagnóstico. Solo puedes elegir entre las fuentes habilitadas en el contrato.</p>
        {habilitadas.length === 0 ? (
          <p className={AYUDA_CLS}>El contrato no tiene fuentes habilitadas.</p>
        ) : (
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {habilitadas.map((fuente) => (
              <label key={fuente} className={OPCION_CLS}>
                <input
                  type="checkbox"
                  name="fuentes"
                  value={fuente}
                  checked={porDefecto.includes(fuente)}
                  onChange={() => alternar(fuente)}
                  disabled={deshabilitado}
                  className={MARCA_CLS}
                />
                {nombreFuente(fuente)}
              </label>
            ))}
          </div>
        )}
      </fieldset>

      <fieldset className="flex flex-col gap-2">
        <legend className={ETIQUETA_CLS}>Estimado de consultas</legend>
        <div className="grid gap-2 sm:grid-cols-3">
          {ESTIMADOS.map((op) => (
            <label key={op.valor} className={OPCION_CLS}>
              <input
                type="radio"
                name="estimado"
                value={op.valor}
                checked={estimado === op.valor}
                onChange={() => setEstimado(op.valor)}
                disabled={deshabilitado}
                className={MARCA_CLS}
              />
              {op.nombre}
            </label>
          ))}
        </div>
      </fieldset>

      {estado.tipo === "error" && (
        <InlineAlert
          tone="error"
          title="No se guardó la configuración"
          action={
            estado.reintentable ? (
              <button type="button" onClick={() => void guardar()} className={BOTON_REINTENTAR_CLS}>
                Reintentar
              </button>
            ) : undefined
          }
        >
          {estado.mensaje}
        </InlineAlert>
      )}
      {estado.tipo === "guardado" && <InlineAlert tone="success">Configuración guardada.</InlineAlert>}

      <div className="flex justify-end">
        <button type="submit" disabled={deshabilitado} className={BOTON_PRIMARIO_CLS}>
          <Save className="h-4 w-4" aria-hidden="true" />
          {estado.tipo === "guardando" ? "Guardando…" : "Guardar operación"}
        </button>
      </div>
    </form>
  );
}
