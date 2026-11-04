"use client";

// HU #14232 — Paso Diagnóstico del canal OT: mismo cascarón y cards del wizard del portal PN, sin solicitud.
import { useEffect, useRef, useState } from "react";
import { AlertTriangle, Car, Check } from "lucide-react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { boton } from "@/components/landing/estilos";
import { ActoresPunto } from "@/components/punto/ActoresPunto";
import { EnvioPunto } from "@/components/punto/EnvioPunto";
import { RequisitosPunto } from "@/components/punto/RequisitosPunto";
import { Badge, Card, Nav, Row } from "@/components/tramite/piezas";
import type { ClientePortal, Diagnostico, ResultadoVehiculoPunto, VehiculoDelTramite } from "@/lib/api/cliente";
import { mensajeDeFallo } from "@/lib/api/errores";
import { ASSETS } from "@/lib/assets";
import {
  datosTecnicos,
  filasDiagnostico,
  MENSAJE_ATENCION,
  MENSAJE_NO_PUEDE_CONTINUAR,
  MENSAJE_PUEDE_CONTINUAR,
  MENSAJE_REVISANDO_VEHICULO,
} from "@/lib/tramite/diagnostico";

/** Mismos pasos base del portal (Cascaron / PASOS_BASE), sin Cotizar. */
const PASOS = [
  { id: "diagnostico", titulo: "Diagnóstico", miga: "Diagnóstico" },
  { id: "actores", titulo: "Actores", miga: "Actores" },
  { id: "requisitos", titulo: "Requisitos", miga: "Requisitos" },
  { id: "resumen", titulo: "Resumen", miga: "Resumen" },
  { id: "envio", titulo: "Envío", miga: "Confirmación" },
] as const;

type FasePaso = "diagnostico" | "actores" | "requisitos" | "resumen" | "envio";

type EstadoCarga =
  | { fase: "cargando" }
  | { fase: "listo"; diagnostico: Diagnostico; vehiculo: ResultadoVehiculoPunto | null }
  | { fase: "error"; mensaje: string };

function aVehiculoDelTramite(v: ResultadoVehiculoPunto | null): VehiculoDelTramite | null {
  if (!v) return null;
  return {
    existe: v.existe,
    placa: v.placa ?? null,
    clase: v.clase ?? null,
    marca: v.marca ?? null,
    linea: v.linea ?? null,
    modeloAno: v.modeloAno ?? null,
    color: v.color ?? null,
    estado: v.estado ?? null,
    tipoServicio: null,
    tienePrenda: false,
    tieneLimitacion: false,
    tieneRestricciones: false,
  };
}

