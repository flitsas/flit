"use client";

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { Btn, Card } from "@/components/tramite/piezas";
import { PersonaActor } from "@/components/tramite/renderers/PersonaActor";
import type { EstadoIdentidad } from "@/lib/api/cliente";
import { ApiError, mensajeDe } from "@/lib/api/errores";
import {
  actoresParaGuardar,
  enCurso,
  erroresDeServidor,
  estadoDeFila,
  filasIniciales,
  filaVacia,
  motivoIncompleto,
  ordinalLibre,
  parteDe,
  puedeAgregar,
  puedeQuitar,
  type ErroresActor,
  type FilaActor,
} from "@/lib/tramite/actores";

export const claveIdentidad = (solicitudId: string) => ["pn", "identidad", solicitudId] as const;

const SONDEO_INICIAL_MS = 8000;
const SONDEO_LENTO_MS = 15000;
const SONDEOS_RAPIDOS = 6;

/**
 * Renderer de `actor_form` (HU F3 y K5): una tarjeta por rol de la definición con sus personas (mínimo y máximo, agregar y quitar),
 * la consulta de cada documento, el contacto, la dirección y, en los roles que exigen validar la identidad, el envío y el
 * reenvío del enlace con su estado en vivo. «Continuar» queda bloqueado hasta que todo lo obligatorio esté completo y aprobado.
 * Lo que el ciudadano escribe vive solo en el estado de este componente y viaja únicamente al BFF.
 */
