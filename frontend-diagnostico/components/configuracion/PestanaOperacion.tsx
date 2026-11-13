"use client";

import { useState, type FormEvent } from "react";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { ApiError } from "@/lib/api";
import {
  ESTIMADOS,
  VISIBILIDADES,
  contratoCambioDespuesDeOperacion,
  guardarOperacion,
  nombreFuente,
  ordenarFuentes,
  soloDisponibles,
  type Configuracion,
  type EstimadoConsultas,
  type Fuente,
  type VisibilidadUsuario,
} from "@/lib/configuracion";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, ETIQUETA_CLS, MARCA_CLS, OPCION_CLS, TARJETA_CLS } from "./estilos";

type Estado =
  | { tipo: "inactivo" }
  | { tipo: "guardando" }
  | { tipo: "guardado" }
  | { tipo: "conflicto" }
  | { tipo: "error"; mensaje: string; reintentable: boolean };

/** RUNT vehículo es la fuente base de todo diagnóstico (AC42): siempre marcada y bloqueada en las fuentes por defecto. */
const FUENTE_FIJA: Fuente = "RUNT";

function conFuenteFija(fuentes: readonly Fuente[], habilitadas: readonly Fuente[]): Fuente[] {
  return habilitadas.includes(FUENTE_FIJA) && !fuentes.includes(FUENTE_FIJA) ? ordenarFuentes([...fuentes, FUENTE_FIJA]) : [...fuentes];
}

function mismasFuentes(a: readonly Fuente[], b: readonly Fuente[]): boolean {
  return a.length === b.length && a.every((f) => b.includes(f));
}

/**
 * Nivel operativo de la compañía del token: visibilidad, fuentes por defecto (solo entre las habilitadas del contrato
 * que el backend declara en `fuentes_disponibles`, HU #13865) y estimado de consultas. Una por defecto ya guardada que
 * dejó de estar disponible no se ofrece, pero tampoco se borra en silencio: viaja igual en el PUT. Sin diagnostico.configuracion.editar todo queda deshabilitado. Un rechazo de la API (400, 403,
 * 422) se muestra sin perder lo digitado; un error del servidor (hoy también el conflicto de concurrencia, que responde
 * 500) ofrece Reintentar. Un 409 avisa que el contrato cambió y ofrece recargar (`onRecargar`) sin perder la vista.
 * Descartar vuelve a lo último guardado (HU #14562 · AC42).
 */
export function PestanaOperacion({
  configuracion,
  puedeEditar,
  onGuardado,
  onRecargar,
}: {
  configuracion: Configuracion;
  puedeEditar: boolean;
  onGuardado?: (configuracion: Configuracion) => void;
  onRecargar?: () => void;
}) {
  const habilitadas = ordenarFuentes(configuracion.contrato.fuentes_habilitadas);
  const ofrecidas = soloDisponibles(habilitadas, configuracion.fuentes_disponibles);
  const [base, setBase] = useState(configuracion.operacion);
  const [visibilidad, setVisibilidad] = useState<VisibilidadUsuario>(base.visibilidad_usuario);
  const [porDefecto, setPorDefecto] = useState<Fuente[]>(() => conFuenteFija(base.fuentes_por_defecto, habilitadas));
  const [estimado, setEstimado] = useState<EstimadoConsultas>(base.estimado_consultas);
  const [estado, setEstado] = useState<Estado>({ tipo: "inactivo" });
  const deshabilitado = !puedeEditar || estado.tipo === "guardando";
  const hayCambios =
    visibilidad !== base.visibilidad_usuario ||
    estimado !== base.estimado_consultas ||
    !mismasFuentes(porDefecto, conFuenteFija(base.fuentes_por_defecto, habilitadas));

  function descartar() {
    setVisibilidad(base.visibilidad_usuario);
    setPorDefecto(conFuenteFija(base.fuentes_por_defecto, habilitadas));
    setEstimado(base.estimado_consultas);
    setEstado({ tipo: "inactivo" });
  }

  function alternar(fuente: Fuente) {
    if (fuente === FUENTE_FIJA) return;
    setPorDefecto((actual) => (actual.includes(fuente) ? actual.filter((f) => f !== fuente) : ordenarFuentes([...actual, fuente])));
  }

  async function guardar() {
    if (!puedeEditar) return;
    setEstado({ tipo: "guardando" });
    try {
      const guardada = await guardarOperacion({
        visibilidad_usuario: visibilidad,
        fuentes_por_defecto: conFuenteFija(porDefecto, habilitadas).filter((f) => habilitadas.includes(f)),
        estimado_consultas: estimado,
      });
      setBase(guardada.operacion);
      setVisibilidad(guardada.operacion.visibilidad_usuario);
      setPorDefecto(conFuenteFija(guardada.operacion.fuentes_por_defecto, habilitadas));
      setEstimado(guardada.operacion.estimado_consultas);
      setEstado({ tipo: "guardado" });
      onGuardado?.(guardada);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setEstado({ tipo: "conflicto" });
        return;
      }
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
        <h2 id="operacion-titulo" className="text-lg font-semibold text-flit-primary">
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
        {ofrecidas.length === 0 ? (
          <p className={AYUDA_CLS}>
            {habilitadas.length === 0 ? "El contrato no tiene fuentes habilitadas." : "Ninguna fuente habilitada está disponible todavía."}
          </p>
        ) : (
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {ofrecidas.map((fuente) => (
              <label key={fuente} className={OPCION_CLS}>
                <input
                  type="checkbox"
                  name="fuentes"
                  value={fuente}
                  checked={fuente === FUENTE_FIJA || porDefecto.includes(fuente)}
                  onChange={() => alternar(fuente)}
                  disabled={deshabilitado || fuente === FUENTE_FIJA}
                  aria-label={nombreFuente(fuente)}
                  aria-describedby={fuente === FUENTE_FIJA ? "operacion-fuente-fija" : undefined}
                  className={MARCA_CLS}
                />
                {nombreFuente(fuente)}
                {fuente === FUENTE_FIJA && (
                  <span id="operacion-fuente-fija" className="ml-auto inline-flex items-center gap-1 text-xs text-muted-foreground">
                    <Icono nombre="candado" className="h-3.5 w-3.5" aria-hidden="true" />
                    Siempre incluida
                  </span>
                )}
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
      {estado.tipo === "conflicto" && (
        <InlineAlert
          tone="error"
          title="El contrato cambió"
          action={
            onRecargar ? (
              <button type="button" onClick={onRecargar} className={BOTON_REINTENTAR_CLS}>
                Recargar
              </button>
            ) : undefined
          }
        >
          El backoffice FLIT actualizó la configuración mientras editabas. Recarga para ver la versión vigente; tus cambios siguen en pantalla.
        </InlineAlert>
      )}
      {estado.tipo === "guardado" && <InlineAlert tone="success">Configuración guardada.</InlineAlert>}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
        <p className={AYUDA_CLS}>Los cambios aplican a los diagnósticos nuevos</p>
        <div className="flex gap-2">
          <button type="button" onClick={descartar} disabled={deshabilitado || !hayCambios} className={BOTON_SECUNDARIO_CLS}>
            Descartar
          </button>
          <button type="submit" disabled={deshabilitado} className={BOTON_PRIMARIO_CLS}>
          <Icono nombre="nube-subir" className="h-4 w-4" />
          {estado.tipo === "guardando" ? "Guardando…" : "Guardar operación"}
          </button>
        </div>
      </div>
    </form>
  );
}
