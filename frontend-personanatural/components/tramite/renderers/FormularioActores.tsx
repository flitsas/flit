"use client";

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { Btn, Card } from "@/components/tramite/piezas";
import { AvisoModoPruebas } from "@/components/tramite/AvisoModoPruebas";
import { useOmitirIdentidad } from "@/components/landing/useConfiguracion";
import { AlertaIdentidadValidada, nombrePropio } from "@/components/tramite/AlertaIdentidadValidada";
import { PersonaActor } from "@/components/tramite/renderers/PersonaActor";
import type { EstadoIdentidad } from "@/lib/api/cliente";
import { ApiError, mensajeDe } from "@/lib/api/errores";
import {
  actoresDeLaSeccion,
  actoresParaGuardar,
  actoresYaGuardados,
  enCurso,
  erroresDeServidor,
  estadoDeFila,
  filasIniciales,
  rolDeUnaPersona,
  validarFila,
  sembrarDocumentoInicial,
  filaVacia,
  motivoIncompleto,
  ordinalLibre,
  parteDe,
  puedeAgregar,
  puedeQuitar,
  type ErroresActor,
  type FilaActor,
} from "@/lib/tramite/actores";
import { recordarActor } from "@/lib/tramite/presentacion";
import { marcarEnvioFallido } from "@/lib/tramite/envioIdentidad";

export const claveIdentidad = (solicitudId: string) => ["pn", "identidad", solicitudId] as const;

const SONDEO_INICIAL_MS = 8000;
const SONDEO_LENTO_MS = 15000;
const SONDEOS_RAPIDOS = 6;

/**
 * Renderer de `actor_form` (HU F3 y K5): una tarjeta por rol de la definición con sus personas (mínimo y máximo, agregar y quitar),
 * la consulta de cada documento, el contacto, la dirección y, en los roles que exigen validar la identidad, el envío y el
 * reenvío del enlace con su estado en vivo. «Continuar» exige los datos de cada persona; la identidad aprobada no lo traba
 * mientras `EXIGIR_IDENTIDAD_PARA_CONTINUAR` esté apagado.
 * Lo que el ciudadano escribe vive solo en el estado de este componente y viaja únicamente al BFF.
 */