export function FormularioActores({ seccion, activa, intentoAvance, onEstado }: PropsSeccion) {
  const { cliente, definicion, solicitud, contexto, guardarConVersion, registrarGuardado, reportarError } = useWizard();
  const [filas, setFilas] = useState<FilaActor[]>(() => filasIniciales(definicion, solicitud.actores));
  const [erroresServidor, setErroresServidor] = useState<Record<string, ErroresActor>>({});
  const idSolicitud = solicitud.solicitudId;
  const hayIdentidad = definicion.actores.some((r) => r.biometria);

  const identidad = useQuery<EstadoIdentidad>({
    queryKey: claveIdentidad(idSolicitud),
    enabled: hayIdentidad && activa,
    queryFn: ({ signal }) => cliente.identidad.estado(idSolicitud, { signal }),
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    // Sondeo prudente: solo mientras el paso está a la vista y alguna parte espera; se frena al aprobarse todo o al salir.
    refetchInterval: (consulta) => {
      const datos = consulta.state.data;
      if (!activa || !datos || !datos.partes.some((p) => enCurso(p.estado))) return false;
      return consulta.state.dataUpdateCount < SONDEOS_RAPIDOS ? SONDEO_INICIAL_MS : SONDEO_LENTO_MS;
    },
  });
  const refetchIdentidad = identidad.refetch;
  const refrescarIdentidad = useCallback(() => void refetchIdentidad(), [refetchIdentidad]);
  useEffect(() => {
    if (identidad.error) reportarError(identidad.error);
  }, [identidad.error, reportarError]);

  const estadoDe = useCallback((f: FilaActor) => estadoDeFila(f, identidad.data, solicitud.actores), [identidad.data, solicitud.actores]);
  const motivo = useMemo(() => motivoIncompleto(definicion, filas, estadoDe), [definicion, filas, estadoDe]);
  useEffect(() => {
    onEstado({ completo: motivo === undefined, bloqueo: motivo });
  }, [motivo, onEstado]);

  // ── Guardado ────────────────────────────────────────────────────────────────────────────────────────────────────────

  /** Guarda las personas de los roles indicados (todos si no se indica). Un rol incluido se reemplaza entero en el back. */
  const guardar = useCallback(
    async (roles?: readonly string[]) => {
      const seleccion = filas.filter((f) => f.consultado && (!roles || roles.includes(f.rol)));
      if (seleccion.length === 0) return;
      try {
        await guardarConVersion((version) => cliente.solicitudes.guardarActores(idSolicitud, { version, actores: actoresParaGuardar(seleccion) }));
        setErroresServidor({});
      } catch (error) {
        if (error instanceof ApiError && error.errores) setErroresServidor(erroresDeServidor(error.errores, seleccion.map((f) => f.clave)));
        throw error;
      }
    },
    [filas, cliente, idSolicitud, guardarConVersion],
  );

  useEffect(() => {
    registrarGuardado(seccion.clave, () => guardar());
    return () => registrarGuardado(seccion.clave, null);
  }, [registrarGuardado, seccion.clave, guardar]);

  // ── Edición ─────────────────────────────────────────────────────────────────────────────────────────────────────────

  const cambiar = (clave: string, patch: Partial<FilaActor>) => {
    setFilas((actuales) => actuales.map((f) => (f.clave === clave ? { ...f, ...patch } : f)));
    setErroresServidor((e) => (e[clave] ? Object.fromEntries(Object.entries(e).filter(([k]) => k !== clave)) : e));
  };
  const agregar = (rolCodigo: string) => {
    const rol = definicion.actores.find((r) => r.rol === rolCodigo);
    if (!rol) return;
    setFilas((actuales) => (puedeAgregar(actuales, rol) ? [...actuales, filaVacia(rolCodigo, ordinalLibre(actuales, rol))] : actuales));
  };
  const quitar = (clave: string) => {
    setFilas((actuales) => {
      const fila = actuales.find((f) => f.clave === clave);
      const rol = fila && definicion.actores.find((r) => r.rol === fila.rol);
      return fila && rol && puedeQuitar(actuales, rol) ? actuales.filter((f) => f.clave !== clave) : actuales;
    });
  };

  if (definicion.actores.length === 0) {
    return (
      <Card title={seccion.paso?.titulo || undefined}>
        <p role="status" className="text-sm text-muted-foreground">
          Este trámite no pide registrar personas en este paso.
        </p>
      </Card>
    );
  }

  const cargando = hayIdentidad && activa && identidad.isPending;
  const errorEstado = identidad.isError ? identidad.error : null;

  return (
    <>
      {cargando && (
        <div role="status" className="flex items-center gap-3 text-sm text-muted-foreground">
          <MobilityLoader />
          <span>Consultando el estado de las validaciones...</span>
        </div>
      )}
      {errorEstado !== null && (
        <div role="alert" className="flex flex-col items-start gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
          <p className="flex gap-2">
            <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
            <span>No pudimos consultar el estado de las validaciones de identidad. {mensajeDe(errorEstado)}</span>
          </p>
          <Btn tone="secondary" onClick={refrescarIdentidad}>
            Reintentar
          </Btn>
        </div>
      )}
      <p role="status" aria-live="polite" className="sr-only">
        {hayIdentidad && identidad.data ? (identidad.data.completa ? "Todas las identidades están aprobadas." : "Hay validaciones de identidad pendientes.") : ""}
      </p>

      {definicion.actores.map((rol, indiceRol) => {
        const delRol = filas.filter((f) => f.rol === rol.rol);
        const varias = rol.max > 1;
        // Con título la persona se puede quitar (rol con varias personas o con mínimo 0, que admite quedar vacío).
        const conTitulo = varias || rol.min === 0;
        const id = `actor-rol-${indiceRol}`;
        return (
          <Card key={rol.rol} title={rol.etiqueta} id={id}>
            {rol.max > 1 && (
              <p className="mb-4 text-xs font-light text-muted-foreground">
                {rol.min === rol.max ? `Se requieren ${rol.max} personas.` : `Mínimo ${rol.min}, máximo ${rol.max}.`}
              </p>
            )}
            {delRol.length === 0 && (
              <p role="status" className="text-sm text-muted-foreground">
                Este rol es opcional: agrega una persona solo si aplica.
              </p>
            )}
            <div className="space-y-6">
              {delRol.map((fila, i) => (
                <PersonaActor
                  key={fila.clave}
                  fila={fila}
                  rol={rol}
                  titulo={conTitulo ? (varias ? `${rol.etiqueta} ${i + 1}` : rol.etiqueta) : undefined}
                  solicitudId={idSolicitud}
                  cliente={cliente}
                  cedulaSolicitante={contexto?.cedula}
                  mostrarErrores={intentoAvance > 0}
                  erroresServidor={erroresServidor[fila.clave]}
                  estado={estadoDe(fila)}
                  parte={parteDe(fila, identidad.data)}
                  versionEstado={identidad.dataUpdatedAt}
                  onCambio={(patch) => cambiar(fila.clave, patch)}
                  onQuitar={conTitulo && puedeQuitar(filas, rol) ? () => quitar(fila.clave) : undefined}
                  guardarRol={() => guardar([rol.rol])}
                  refrescarIdentidad={refrescarIdentidad}
                  reportarError={reportarError}
                />
              ))}
            </div>
            {puedeAgregar(filas, rol) && (
              <div className="mt-4">
                <Btn tone="secondary" onClick={() => agregar(rol.rol)}>
                  Agregar {rol.etiqueta}
                </Btn>
              </div>
            )}
          </Card>
        );
      })}
    </>
  );
}

/**
 * Renderer de `biometric`: la validación de identidad vive dentro de la tarjeta de cada actor, así que si la definición ya trae
 * `actor_form` esta sección no dibuja nada. Si solo trae `biometric`, se pinta el mismo paso para no dejar el trámite sin él.
 */
export function ValidacionIdentidad(props: PropsSeccion) {
  const { definicion } = useWizard();
  const conFormulario = definicion.pasos.some((p) => p.sectionTypes.includes("actor_form"));
  const { onEstado } = props;
  useEffect(() => {
    if (conFormulario) onEstado({ completo: true });
  }, [conFormulario, onEstado]);
  return conFormulario ? null : <FormularioActores {...props} />;
}