export function DiagnosticoPunto({
  cliente,
  placa,
  documento,
  tipoCodigo,
  tipoNombre: nombreDelTipo,
  organismoNombre,
  onVolver,
  onTerminar,
  onAviso,
}: {
  cliente: ClientePortal;
  placa: string;
  documento: string;
  tipoCodigo: string;
  /** Nombre del tipo tal como lo ofrece el catálogo del punto; sin él se muestra el código. */
  tipoNombre?: string | undefined;
  organismoNombre: string;
  onVolver: () => void;
  /** Al cerrar la confirmación: el punto queda listo para la siguiente persona (sin datos del trámite anterior). */
  onTerminar?: () => void;
  onAviso: (titulo: string, mensaje: string, tono?: "alerta" | "exito" | "info") => void;
}) {
  const [paso, setPaso] = useState<FasePaso>("diagnostico");
  const [solicitudIdPunto, setSolicitudIdPunto] = useState<string | null>(null);
  const [estado, setEstado] = useState<EstadoCarga>({ fase: "cargando" });
  const intento = useRef(0);
  const tituloRef = useRef<HTMLHeadingElement | null>(null);
  const tipoNombre = nombreDelTipo?.trim() || tipoCodigo;
  const indicePaso =
    paso === "diagnostico" ? 0 : paso === "actores" ? 1 : paso === "requisitos" ? 2 : paso === "resumen" ? 3 : 4;

  async function cargar() {
    const id = ++intento.current;
    setEstado({ fase: "cargando" });
    try {
      const [diagnostico, vehiculo] = await Promise.all([
        cliente.punto.diagnostico({ placa, documento, tipoCodigo }),
        cliente.punto.consultas
          .vehiculo({ placa, documento, consentimiento: true })
          .catch(() => null),
      ]);
      if (id !== intento.current) return;
      setEstado({ fase: "listo", diagnostico, vehiculo });
    } catch (err) {
      if (id !== intento.current) return;
      const mensaje = mensajeDeFallo(err);
      setEstado({ fase: "error", mensaje });
      onAviso("No pudimos continuar", mensaje);
    }
  }

  useEffect(() => {
    // Diferido un turno (como RequisitosPunto): el estado ya arranca en «cargando» y así el efecto no escribe estado en su cuerpo.
    const id = window.setTimeout(() => void cargar(), 0);
    return () => window.clearTimeout(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- recarga explícita con Reintentar
  }, [placa, documento, tipoCodigo]);

  useEffect(() => {
    tituloRef.current?.focus({ preventScroll: true });
  }, [paso]);

  const diagnostico = estado.fase === "listo" ? estado.diagnostico : null;
  const vehiculo = estado.fase === "listo" ? estado.vehiculo : null;
  const filas = diagnostico ? filasDiagnostico(diagnostico.requisitos) : [];
  const avisos = diagnostico
    ? diagnostico.requisitos.filter((r) => r.estado !== "cumple" && r.estado !== "no_aplica")
    : [];
  const puede = diagnostico?.puedeContinuar === true;
  const conBloqueos = diagnostico !== null && !diagnostico.puedeContinuar;
  const tecnicos = datosTecnicos(aVehiculoDelTramite(vehiculo), { placa, vin: null }, documento);
  const miga = PASOS[indicePaso].miga;
  const cargando = estado.fase === "cargando";

  return (
    <div className="min-h-screen bg-background font-sans text-foreground" aria-label="Wizard del punto">
      <header className="wizard-header flex flex-wrap items-center justify-between gap-3 border-b border-border bg-background py-3">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src={ASSETS.logo} alt="FLIT 2.0" className="h-9 w-auto" />
        <div className="flex flex-wrap items-center justify-end gap-4 text-sm md:gap-6">
          <p className="max-w-md truncate font-medium text-foreground/80" title={organismoNombre}>
            Punto de atención · {organismoNombre}
          </p>
          <span className="hidden sm:inline">
            Placa <b>{placa}</b>
          </span>
          <span className="hidden md:inline">{tipoNombre}</span>
        </div>
      </header>

      <main className="wizard-main w-full py-8">
        <nav aria-label="Ruta de navegación">
          <p className="text-sm font-medium text-muted-foreground">
            Punto de atención &gt; {tipoNombre} &gt;{" "}
            <span aria-current="page" className="border-b-2 border-primary text-primary">
              {miga}
            </span>
          </p>
        </nav>
        <h1 className="mt-2 text-[32px] font-bold">Trámite de {tipoNombre}</h1>

        <div className="wizard-cols mt-6">
          <aside className="wizard-sidebar" aria-label="Resumen y navegación del trámite">
            <div className="flex flex-col items-center gap-2 text-center">
              <div className="flex items-center justify-center gap-3">
                <span aria-hidden className="grid h-12 w-12 shrink-0 place-items-center rounded-full bg-primary/10 text-primary">
                  <Car className="h-6 w-6" />
                </span>
                <p className="font-bold text-primary">Mi trámite</p>
              </div>
              <p className="rounded-full bg-primary/10 px-3 py-0.5 text-sm font-semibold text-primary">{placa}</p>
              <p className="text-xs font-light text-muted-foreground">Canal del punto · sin radicado aún</p>
            </div>
            <div className="space-y-3 text-sm">
              <p className="text-center font-bold text-primary">Configuración del trámite</p>
              <div>
                <span className="mb-1 block text-xs font-light text-muted-foreground">Organismo de Tránsito</span>
                <input className="field" readOnly value={organismoNombre} aria-label="Organismo de Tránsito" />
              </div>
              <div>
                <span className="mb-1 block text-xs font-light text-muted-foreground">Tipo de Trámite</span>
                <input className="field" readOnly value={tipoNombre} aria-label="Tipo de Trámite" />
              </div>
            </div>
            <hr className="border-border" />
            <ol className="wizard-step-nav space-y-1" aria-label="Pasos del trámite">
              {PASOS.map((p, i) => {
                const hecho = i < indicePaso;
                const activo = i === indicePaso;
                return (
                  <li key={p.id}>
                    <button
                      type="button"
                      disabled
                      aria-current={activo ? "step" : undefined}
                      className={boton(
                        "ghost",
                        "flex h-auto w-full cursor-default items-center justify-start gap-3 px-2 py-1 opacity-100",
                        "none",
                      )}
                    >
                      <span
                        className={`grid h-9 w-9 shrink-0 place-items-center rounded-full border-2 text-sm font-semibold ${
                          hecho
                            ? "border-success bg-success text-primary-foreground"
                            : activo
                              ? "border-primary text-primary"
                              : "border-disabled text-disabled"
                        }`}
                      >
                        {hecho ? <Check aria-hidden className="h-4 w-4" /> : i + 1}
                      </span>
                      <span className={`text-sm font-medium ${hecho ? "text-success" : activo ? "text-primary" : "text-disabled"}`}>
                        {hecho && <span className="sr-only">Completado: </span>}Paso {i + 1} · {p.titulo}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ol>
            <p role="status" aria-live="polite" className="sr-only">
              Paso {indicePaso + 1} de {PASOS.length}: {PASOS[indicePaso].titulo}
            </p>
          </aside>

          <div className="min-w-0 space-y-6">
            <h2 ref={tituloRef} tabIndex={-1} className="sr-only">
              Paso {indicePaso + 1} · {PASOS[indicePaso].titulo}
            </h2>

            {/* Actores permanece montado al ir a Requisitos/Resumen/Envío para no reabrir identidad del vendedor ya validado. */}
            {(paso === "actores" || paso === "requisitos" || paso === "resumen" || paso === "envio") && (
              <div
                className={paso === "actores" ? undefined : "hidden"}
                aria-hidden={paso !== "actores"}
                id="punto-paso-actores-wrap"
              >
                <ActoresPunto
                  cliente={cliente}
                  placa={placa}
                  documento={documento}
                  tipoCodigo={tipoCodigo}
                  onAtras={() => setPaso("diagnostico")}
                  onContinuarRequisitos={() => setPaso("requisitos")}
                  onSolicitudLista={setSolicitudIdPunto}
                  onAviso={onAviso}
                />
              </div>
            )}
            {paso === "requisitos" && solicitudIdPunto ? (
              <RequisitosPunto
                cliente={cliente}
                solicitudId={solicitudIdPunto}
                tipoCodigo={tipoCodigo}
                placa={placa}
                onAtras={() => setPaso("actores")}
                onContinuarPago={() => setPaso("resumen")}
                onAviso={onAviso}
              />
            ) : paso === "requisitos" ? (
              <Card title="Requisitos" id="punto-requisitos-sin-borrador">
                <p className="text-sm" role="status">
                  Primero completa Actores para abrir el borrador y cargar documentos.
                </p>
                <Nav onBack={() => setPaso("actores")} onNext={() => setPaso("actores")} nextLabel="Ir a Actores" />
              </Card>
            ) : (paso === "resumen" || paso === "envio") && solicitudIdPunto ? (
              <EnvioPunto
                cliente={cliente}
                solicitudId={solicitudIdPunto}
                tipoCodigo={tipoCodigo}
                paso={paso}
                onPaso={setPaso}
                onAtras={() => setPaso("requisitos")}
                onIrActores={() => setPaso("actores")}
                onTerminar={() => {
                  setSolicitudIdPunto(null);
                  setPaso("diagnostico");
                  (onTerminar ?? onVolver)();
                }}
              />
            ) : paso === "diagnostico" ? (
              <>
                <Card title="Diagnóstico de requisitos previos" id="punto-diagnostico-requisitos">
                  <div className="relative min-h-[12rem]" aria-busy={cargando}>
                    {diagnostico && (
                      <>
                        <h2
                          id="punto-diagnostico-resultado"
                          className={`mb-3 text-xl font-bold ${puede ? "text-success" : "text-alert"}`}
                        >
                          {puede ? MENSAJE_PUEDE_CONTINUAR : MENSAJE_NO_PUEDE_CONTINUAR}
                        </h2>

                        {filas.length > 0 && (
                          <ul
                            className="wizard-diagnosis mt-3 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6"
                            aria-label="Indicadores del vehículo"
                          >
                            {filas.map((f) => (
                              <Row key={f.clave} k={f.nombre} v={<Badge tone={f.tono}>{f.estado}</Badge>} />
                            ))}
                          </ul>
                        )}

                        {avisos.length > 0 && (
                          <ul className="mt-3 list-disc space-y-1 pl-5 text-sm" aria-label="Motivos del diagnóstico">
                            {avisos.map((a) => (
                              <li key={a.codigo}>{a.descripcion}</li>
                            ))}
                          </ul>
                        )}

                        <div
                          role={conBloqueos ? "alert" : undefined}
                          className="mt-4 flex gap-3 rounded-[9px] bg-warning/10 p-4"
                        >
                          <AlertTriangle aria-hidden className="h-6 w-6 shrink-0 text-warning" />
                          <p>
                            <b>Atención:</b> {MENSAJE_ATENCION}
                          </p>
                        </div>
                      </>
                    )}

                    {estado.fase === "error" && (
                      <div
                        role="alert"
                        className="flex flex-col items-start gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert"
                      >
                        <p className="flex gap-2">
                          <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
                          <span>{estado.mensaje}</span>
                        </p>
                        <button type="button" className={boton("back", "min-h-11 px-6", "none")} onClick={() => void cargar()}>
                          Reintentar
                        </button>
                      </div>
                    )}

                    {cargando && !diagnostico && <MobilityLoader mensaje={MENSAJE_REVISANDO_VEHICULO} />}
                    {cargando && diagnostico && (
                      <div className="absolute inset-0 z-10 grid place-items-center rounded-[9px] bg-background/80 backdrop-blur-sm">
                        <MobilityLoader mensaje={MENSAJE_REVISANDO_VEHICULO} />
                      </div>
                    )}
                  </div>
                </Card>

                <Card title="Datos técnicos del vehículo" id="punto-datos-tecnicos">
                  <dl className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
                    {tecnicos.length === 0 ? (
                      <div>
                        <dt className="text-xs font-light text-muted-foreground">Placa</dt>
                        <dd className="break-words">{placa}</dd>
                      </div>
                    ) : (
                      tecnicos.map(([k, v]) => (
                        <div key={k}>
                          <dt className="text-xs font-light text-muted-foreground">{k}</dt>
                          <dd className="break-words">{v}</dd>
                        </div>
                      ))
                    )}
                  </dl>
                </Card>
              </>
            ) : null}

            {paso === "diagnostico" && (
              <Nav
                onBack={onVolver}
                onNext={() => setPaso("actores")}
                nextLabel="Continuar"
                nextDisabled={!puede || cargando || estado.fase === "error"}
                motivo={
                  conBloqueos
                    ? "Resuelve los pendientes del diagnóstico para continuar."
                    : estado.fase === "error"
                      ? estado.mensaje
                      : cargando
                        ? "Revisando el vehículo…"
                        : undefined
                }
              />
            )}
          </div>
        </div>
      </main>
    </div>
  );
}
