"use client";

import { AlertTriangle } from "lucide-react";
import { skipToken, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Drop, type EstadoArchivo } from "@/components/tramite/Drop";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { Btn, Card, Field } from "@/components/tramite/piezas";
import { TotalLiquidado } from "@/components/tramite/TotalLiquidado";
import type { CampoDefinicion, Cotizacion, DocumentoRequerido } from "@/lib/api/cliente";
import { alEscribirMoneda, formatearPesos, numeroMoneda, textoMonedaCompleta } from "@/lib/formato";
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
  MOTIVO_REQUISITOS,
  tarjetasDeRequisitos,
  TRANSFORMACIONES,
  type EntradaRequisitos,
} from "@/lib/tramite/requisitos";
import { GRUPO_POR_SECCION } from "@/lib/tramite/pasos";

const AUTOGUARDADO_MS = 3000;

interface Subida {
  estado: "cargando" | "error";
  nombre: string;
  error?: string;
  /** Solo en memoria, para «Reintentar» tras un fallo del servidor. */
  archivo?: File;
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
  const { cliente, definicion, solicitud, guardarConVersion, registrarGuardado, recargar, reportarError } = useWizard();
  const idSolicitud = solicitud.solicitudId;
  const t = useMemo(() => tarjetasDeRequisitos(definicion), [definicion]);
  const documentos = useMemo(() => (t.documentos ? documentosOrdenados(definicion) : []), [t.documentos, definicion]);

  const [campos, setCampos] = useState<Record<string, string>>(() => ({ ...solicitud.fieldValues }));
  const [valorVenta, setValorVenta] = useState(() => {
    const valor = solicitud.datos.comercial?.valor;
    return valor != null && valor > 0 ? textoMonedaCompleta(valor) : "";
  });
  const [tienePrenda, setTienePrenda] = useState(() => solicitud.datos.prenda?.tiene ?? false);
  const [acreedor, setAcreedor] = useState(() => solicitud.datos.prenda?.acreedor ?? "");
  const [nitAcreedor, setNitAcreedor] = useState(() => solicitud.datos.prenda?.nitAcreedor ?? "");
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

  const hayPersonaJuridica = solicitud.actores.some((a) => a.tipoDocumento === "NIT");
  const condicion = { tienePrenda: t.prendaObligatoria || tienePrenda, transformaciones, campos, hayPersonaJuridica };
  const documentosVisibles = documentos.filter((doc) => documentoVisible(doc, condicion));
  const entrada: EntradaRequisitos = { campos, valorVenta, tienePrenda: condicion.tienePrenda, acreedor, nitAcreedor, observaciones, transformaciones, hayPersonaJuridica };
  const errores = erroresDeRequisitos(definicion, solicitud.adjuntos, entrada);
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

  const firma = JSON.stringify([campos, valorVenta, tienePrenda, acreedor, nitAcreedor, transformaciones, observaciones]);
  const guardada = useRef(firma);

