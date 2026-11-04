"use client";

import { AlertTriangle } from "lucide-react";
import { skipToken, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Drop, type EstadoArchivo } from "@/components/tramite/Drop";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { Btn, Card, Field } from "@/components/tramite/piezas";
import { DeclaracionesTipo, type SubidaSoporte } from "@/components/tramite/renderers/DeclaracionesTipo";
import { TarjetaDestino } from "@/components/tramite/TarjetaDestino";
import { TotalLiquidado } from "@/components/tramite/TotalLiquidado";
import type { CampoDefinicion, Cotizacion, DeclaracionTipo, DefinicionTipo, DocumentoRequerido, GravamenRunt, ValoresActualesRunt } from "@/lib/api/cliente";
import { categoriaRuntDe, claveDeCampoDeclaracion, declaracionesActivas, declaracionesDe, destinoDelTipo, erroresDeDeclaraciones, faltaDestino, valoresDeDeclaraciones } from "@/lib/tramite/declaraciones";
import { alEscribirMoneda, formatearFecha, formatearPesos, numeroMoneda, textoMonedaCompleta } from "@/lib/formato";
import { claveAdicionalesActivos, claveCotizacion } from "@/lib/tramite/cotizacion";
import { ApiError, mensajeDe } from "@/lib/api/errores";
import {
  adjuntoDe,
  AYUDA_ARCHIVOS,
  documentosOrdenados,
  documentoVisible,
  erroresDeRequisitos,
  errorDeArchivo,
  esExigible,
  MENSAJE_OBLIGATORIO,
  MOTIVO_REQUISITOS,
  tarjetasDeRequisitos,
  TRANSFORMACIONES,
  type EntradaRequisitos,
} from "@/lib/tramite/requisitos";
import { GRUPO_POR_SECCION } from "@/lib/tramite/pasos";
import { nombreVisibleDeAdjunto } from "@/lib/tramite/presentacion";
import { accionGuardada, decisionDe, documentoDeDecision, DOCS_DE_PRENDA, errorDeDocumentoDePrenda, ETIQUETA_ACCION_PRENDA, type AccionPrenda } from "@/lib/tramite/prenda";

const AUTOGUARDADO_MS = 3000;

/** Solo en memoria, para «Reintentar» tras un fallo del servidor (`archivo`). */
type Subida = SubidaSoporte;

/** Códigos de los soportes que cubren las declaraciones: esos documentos se piden en su tarjeta y no se repiten en «Documentos». */
function soportesDeDeclaraciones(d: DefinicionTipo): Set<string> {
  const codigos = new Set<string>();
  for (const decl of declaracionesDe(d)) {
    if (decl.docCodeSoporte) codigos.add(decl.docCodeSoporte);
    for (const o of decl.opciones) for (const c of o.docCodes) codigos.add(c);
  }
  return codigos;
}

type EstadoBorrador = "inactivo" | "guardando" | "guardado" | "error";

/**
 * Renderer del paso Requisitos (HU F4 y K6) para `commercial`, `prenda_decision`, `document_checklist` y `generic_form`. Los cuatro
 * `section_type` caen en el mismo paso y se guardan juntos (el back valida datos comerciales y prenda en una sola escritura), así
 * que la primera sección del paso pinta todas las tarjetas que aplican al tipo y las demás no dibujan nada.
 */
export function FormularioRequisitos(props: PropsSeccion) {
  const { definicion } = useWizard();
  const { seccion, onEstado } = props;
  const primera = definicion.pasos.flatMap((p) => p.sectionTypes.map((s) => ({ clave: `${p.clave}:${s}`, s }))).find((x) => GRUPO_POR_SECCION[x.s] === "requisitos");
  const propietaria = primera?.clave === seccion.clave;
  useEffect(() => {
    if (!propietaria) onEstado({ completo: true });
  }, [propietaria, onEstado]);
  return propietaria ? <PasoRequisitos {...props} /> : null;
}

const sinClave = <T,>(mapa: Record<string, T>, clave: string): Record<string, T> => Object.fromEntries(Object.entries(mapa).filter(([k]) => k !== clave));