export function FormularioActores({ seccion, activa, intentoAvance, onEstado }: PropsSeccion) {
  const { cliente, definicion, solicitud, contexto, guardarConVersion, registrarGuardado, reportarError } = useWizard();
  const omitirIdentidad = useOmitirIdentidad(cliente);
  const definicionSeccion = useMemo(
    () => ({ ...definicion, actores: actoresDeLaSeccion(definicion, seccion.paso?.clave) }),
    [definicion, seccion.paso?.clave],
  );
  const incluyeAlTitular = definicionSeccion.actores.some((r) => r.rol === definicion.actores[0]?.rol);
  // Documento del primer paso: el del propietario consultado o, al reanudar, la cédula con la que se abrió/reanudó el trámite.
  const propietarioDeContexto = contexto?.propietario;
  const cedulaDeContexto = contexto?.cedula;
  const documentoInicial = propietarioDeContexto ?? (cedulaDeContexto ? { tipo: "CC", numero: cedulaDeContexto } : undefined);
  const [filas, setFilas] = useState<FilaActor[]>(() => {
    const iniciales = filasIniciales(definicionSeccion, solicitud.actores);
    return incluyeAlTitular ? sembrarDocumentoInicial(iniciales, documentoInicial) : iniciales;
  });
  const documentoSembrado = useRef(Boolean(documentoInicial));
  useEffect(() => {
    if (documentoSembrado.current || !documentoInicial) return;
    documentoSembrado.current = true;
    setFilas((actuales) => sembrarDocumentoInicial(actuales, documentoInicial));
  }, [documentoInicial?.tipo, documentoInicial?.numero]); // eslint-disable-line react-hooks/exhaustive-deps
  const [erroresServidor, setErroresServidor] = useState<Record<string, ErroresActor>>({});
  const idSolicitud = solicitud.solicitudId;
  useEffect(() => {
    for (const fila of filas) recordarActor(idSolicitud, fila.rol, fila.ordinal, fila.nombre);
  }, [filas, idSolicitud]);
  const hayIdentidad = definicionSeccion.actores.some((r) => r.biometria);

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
  const motivo = useMemo(() => motivoIncompleto(definicionSeccion, filas, estadoDe), [definicionSeccion, filas, estadoDe]);
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
        // El reenvío de la identidad ya guardó a estas personas. Un 429 al repetir el mismo guardado no puede dejar el paso trabado.
        if (error instanceof ApiError && error.status === 429 && actoresYaGuardados(seleccion, solicitud.actores)) return;
        if (error instanceof ApiError && error.errores) setErroresServidor(erroresDeServidor(error.errores, seleccion.map((f) => f.clave)));
        throw error;
      }
    },
    [filas, cliente, idSolicitud, guardarConVersion, solicitud.actores],
  );

  // ── Envío automático de la validación de identidad (al pasar al siguiente paso) ─────────────────────────────────────

  /** Motivo corto por persona cuando su validación no se pudo enviar sola. */
  const [fallosEnvio, setFallosEnvio] = useState<Record<string, string>>({});
  const yaEnviadas = useRef(new Set<string>());
  const marcarFallo = useCallback(
    (clave: string, motivoCorto: string | null) => {
      setFallosEnvio((actual) => {
        if (motivoCorto === null) return clave in actual ? Object.fromEntries(Object.entries(actual).filter(([k]) => k !== clave)) : actual;
        return actual[clave] === motivoCorto ? actual : { ...actual, [clave]: motivoCorto };
      });
      marcarEnvioFallido(idSolicitud, clave, motivoCorto !== null);
    },
    [idSolicitud],
  );

  /**
   * Solicita la validación de cada persona que la exige y aún no la tiene enviada, en paralelo. Nunca lanza: lo que falle queda
   * como botón de alerta en su tarjeta y no impide avanzar.
   */
  const enviarPendientes = useCallback(async () => {
    // Sin el estado real de las validaciones no se sabe a quién ya se le envió: no se arriesga un envío doble.
    if (hayIdentidad && !identidad.data) return;
    const candidatas = filas.filter((f) => {
      const rol = definicionSeccion.actores.find((r) => r.rol === f.rol);
      return rol?.biometria && f.consultado && estadoDe(f) === "sin_solicitar" && !yaEnviadas.current.has(f.clave);
    });
    if (candidatas.length === 0) return;
    await Promise.all(
      candidatas.map(async (f) => {
        const rol = definicionSeccion.actores.find((r) => r.rol === f.rol);
        if (!rol) return;
        if (Object.keys(validarFila(f, rolDeUnaPersona(rol))).length > 0) return marcarFallo(f.clave, "revisa los datos de esta persona.");
        if (!f.runt?.inscrito) return marcarFallo(f.clave, f.runt ? "no está inscrita en el RUNT." : "consulta primero el documento en el RUNT.");
        try {
          const resultado = await cliente.identidad.solicitar(idSolicitud, f.rol, f.ordinal);
          if (resultado.estado === "pendiente_envio") return marcarFallo(f.clave, "el enlace no salió todavía; reintenta el envío.");
          yaEnviadas.current.add(f.clave);
          marcarFallo(f.clave, null);
        } catch (error) {
          reportarError(error);
          const contacto = error instanceof ApiError && error.code === "contacto_invalido";
          marcarFallo(f.clave, contacto ? "revisa el correo y el celular." : mensajeDe(error));
        }
      }),
    );
    refrescarIdentidad();
  }, [filas, hayIdentidad, identidad.data, definicionSeccion.actores, estadoDe, cliente, idSolicitud, reportarError, marcarFallo, refrescarIdentidad]);

  useEffect(() => {
    registrarGuardado(seccion.clave, async () => {
      await guardar();
      await enviarPendientes();
    });
    return () => registrarGuardado(seccion.clave, null);
  }, [registrarGuardado, seccion.clave, guardar, enviarPendientes]);

  // Cuando la validación ya salió (por el envío automático o el reenvío manual) deja de estar pendiente de envío.
  useEffect(() => {
    for (const f of filas) {
      const e = estadoDe(f);
      if (e !== "sin_solicitar" && e !== "pendiente_envio") marcarEnvioFallido(idSolicitud, f.clave, false);
    }
  }, [filas, estadoDe, idSolicitud]);

  // ── Edición ─────────────────────────────────────────────────────────────────────────────────────────────────────────

  const cambiar = (clave: string, patch: Partial<FilaActor>) => {
    setFilas((actuales) => actuales.map((f) => (f.clave === clave ? { ...f, ...patch } : f)));
    setErroresServidor((e) => (e[clave] ? Object.fromEntries(Object.entries(e).filter(([k]) => k !== clave)) : e));
  };
  const agregar = (rolCodigo: string) => {
    const definido = definicion.actores.find((r) => r.rol === rolCodigo);
    if (!definido) return;
    const rol = rolDeUnaPersona(definido);
    setFilas((actuales) => (puedeAgregar(actuales, rol) ? [...actuales, filaVacia(rolCodigo, ordinalLibre(actuales, rol))] : actuales));
  };
  const quitar = (clave: string) => {
    setFilas((actuales) => {
      const fila = actuales.find((f) => f.clave === clave);
      const definido = fila && definicion.actores.find((r) => r.rol === fila.rol);
      const rol = definido && rolDeUnaPersona(definido);
      return fila && rol && puedeQuitar(actuales, rol) ? actuales.filter((f) => f.clave !== clave) : actuales;
    });
  };

  if (definicionSeccion.actores.length === 0) {
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
  // Alerta de «identidad validada»: solo con aprobación real de TODAS las personas que validan identidad en la solicitud.
  const filasConIdentidad = filas.filter((f) => definicionSeccion.actores.find((r) => r.rol === f.rol)?.biometria);
  const totalPersonas = Math.max(identidad.data?.partes.length ?? 0, filasConIdentidad.length);
  const todasValidaron =
    hayIdentidad &&
    Boolean(identidad.data?.completa) &&
    filasConIdentidad.length > 0 &&
    filasConIdentidad.every((f) => f.consultado && estadoDe(f) === "aprobada");
  const unaSolaPersona = totalPersonas === 1;
  const nombreUnica = unaSolaPersona ? nombrePropio(filasConIdentidad[0]?.nombre) : "";

  return (
    <>
      {omitirIdentidad && hayIdentidad && <AvisoModoPruebas id={`actores-modo-pruebas-${seccion.clave}`} />}
      {cargando && (
        <MobilityLoader mensaje="Consultando el estado de las validaciones..." compacto />
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
        {hayIdentidad && identidad.data && !identidad.data.completa ? "Hay validaciones de identidad pendientes." : ""}
      </p>
      {todasValidaron && (
        <AlertaIdentidadValidada id={`actores-identidad-validada-${seccion.clave}`}>
          {unaSolaPersona
            ? `${nombreUnica ? `${nombreUnica}, tu` : "Tu"} identidad quedó validada. Ya puedes continuar con tu trámite.`
            : "Todas las personas del trámite validaron su identidad. Ya puedes continuar con tu trámite."}
        </AlertaIdentidadValidada>
      )}

      {definicionSeccion.actores.map((definido, indiceRol) => {
        const rol = rolDeUnaPersona(definido);
        const delRol = filas.filter((f) => f.rol === rol.rol);
        const varias = rol.max > 1;
        // Con título la persona se puede quitar (rol con varias personas o con mínimo 0, que admite quedar vacío).
        const conTitulo = varias || rol.min === 0;
        const id = `actor-${seccion.clave}-${indiceRol}`;
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
                  esPropietario={definicion.actores[0]?.rol === rol.rol && fila.ordinal === 1}
                  documentoFijo={definicion.actores[0]?.rol === rol.rol && fila.ordinal === 1 && Boolean(documentoInicial) && fila.numeroDocumento === documentoInicial?.numero.trim()}
                  solicitudId={idSolicitud}
                  cliente={cliente}
                  mostrarErrores={intentoAvance > 0}
                  erroresServidor={erroresServidor[fila.clave]}
                  estado={estadoDe(fila)}
                  parte={parteDe(fila, identidad.data)}
                  versionEstado={identidad.dataUpdatedAt}
                  envioAutomatico
                  confirmarIdentidad={totalPersonas > 1}
                  consultaActiva={activa}
                  fallaEnvio={fallosEnvio[fila.clave]}
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