  const guardar = useCallback(async () => {
    try {
      if (t.campos.length > 0) {
        const valores: Record<string, string | null> = {};
        for (const paso of t.campos) for (const c of paso.campos) valores[c.fieldKey] = (campos[c.fieldKey] ?? "").trim() || null;
        await guardarConVersion((version) => cliente.solicitudes.guardarCampos(idSolicitud, { version, valores }));
      }
      if (t.comercial || t.prenda) {
        const tiene = t.prendaObligatoria || tienePrenda;
        await guardarConVersion((version) =>
          cliente.solicitudes.guardarComercial(idSolicitud, {
            version,
            ...(t.comercial ? { valorComercial: numeroMoneda(valorVenta) } : {}),
            ...(t.prenda ? { prenda: { tienePrenda: tiene, ...(tiene ? { acreedor: acreedor.trim(), nitAcreedor } : {}) } } : {}),
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
      if (error instanceof ApiError && error.errores) setErroresServidor(erroresDeServidor(error.errores));
      throw error;
    }
  }, [t, campos, valorVenta, tienePrenda, acreedor, nitAcreedor, transformaciones, observaciones, guardarConVersion, cliente, idSolicitud, firma]);

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

  const subir = async (doc: DocumentoRequerido, archivo: File) => {
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

  const quitar = async (doc: DocumentoRequerido) => {
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

  // Las observaciones acompañan a cualquier paso de requisitos, así que siempre hay al menos una tarjeta que pintar.
  return (
    <>
      {t.comercial && (
        <Card title="Datos comerciales" id="requisitos-comercial">
          <div className="grid gap-4 md:grid-cols-3">
            <Field label="Avalúo sugerido (Fasecolda)">
              <input className="field" disabled value="" placeholder="—" />
            </Field>
            <Field label="Fuentes consultadas">
              <select className="field" disabled>
                <option>Fasecolda: No disponible</option>
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
          {(t.prendaObligatoria || tienePrenda) && (
            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <Field label="Acreedor (beneficiario) *" error={mostrar("prenda.acreedor")}>
                <input
                  className="field"
                  autoComplete="off"
                  aria-invalid={mostrar("prenda.acreedor") ? true : undefined}
                  value={acreedor}
                  onChange={(e) => {
                    setAcreedor(e.target.value);
                    editar("prenda.acreedor");
                  }}
                  onBlur={() => tocar("prenda.acreedor")}
                />
              </Field>
              <Field label="NIT / Documento del acreedor *" error={mostrar("prenda.nitAcreedor")}>
                <input
                  className="field"
                  inputMode="numeric"
                  autoComplete="off"
                  aria-invalid={mostrar("prenda.nitAcreedor") ? true : undefined}
                  value={nitAcreedor}
                  onChange={(e) => {
                    setNitAcreedor(e.target.value.replace(/\D/g, ""));
                    editar("prenda.nitAcreedor");
                  }}
                  onBlur={() => tocar("prenda.nitAcreedor")}
                />
              </Field>
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

      {documentosVisibles.length > 0 && (
        <Card title="Documentos" id="requisitos-documentos">
          <div className="grid gap-4 md:grid-cols-3">
            {documentosVisibles.map((doc) => {
              const subida = subidas[doc.docCode];
              const cargado = adjuntoDe(solicitud.adjuntos, doc.docCode);
              const estado: EstadoArchivo = subida?.estado === "cargando" ? "cargando" : subida?.estado === "error" && !cargado ? "error" : cargado ? "ok" : "vacio";
              const exigible = esExigible(doc, condicion);
              const faltante = intentoAvance > 0 ? errores[`documento.${doc.docCode}`] : undefined;
              const error = subida?.estado === "error" ? subida.error : faltante;
              const ayuda = [doc.instruccionCargue.trim().replace(/\.$/, ""), doc.condicion.trim() ? `Aplica si: ${doc.condicion.trim().replace(/\.$/, "")}` : "", AYUDA_ARCHIVOS].filter(Boolean).join(". ");
              return (
                <div key={doc.docCode} className="space-y-2">
                  <Drop
                    title={`${doc.nombre}${exigible ? " *" : ""}`}
                    hint={ayuda}
                    optional={!exigible}
                    estado={estado}
                    nombre={subida?.nombre ?? cargado?.nombreArchivo}
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
            })}
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

      <TotalLiquidado cliente={cliente} organismoId={solicitud.organismoId} tipoCodigo={definicion.code} transformaciones={t.transformaciones ? transformaciones : []} />

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
function erroresDeServidor(errores: Readonly<Record<string, readonly string[]>>): Record<string, string> {
  const salida: Record<string, string> = {};
  for (const [llave, mensajes] of Object.entries(errores)) {
    const mensaje = mensajes[0];
    if (!mensaje) continue;
    salida[llave.startsWith("valores.") ? `campo.${llave.slice("valores.".length)}` : llave] = mensaje;
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