function PasoRequisitos({ seccion, intentoAvance, onEstado }: PropsSeccion) {
  const { cliente, definicion: definicionBase, solicitud, guardarConVersion, registrarGuardado, recargar, reportarError, canal, avaluoSugerido } = useWizard();
  const idSolicitud = solicitud.solicitudId;
  // Los documentos que ya cubre una declaración (su soporte) salen de la matriz de «Documentos» para no pedirse dos veces.
  const definicion = useMemo<DefinicionTipo>(() => {
    const cubiertos = soportesDeDeclaraciones(definicionBase);
    // Los documentos propios de la prenda (registro y levantamiento) se piden en la tarjeta de prenda, según la decisión.
    if (tarjetasDeRequisitos(definicionBase).prenda) for (const c of DOCS_DE_PRENDA) cubiertos.add(c);
    return cubiertos.size === 0 ? definicionBase : { ...definicionBase, documentos: definicionBase.documentos.filter((d) => !cubiertos.has(d.docCode)) };
  }, [definicionBase]);
  const t = useMemo(() => tarjetasDeRequisitos(definicion), [definicion]);
  const documentos = useMemo(() => (t.documentos ? documentosOrdenados(definicion) : []), [t.documentos, definicion]);
  const avaluo = avaluoSugerido != null && avaluoSugerido > 0 ? avaluoSugerido : null;

  const [campos, setCampos] = useState<Record<string, string>>(() => ({ ...solicitud.fieldValues }));
  // Con avalúo sugerido (canal del punto) y sin valor guardado, el valor de venta arranca en el avalúo: sigue siendo editable.
  const [valorVenta, setValorVenta] = useState(() => {
    const valor = solicitud.datos.comercial?.valor;
    if (valor != null && valor > 0) return textoMonedaCompleta(valor);
    return avaluo != null ? textoMonedaCompleta(avaluo) : "";
  });
  const valorPrecargado = !(solicitud.datos.comercial?.valor != null && solicitud.datos.comercial.valor > 0) && avaluo != null;
  const [tienePrenda, setTienePrenda] = useState(() => solicitud.datos.prenda?.tiene ?? false);
  const accionFija = (definicionBase.flags.accionPrenda === "levantar" ? "levantar" : definicionBase.flags.accionPrenda === "registrar" ? "registrar" : null) as AccionPrenda | null;
  const [accionPrenda, setAccionPrenda] = useState<AccionPrenda>(() => accionFija ?? accionGuardada(solicitud.datos.prenda?.decision));
  // `null` = la persona aún no escribió: se muestra el acreedor del RUNT si llegó (editable). Al escribir, manda lo escrito.
  // Un borrador guardado sin acreedor/NIT (vacío) cuenta como «no escrito»: así la precarga del RUNT también aplica al reabrirlo.
  const [acreedorEscrito, setAcreedorEscrito] = useState<string | null>(() => solicitud.datos.prenda?.acreedor?.trim() || null);
  const [nitEscrito, setNitEscrito] = useState<string | null>(() => solicitud.datos.prenda?.nitAcreedor?.trim() || null);
  const [entidadLevantamiento, setEntidadLevantamiento] = useState(() => solicitud.datos.prenda?.entidadLevantamiento ?? "");
  const verPrenda = t.prenda && (t.prendaObligatoria || tienePrenda);
  const gravamen = useQuery<GravamenRunt>({
    queryKey: ["pn", "gravamen", idSolicitud],
    queryFn: ({ signal }) => cliente.solicitudes.gravamen(idSolicitud, { signal }),
    enabled: verPrenda,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
  const acreedorRunt = gravamen.data?.acreedor?.trim() ?? "";
  const nitRunt = (gravamen.data?.nitAcreedor ?? "").replace(/\D/g, "");
  // Como en FLIT 2: al levantar una prenda que el RUNT confirmó con sus datos, el acreedor y el NIT son de solo lectura.
  const levantando = (accionFija ?? accionPrenda) === "levantar";
  const acreedorBloqueado = levantando && acreedorRunt !== "";
  const nitBloqueado = levantando && nitRunt !== "";
  const acreedor = acreedorBloqueado ? acreedorRunt : (acreedorEscrito ?? acreedorRunt);
  const nitAcreedor = nitBloqueado ? nitRunt : (nitEscrito ?? nitRunt);
  const runtReportaPrenda = Boolean(gravamen.data?.tienePrenda || gravamen.data?.tieneGravamenes);
  const fechaInscripcion = formatearFecha(gravamen.data?.fechaInscripcion);
  const [transformaciones, setTransformaciones] = useState<string[]>(() => solicitud.datos.requisitos?.transformaciones ?? []);
  const cotizacion = useQuery<Cotizacion>({
    queryKey: claveCotizacion(solicitud.organismoId, definicion.code, transformaciones),
    queryFn: skipToken,
    staleTime: Infinity,
  });
  const [observaciones, setObservaciones] = useState(() => solicitud.datos.requisitos?.observaciones ?? "");
  const [subidas, setSubidas] = useState<Record<string, Subida>>({});
  const [tocados, setTocados] = useState<Record<string, boolean>>({});
  const [erroresServidor, setErroresServidor] = useState<Record<string, string>>({});
  const [borrador, setBorrador] = useState<EstadoBorrador>("inactivo");
  const [errorBorrador, setErrorBorrador] = useState<string | null>(null);

  // Apagado: el portal es solo para personas naturales (no hay partes con NIT) y el servidor ya filtra los documentos de persona jurídica.
  const hayPersonaJuridica = false;
  const condicion = { tienePrenda: t.prendaObligatoria || tienePrenda, transformaciones, campos, hayPersonaJuridica };
  const documentosVisibles = documentos.filter((doc) => documentoVisible(doc, condicion));
  const decisionPrenda = decisionDe(condicion.tienePrenda, accionPrenda);
  const entrada: EntradaRequisitos = { campos, valorVenta, tienePrenda: condicion.tienePrenda, acreedor, nitAcreedor, ...(accionFija === "levantar" ? { entidadLevantamiento } : {}), observaciones, transformaciones, hayPersonaJuridica };
  const declaraciones = useMemo(() => (t.declaraciones ? declaracionesActivas(definicion, transformaciones) : []), [t.declaraciones, definicion, transformaciones]);
  // Lo que el RUNT tiene hoy (color, combustible, carrocería): el valor nuevo de una transformación debe ser distinto. Si no llega, no se bloquea.
  const pideValoresRunt = declaraciones.some((d) => categoriaRuntDe(d) !== null);
  const valoresRunt = useQuery<ValoresActualesRunt>({
    queryKey: ["pn", "valores-actuales", idSolicitud],
    queryFn: ({ signal }) => cliente.solicitudes.valoresActuales(idSolicitud, { signal }),
    enabled: pideValoresRunt,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
  // El punto no tiene paso de diagnóstico: la secretaría de destino (traslado de cuenta) se elige aquí. En el portal web va en el diagnóstico.
  const destinoDelPunto = canal === "punto" ? destinoDelTipo(definicionBase) : null;
  const errores = {
    ...erroresDeRequisitos(definicion, solicitud.adjuntos, entrada),
    ...(destinoDelPunto && faltaDestino(definicionBase, solicitud.fieldValues) ? { [`declaracion.${destinoDelPunto.clave}`]: MENSAJE_OBLIGATORIO } : {}),
    ...(t.prenda ? errorDeDocumentoDePrenda(definicionBase, solicitud.adjuntos, decisionPrenda) : {}),
    ...(t.declaraciones ? erroresDeDeclaraciones(definicion, solicitud.adjuntos, campos, transformaciones, valoresRunt.data) : {}),
  };
  const subiendo = Object.values(subidas).some((s) => s.estado === "cargando");
  const completo = Object.keys(errores).length === 0 && !subiendo;
  const motivo = completo ? undefined : subiendo ? "Espera a que termine la carga del archivo." : MOTIVO_REQUISITOS;
  useEffect(() => {
    onEstado({ completo, bloqueo: motivo });
  }, [completo, motivo, onEstado]);

  const mostrar = (llave: string): string | undefined => erroresServidor[llave] ?? (intentoAvance > 0 || tocados[llave] ? errores[llave] : undefined);
  const tocar = (llave: string) => setTocados((x) => (x[llave] ? x : { ...x, [llave]: true }));
  const editar = (llave: string) => setErroresServidor((e) => (e[llave] ? sinClave(e, llave) : e));

  // ── Guardado del borrador ───────────────────────────────────────────────────────────────────────────────────────────

  const firma = JSON.stringify([campos, valorVenta, tienePrenda, accionPrenda, acreedor, nitAcreedor, entidadLevantamiento, transformaciones, observaciones]);
  // Un valor precargado con el avalúo aún no está en el servidor: queda como cambio pendiente para que se guarde al avanzar.
  const guardada = useRef(valorPrecargado ? "" : firma);

  const guardar = useCallback(async () => {
    try {
      if (t.campos.length > 0 || t.declaraciones) {
        const valores: Record<string, string | null> = {};
        for (const paso of t.campos) for (const c of paso.campos) valores[c.fieldKey] = (campos[c.fieldKey] ?? "").trim() || null;
        if (t.declaraciones) Object.assign(valores, valoresDeDeclaraciones(definicion, campos, transformaciones, solicitud.fieldValues));
        if (Object.keys(valores).length > 0) await guardarConVersion((version) => cliente.solicitudes.guardarCampos(idSolicitud, { version, valores }));
      }
      if (t.comercial || t.prenda) {
        const tiene = t.prendaObligatoria || tienePrenda;
        await guardarConVersion((version) =>
          cliente.solicitudes.guardarComercial(idSolicitud, {
            version,
            ...(t.comercial ? { valorComercial: numeroMoneda(valorVenta) } : {}),
            ...(t.prenda
              ? { prenda: { tienePrenda: tiene, decision: decisionDe(tiene, accionPrenda), ...(tiene ? { acreedor: acreedor.trim(), nitAcreedor } : {}), ...(tiene && accionFija === "levantar" ? { entidadLevantamiento: entidadLevantamiento.trim() } : {}) } }
              : {}),
          }),
        );
      }
      if (t.observaciones || t.transformaciones) {
        await guardarConVersion((version) =>
          cliente.solicitudes.guardarRequisitos(idSolicitud, { version, ...(t.transformaciones ? { transformaciones } : {}), observaciones: observaciones.trim() }),
        );
      }
      guardada.current = firma;
      setErroresServidor({});
    } catch (error) {
      if (error instanceof ApiError && error.errores) setErroresServidor(erroresDeServidor(error.errores, definicion));
      throw error;
    }
  }, [t, definicion, solicitud.fieldValues, campos, valorVenta, tienePrenda, accionPrenda, acreedor, nitAcreedor, entidadLevantamiento, accionFija, transformaciones, observaciones, guardarConVersion, cliente, idSolicitud, firma]);

  // Al pasar al siguiente paso: guarda solo si algo cambió desde el último guardado.
  useEffect(() => {
    registrarGuardado(seccion.clave, async () => {
      if (guardada.current === firma) return;
      await guardar();
      setBorrador("guardado");
    });
    return () => registrarGuardado(seccion.clave, null);
  }, [registrarGuardado, seccion.clave, guardar, firma]);

  // Autoguardado: tras un rato sin escribir, solo si lo escrito ya es válido (el back rechaza un borrador a medias).
  const sinDocumentos = Object.keys(errores).every((k) => k.startsWith("documento."));
  const intentarGuardado = useCallback(async () => {
    setBorrador("guardando");
    setErrorBorrador(null);
    try {
      await guardar();
      setBorrador("guardado");
    } catch (error) {
      reportarError(error);
      setBorrador("error");
      setErrorBorrador(mensajeDe(error));
    }
  }, [guardar, reportarError]);
  useEffect(() => {
    if (guardada.current === firma || !sinDocumentos) return;
    const id = setTimeout(() => void intentarGuardado(), AUTOGUARDADO_MS);
    return () => clearTimeout(id);
  }, [firma, sinDocumentos, intentarGuardado]);

  // ── Documentos ──────────────────────────────────────────────────────────────────────────────────────────────────────

  const subir = async (doc: Pick<DocumentoRequerido, "docCode">, archivo: File) => {
    const invalido = errorDeArchivo(archivo);
    if (invalido) {
      setSubidas((s) => ({ ...s, [doc.docCode]: { estado: "error", nombre: archivo.name, error: invalido } }));
      return;
    }
    setSubidas((s) => ({ ...s, [doc.docCode]: { estado: "cargando", nombre: archivo.name } }));
    const previo = adjuntoDe(solicitud.adjuntos, doc.docCode);
    try {
      await cliente.adjuntos.subir(idSolicitud, doc.docCode, archivo);
      // Reemplazo: el archivo nuevo ya quedó guardado; el anterior se retira (si falla, el más reciente es el vigente).
      if (previo) await cliente.adjuntos.eliminar(idSolicitud, previo.id).catch(() => undefined);
      await recargar();
      setSubidas((s) => sinClave(s, doc.docCode));
    } catch (error) {
      reportarError(error);
      setSubidas((s) => ({ ...s, [doc.docCode]: { estado: "error", nombre: archivo.name, error: mensajeDe(error), archivo } }));
    }
  };

  const quitar = async (doc: Pick<DocumentoRequerido, "docCode">) => {
    const actual = adjuntoDe(solicitud.adjuntos, doc.docCode);
    if (!actual) return;
    setSubidas((s) => ({ ...s, [doc.docCode]: { estado: "cargando", nombre: actual.nombreArchivo } }));
    try {
      await cliente.adjuntos.eliminar(idSolicitud, actual.id);
      await recargar();
      setSubidas((s) => sinClave(s, doc.docCode));
    } catch (error) {
      reportarError(error);
      setSubidas((s) => ({ ...s, [doc.docCode]: { estado: "error", nombre: actual.nombreArchivo, error: mensajeDe(error) } }));
    }
  };

  const cambiarTransformacion = (codigo: string) => setTransformaciones((actual) => (actual.includes(codigo) ? actual.filter((c) => c !== codigo) : [...actual, codigo]));

  const queryClient = useQueryClient();
  useEffect(() => {
    queryClient.setQueryData(claveAdicionalesActivos, transformaciones);
  }, [queryClient, transformaciones]);

  /** Cargador de un documento de la matriz del tipo (Documentos y documento de la prenda): mismos estados, reintento y quitar. */
  const dropDe = (doc: DocumentoRequerido, exigible: boolean, faltante: string | undefined, ayudaExtra = "") => {
    const subida = subidas[doc.docCode];
    const cargado = adjuntoDe(solicitud.adjuntos, doc.docCode);
    const estado: EstadoArchivo = subida?.estado === "cargando" ? "cargando" : subida?.estado === "error" && !cargado ? "error" : cargado ? "ok" : "vacio";
    const error = subida?.estado === "error" ? subida.error : faltante;
    const ayuda = [doc.instruccionCargue.trim().replace(/[.]$/, ""), ayudaExtra, AYUDA_ARCHIVOS].filter(Boolean).join(". ");
    return (
      <div key={doc.docCode} className="space-y-2">
        <Drop
          title={`${doc.nombre}${exigible ? " *" : ""}`}
          hint={ayuda}
          optional={!exigible}
          estado={estado}
          nombre={subida?.nombre ?? (cargado ? nombreVisibleDeAdjunto(cargado, idSolicitud) : undefined)}
          error={error}
          onFile={(archivo) => void subir(doc, archivo)}
          onQuitar={cargado ? () => void quitar(doc) : undefined}
        />
        {subida?.estado === "error" && subida.archivo && (
          <Btn tone="secondary" onClick={() => void subir(doc, subida.archivo as File)}>
            Reintentar subida
          </Btn>
        )}
      </div>
    );
  };
  // El documento de la decisión de prenda (registro o levantamiento), si la matriz del tipo lo trae.
  const documentoPrenda = t.prenda ? documentoDeDecision(definicionBase, decisionPrenda) : undefined;

  // Las observaciones acompañan a cualquier paso de requisitos, así que siempre hay al menos una tarjeta que pintar.
  return (
    <>
      {destinoDelPunto && <TarjetaDestino intentoAvance={intentoAvance} />}

      {t.comercial && (
        <Card title="Datos comerciales" id="requisitos-comercial">
          <div className="grid gap-4 md:grid-cols-3">
            <Field label="Avalúo sugerido (Fasecolda)">
              <input className="field" disabled value={avaluo != null ? textoMonedaCompleta(avaluo) : ""} placeholder="—" />
            </Field>
            <Field label="Fuentes consultadas">
              <select className="field" disabled>
                <option>{avaluo != null ? "Fasecolda: Disponible" : "Fasecolda: No disponible"}</option>
              </select>
            </Field>
            <Field label="Valor de venta ($) *" error={mostrar("valorComercial")}>
              <input
                className="field"
                inputMode="numeric"
                autoComplete="off"
                aria-invalid={mostrar("valorComercial") ? true : undefined}
                value={valorVenta}
                onChange={(e) => {
                  const el = e.target;
                  const escrito = alEscribirMoneda(el.value, el.selectionStart ?? el.value.length);
                  setValorVenta(escrito.texto);
                  editar("valorComercial");
                  requestAnimationFrame(() => el.setSelectionRange(escrito.cursor, escrito.cursor));
                }}
                onBlur={() => {
                  tocar("valorComercial");
                  const n = numeroMoneda(valorVenta);
                  if (Number.isFinite(n) && n > 0) setValorVenta(textoMonedaCompleta(n));
                }}
              />
            </Field>
          </div>
        </Card>
      )}

      {t.prenda && (
        <Card title="Prenda / limitación a la propiedad" id="requisitos-prenda">
          {!t.prendaObligatoria && (
            <fieldset>
              <legend>¿Al vehículo se le asociará una prenda?</legend>
              <div className="mt-2 flex gap-6">
                {(
                  [
                    ["No", false],
                    ["Sí", true],
                  ] as const
                ).map(([etiqueta, valor]) => (
                  <label key={etiqueta} className="flex min-h-11 cursor-pointer items-center gap-2">
                    <input type="radio" name="prenda" className="h-5 w-5 accent-primary" checked={tienePrenda === valor} onChange={() => setTienePrenda(valor)} />
                    {etiqueta}
                  </label>
                ))}
              </div>
            </fieldset>
          )}
          {(t.prendaObligatoria || tienePrenda) && accionFija === null && (
            <fieldset className={t.prendaObligatoria ? undefined : "mt-4"}>
              <legend>¿Qué se hará con la prenda?</legend>
              <div className="mt-2 flex flex-wrap gap-6">
                {(Object.keys(ETIQUETA_ACCION_PRENDA) as AccionPrenda[]).map((accion) => (
                  <label key={accion} className="flex min-h-11 cursor-pointer items-center gap-2">
                    <input type="radio" name="prenda-accion" className="h-5 w-5 accent-primary" checked={accionPrenda === accion} onChange={() => setAccionPrenda(accion)} />
                    {ETIQUETA_ACCION_PRENDA[accion]}
                  </label>
                ))}
              </div>
            </fieldset>
          )}
          {verPrenda && (
            <div aria-live="polite" className="mt-3 space-y-1 text-sm font-light text-muted-foreground">
              {gravamen.isFetching && <p role="status">Consultando el gravamen en el RUNT...</p>}
              {gravamen.data?.aviso === "sin_gravamen" && <p role="status">RUNT sin gravamen registrado para este vehículo.</p>}
              {gravamen.isError && !gravamen.isFetching && (
                <p role="status">No pudimos consultar el gravamen en el RUNT. Escribe el acreedor y su NIT a mano.</p>
              )}
              {runtReportaPrenda && (
                <p role="status" className="font-normal text-foreground">
                  El RUNT reporta gravamen o prenda para este vehículo.
                  {!acreedorRunt && " No entregó el detalle del acreedor: escríbelo a mano."}
                </p>
              )}
            </div>
          )}
          {verPrenda && runtReportaPrenda && (
            <details open className="mt-3 rounded-[9px] border border-border p-3 text-sm">
              <summary className="cursor-pointer font-normal">Ver información de prenda / gravamen del RUNT</summary>
              <dl className="mt-2 grid gap-x-6 gap-y-1 sm:grid-cols-2">
                <div>
                  <dt className="text-xs font-light text-muted-foreground">TIENE GRAVÁMENES</dt>
                  <dd>{gravamen.data?.tieneGravamenes ? "SI" : "NO"}</dd>
                </div>
                <div>
                  <dt className="text-xs font-light text-muted-foreground">TIENE PRENDAS</dt>
                  <dd>{gravamen.data?.tienePrenda ? "SI" : "NO"}</dd>
                </div>
                {acreedorRunt && (
                  <div>
                    <dt className="text-xs font-light text-muted-foreground">ACREEDOR</dt>
                    <dd>{acreedorRunt}</dd>
                  </div>
                )}
                {nitRunt && (
                  <div>
                    <dt className="text-xs font-light text-muted-foreground">DOCUMENTO ACREEDOR</dt>
                    <dd>{nitRunt}</dd>
                  </div>
                )}
                {fechaInscripcion && (
                  <div>
                    <dt className="text-xs font-light text-muted-foreground">FECHA DE INSCRIPCIÓN</dt>
                    <dd>{fechaInscripcion}</dd>
                  </div>
                )}
              </dl>
            </details>
          )}
          {(t.prendaObligatoria || tienePrenda) && accionPrenda === "levantar" && (
            <p role="note" className="mt-3 text-sm font-light text-muted-foreground">
              Indica el acreedor de la prenda que se levantará y adjunta el documento del levantamiento.
            </p>
          )}
          {(t.prendaObligatoria || tienePrenda) && (
            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <Field label="Acreedor (beneficiario) *" error={mostrar("prenda.acreedor")}>
                <input
                  className={`field${acreedorBloqueado ? " opacity-70" : ""}`}
                  autoComplete="off"
                  readOnly={acreedorBloqueado}
                  aria-invalid={mostrar("prenda.acreedor") ? true : undefined}
                  value={acreedor}
                  onChange={(e) => {
                    setAcreedorEscrito(e.target.value);
                    editar("prenda.acreedor");
                  }}
                  onBlur={() => tocar("prenda.acreedor")}
                />
              </Field>
              <Field label="NIT / Documento del acreedor *" error={mostrar("prenda.nitAcreedor")}>
                <input
                  className={`field${nitBloqueado ? " opacity-70" : ""}`}
                  inputMode="numeric"
                  autoComplete="off"
                  readOnly={nitBloqueado}
                  aria-invalid={mostrar("prenda.nitAcreedor") ? true : undefined}
                  value={nitAcreedor}
                  onChange={(e) => {
                    setNitEscrito(e.target.value.replace(/\D/g, ""));
                    editar("prenda.nitAcreedor");
                  }}
                  onBlur={() => tocar("prenda.nitAcreedor")}
                />
              </Field>
              {accionFija === "levantar" && (
                <Field label="Entidad ante la que se levantó *" error={mostrar("prenda.entidadLevantamiento")}>
                  <input
                    className="field"
                    autoComplete="off"
                    maxLength={200}
                    placeholder="Ej. Notaría 15 de Medellín"
                    aria-invalid={mostrar("prenda.entidadLevantamiento") ? true : undefined}
                    value={entidadLevantamiento}
                    onChange={(e) => {
                      setEntidadLevantamiento(e.target.value);
                      editar("prenda.entidadLevantamiento");
                    }}
                    onBlur={() => tocar("prenda.entidadLevantamiento")}
                  />
                </Field>
              )}
            </div>
          )}
          {documentoPrenda && (
            <div className="mt-4 max-w-sm">
              {dropDe(documentoPrenda, documentoPrenda.obligatorio, intentoAvance > 0 ? errores[`documento.${documentoPrenda.docCode}`] : undefined)}
            </div>
          )}
        </Card>
      )}

      {t.campos.map((paso) => (
        <Card key={paso.clave} title={paso.titulo || undefined} id={`requisitos-campos-${paso.clave}`}>
          <div className="grid gap-4 md:grid-cols-3">
            {paso.campos.map((campo) => (
              <CampoDinamico
                key={campo.fieldKey}
                campo={campo}
                valor={campos[campo.fieldKey] ?? ""}
                error={mostrar(`campo.${campo.fieldKey}`)}
                onChange={(v) => {
                  setCampos((c) => ({ ...c, [campo.fieldKey]: v }));
                  editar(`campo.${campo.fieldKey}`);
                }}
                onBlur={() => tocar(`campo.${campo.fieldKey}`)}
              />
            ))}
          </div>
        </Card>
      ))}

      {declaraciones.length > 0 && (
        <DeclaracionesTipo
          cliente={cliente}
          definicion={definicionBase}
          declaraciones={declaraciones}
          campos={campos}
          valoresRunt={valoresRunt.data}
          solicitudId={idSolicitud}
          adjuntos={solicitud.adjuntos}
          subidas={subidas}
          intentoAvance={intentoAvance}
          error={mostrar}
          errorSoporte={(docCode) => (intentoAvance > 0 ? errores[`documento.${docCode}`] : undefined)}
          onCambiar={(decl: DeclaracionTipo, valor) => {
            setCampos((c) => ({ ...c, [decl.fieldKey]: valor }));
            editar(`declaracion.${decl.clave}`);
          }}
          onTocar={tocar}
          onSubir={(soporte, archivo) => void subir(soporte, archivo)}
          onQuitar={(soporte) => void quitar(soporte)}
        />
      )}

      {documentosVisibles.length > 0 && (
        <Card title="Documentos" id="requisitos-documentos">
          <div className="grid gap-4 md:grid-cols-3">
            {documentosVisibles.map((doc) =>
              dropDe(doc, esExigible(doc, condicion), intentoAvance > 0 ? errores[`documento.${doc.docCode}`] : undefined, doc.condicion.trim() ? `Aplica si: ${doc.condicion.trim().replace(/[.]$/, "")}` : ""),
            )}
          </div>
        </Card>
      )}

      {t.observaciones && (
        <Card title="Observaciones del trámite" id="requisitos-observaciones">
          <textarea
            className="field min-h-28 py-3"
            aria-label="Observaciones del trámite"
            aria-invalid={mostrar("observaciones") ? true : undefined}
            value={observaciones}
            placeholder="Observaciones para el organismo de tránsito"
            onChange={(e) => {
              setObservaciones(e.target.value);
              editar("observaciones");
            }}
            onBlur={() => tocar("observaciones")}
          />
          {mostrar("observaciones") && (
            <p role="alert" className="mt-1 flex items-center gap-1 text-xs text-alert">
              <AlertTriangle aria-hidden className="h-3.5 w-3.5" />
              {mostrar("observaciones")}
            </p>
          )}
        </Card>
      )}

      {t.transformaciones && (
        <Card title="Transformaciones del vehículo" id="requisitos-transformaciones">
          <p>Al activar un switch, el valor de la transformación se sumará automáticamente al valor total liquidado del trámite.</p>
          <div className="mt-3 space-y-3">
            {TRANSFORMACIONES.map(({ codigo, etiqueta }) => {
              const activa = transformaciones.includes(codigo);
              const precio = cotizacion.data?.lineas.find((l) => l.codigo === codigo)?.valorCop;
              return (
                <div key={codigo} className="flex items-center justify-between">
                  <span id={`transf-${codigo}`}>
                    {etiqueta}
                    {precio != null && (
                      <>
                        {" "}
                        <b className="text-primary">+{formatearPesos(precio)}</b>
                      </>
                    )}
                  </span>
                  <button
                    type="button"
                    role="switch"
                    aria-checked={activa}
                    aria-labelledby={`transf-${codigo}`}
                    onClick={() => cambiarTransformacion(codigo)}
                    className={`relative h-11 w-16 rounded-full p-0 transition-colors focus-visible:ring-2 focus-visible:ring-primary/40 ${activa ? "bg-success" : "bg-disabled"}`}
                  >
                    <span className={`absolute top-2.5 h-6 w-6 rounded-full bg-card transition-all ${activa ? "left-9" : "left-1"}`} />
                  </button>
                </div>
              );
            })}
          </div>
          {transformaciones.length === 0 && <p className="mt-3 text-xs font-light text-muted-foreground">Sin transformaciones declaradas: se registrará el dato del RUNT.</p>}
        </Card>
      )}

      {canal !== "punto" && <TotalLiquidado cliente={cliente} organismoId={solicitud.organismoId} tipoCodigo={definicion.code} transformaciones={t.transformaciones ? transformaciones : []} />}

      <div aria-live="polite" className="text-right text-xs">
        {borrador === "guardando" && <p role="status">Guardando borrador...</p>}
        {borrador === "guardado" && <p role="status" className="text-success">Borrador guardado</p>}
        {borrador === "error" && (
          <div role="alert" className="inline-flex flex-col items-end gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
            <p>No pudimos guardar el borrador. {errorBorrador} Lo que escribiste no se pierde.</p>
            <Btn tone="secondary" onClick={() => void intentarGuardado()}>
              Reintentar
            </Btn>
          </div>
        )}
      </div>
    </>
  );
}

/** Errores por campo de un 422 del back (`valores.<key>`, `valorComercial`, `prenda.acreedor`…) a las llaves de esta pantalla. */
function erroresDeServidor(errores: Readonly<Record<string, readonly string[]>>, definicion: DefinicionTipo): Record<string, string> {
  const salida: Record<string, string> = {};
  for (const [llave, mensajes] of Object.entries(errores)) {
    const mensaje = mensajes[0];
    if (!mensaje) continue;
    if (!llave.startsWith("valores.")) {
      salida[llave] = mensaje;
      continue;
    }
    // Un campo que pertenece a una declaración se marca en la declaración; los demás, en su campo.
    const campo = llave.slice("valores.".length);
    salida[claveDeCampoDeclaracion(definicion, campo) ?? `campo.${campo}`] = mensaje;
  }
  return salida;
}

function CampoDinamico({ campo, valor, error, onChange, onBlur }: { campo: CampoDefinicion; valor: string; error: string | undefined; onChange: (v: string) => void; onBlur: () => void }): ReactNode {
  const etiqueta = `${campo.etiqueta}${campo.obligatorio ? " *" : ""}`;
  const comun = { className: "field", "aria-invalid": error ? true : undefined, onBlur } as const;
  switch (campo.tipo.toLowerCase()) {
    case "select":
      return (
        <Field label={etiqueta} error={error}>
          <select {...comun} value={valor} onChange={(e) => onChange(e.target.value)}>
            <option value="">Selecciona</option>
            {campo.opciones.map((o) => (
              <option key={o} value={o}>
                {o}
              </option>
            ))}
          </select>
        </Field>
      );
    case "textarea":
      return (
        <div className="md:col-span-3">
          <Field label={etiqueta} error={error}>
            <textarea {...comun} className="field min-h-28 py-3" value={valor} onChange={(e) => onChange(e.target.value)} />
          </Field>
        </div>
      );
    case "checkbox":
      return (
        <label className="flex min-h-11 cursor-pointer items-center gap-2 self-end">
          <input type="checkbox" className="h-5 w-5 accent-primary" checked={valor === "true"} aria-invalid={error ? true : undefined} onChange={(e) => onChange(e.target.checked ? "true" : "false")} />
          {etiqueta}
        </label>
      );
    case "date":
      return (
        <Field label={etiqueta} error={error}>
          <input {...comun} type="date" value={valor} onChange={(e) => onChange(e.target.value)} />
        </Field>
      );
    case "number":
      return (
        <Field label={etiqueta} error={error}>
          <input {...comun} inputMode="decimal" autoComplete="off" value={valor} onChange={(e) => onChange(e.target.value.replace(/[^\d.-]/g, ""))} />
        </Field>
      );
    default:
      return (
        <Field label={etiqueta} error={error}>
          <input {...comun} autoComplete="off" value={valor} onChange={(e) => onChange(e.target.value)} />
        </Field>
      );
  }
}
