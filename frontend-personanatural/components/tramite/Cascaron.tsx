"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Bell, Car, Check, Info, LogOut, TriangleAlert } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { memo, useCallback, useEffect, useId, useMemo, useRef, useState, type TouchEvent } from "react";
import { boton } from "@/components/landing/estilos";
import { SelectorOrganismo } from "@/components/landing/SelectorOrganismo";
import { useOrganismos } from "@/components/landing/useCatalogos";
import { ModalFlit } from "@/components/modales/ModalFlit";
import { ProveedorWizard, type ApiWizard, type EstadoSeccion, type RegistroRenderers } from "@/components/tramite/ContextoWizard";
import { Nav } from "@/components/tramite/piezas";
import { claveSolicitud } from "@/components/tramite/useSolicitud";
import { MENSAJE_SIN_TARIFA, ValorTramite } from "@/components/tramite/ValorTramite";
import type { ClientePortal, DefinicionTipo, Organismo, Solicitud } from "@/lib/api/cliente";
import { ApiError, mensajeDe } from "@/lib/api/errores";
import { organismoElegido } from "@/lib/tramite/diagnostico";
import { ASSETS } from "@/lib/assets";
import type { ContextoTramite } from "@/lib/tramite/contexto";
import { esEditable, pasoInicial, type GrupoPaso, type PasoWizard, type SeccionWizard } from "@/lib/tramite/pasos";

interface Props {
  cliente: ClientePortal;
  definicion: DefinicionTipo;
  solicitud: Solicitud;
  pasos: PasoWizard[];
  registro: RegistroRenderers;
  contexto: ContextoTramite | null;
  /** La sesión venció en medio del trámite: quien monta el wizard vuelve a pedir radicado y cédula. */
  onSesionVencida: () => void;
}

const DURACION_SLIDE_MS = 350;
const UMBRAL_DESLIZAR_PX = 60;

const reducirMovimiento = (): boolean => typeof window !== "undefined" && typeof window.matchMedia === "function" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/**
 * Cascarón genérico del wizard (HU F1): encabezado, migas, barra lateral con el navegador de pasos y un carrusel horizontal con
 * los pasos. No conoce campos, actores ni documentos: cada sección la dibuja su renderer del registro y le informa si está
 * completa. Mismas clases del diseño de referencia (transfer-step-trail, tramite-traspaso.tsx).
 */
export function Cascaron({ cliente, definicion, solicitud, pasos, registro, contexto, onSesionVencida }: Props) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const idMotivo = useId();
  const editable = esEditable(solicitud.estado);
  const ultimo = pasos.length - 1;

  const [paso, setPaso] = useState(() => pasoInicial(pasos, definicion, solicitud, editable));
  const [alcanzado, setAlcanzado] = useState(paso);
  const [saliendo, setSaliendo] = useState<number | null>(null);
  const [estados, setEstados] = useState<Record<string, EstadoSeccion>>({});
  const [intento, setIntento] = useState(0);
  const [sinTarifa, setSinTarifa] = useState(false);
  const [guardando, setGuardando] = useState(false);
  const [errorGuardado, setErrorGuardado] = useState<string | null>(null);
  const [conflicto, setConflicto] = useState(false);
  const [vencida, setVencida] = useState(false);
  const [saliendoDelTramite, setSaliendoDelTramite] = useState(false);

  const slides = useRef<(HTMLDivElement | null)[]>([]);
  const titulos = useRef<(HTMLHeadingElement | null)[]>([]);
  const guardadores = useRef(new Map<string, () => Promise<void>>());
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);
  const toqueInicial = useRef<number | null>(null);
  const primerRender = useRef(true);

  useEffect(
    () => () => {
      if (temporizador.current) clearTimeout(temporizador.current);
    },
    [],
  );

  // ── API que ven las secciones ─────────────────────────────────────────────────────────────────────────────────────

  const idSolicitud = solicitud.solicitudId;
  const reportarError = useCallback((error: unknown) => {
    if (error instanceof ApiError && error.code.startsWith("session_")) setVencida(true);
  }, []);
  // `irA` se vuelve a crear en cada render; el contexto guarda una función estable que llama a la más reciente.
  const irAlPaso = useRef<(destino: number) => void>(() => undefined);
  const irAPaso = useCallback(
    (grupo: GrupoPaso) => {
      const destino = pasos.findIndex((p) => p.id === grupo);
      if (destino >= 0) irAlPaso.current(destino);
    },
    [pasos],
  );

  const api = useMemo<ApiWizard>(
    () => ({
      cliente,
      definicion,
      solicitud,
      contexto,
      recargar: () => queryClient.invalidateQueries({ queryKey: claveSolicitud(idSolicitud) }),
      guardarConVersion: async (escribir) => {
        const vigente = queryClient.getQueryData<Solicitud>(claveSolicitud(idSolicitud)) ?? solicitud;
        queryClient.setQueryData(claveSolicitud(idSolicitud), await escribir(vigente.version));
      },
      registrarGuardado: (clave, guardar) => {
        if (guardar) guardadores.current.set(clave, guardar);
        else guardadores.current.delete(clave);
      },
      reportarError,
      irAPaso,
    }),
    [cliente, definicion, solicitud, contexto, queryClient, idSolicitud, reportarError, irAPaso],
  );

  // ── Validez y navegación ──────────────────────────────────────────────────────────────────────────────────────────

  const pasoValido = (i: number): boolean => pasos[i].secciones.every((s) => estados[s.clave]?.completo === true);
  const bloqueoDe = (i: number): string | undefined => {
    if (sinTarifa) return MENSAJE_SIN_TARIFA;
    if (pasoValido(i)) return undefined;
    return pasos[i].secciones.map((s) => estados[s.clave]).find((e) => e && !e.completo && e.bloqueo)?.bloqueo;
  };

  const irA = (destino: number) => {
    if (destino === paso) return;
    setSaliendo(paso);
    setPaso(destino);
    setAlcanzado((a) => Math.max(a, destino));
    setIntento(0);
    setErrorGuardado(null);
    if (typeof window.scrollTo === "function") window.scrollTo({ top: 0, behavior: reducirMovimiento() ? "auto" : "smooth" });
    if (temporizador.current) clearTimeout(temporizador.current);
    if (reducirMovimiento()) setSaliendo(null);
    else temporizador.current = setTimeout(() => setSaliendo(null), DURACION_SLIDE_MS);
  };

  useEffect(() => {
    irAlPaso.current = (destino) => {
      if (editable) irA(destino);
    };
  });

  // Tras cambiar de paso el foco pasa al título del paso (lectores de pantalla y teclado).
  useEffect(() => {
    if (primerRender.current) {
      primerRender.current = false;
      return;
    }
    titulos.current[paso]?.focus({ preventScroll: true });
  }, [paso]);

  // Un intento de avanzar con el paso incompleto: el foco va al primer campo inválido que marcó la sección.
  useEffect(() => {
    if (intento === 0) return;
    slides.current[paso]?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [intento, paso]);

  const guardarPaso = async (): Promise<boolean> => {
    const claves = new Set(pasos[paso].secciones.map((s) => s.clave));
    setGuardando(true);
    setErrorGuardado(null);
    try {
      // Copia fija: al guardar, la sección se vuelve a registrar y, si se recorre el mapa en vivo, la clave
      // reinsertada se visita otra vez. Con dos formularios de actor eso repite el guardado hasta el límite.
      const pendientes = [...guardadores.current.entries()].filter(([clave]) => claves.has(clave));
      for (const [, guardar] of pendientes) await guardar();
      return true;
    } catch (error) {
      if (error instanceof ApiError && error.status === 409 && error.code === "conflicto_version") setConflicto(true);
      else if (error instanceof ApiError && error.code.startsWith("session_")) setVencida(true);
      else setErrorGuardado(mensajeDe(error));
      return false;
    } finally {
      setGuardando(false);
    }
  };

  /** Avanza un paso (o hasta `destino` ya alcanzado) si el actual está completo; si no, marca lo que falta. */
  const avanzar = async (destino = paso + 1) => {
    if (!editable || guardando || destino > ultimo || destino <= paso) return;
    // Al saltar varios pasos (desde el navegador de pasos) no se puede pasar por encima de un paso del portal sin completarlo: el
    // resumen exige la confirmación del ciudadano.
    const propiosSaltados = Array.from({ length: destino - paso - 1 }, (_, k) => paso + 1 + k).every((i) => !pasos[i].propio || pasoValido(i));
    if (!pasoValido(paso) || !propiosSaltados || sinTarifa) {
      setIntento((n) => n + 1);
      return;
    }
    if (!(await guardarPaso())) return;
    irA(destino);
  };

  const retroceder = () => {
    if (paso === 0) setSaliendoDelTramite(true);
    else if (!guardando) irA(paso - 1);
  };

  const alTocarNodo = (i: number) => {
    if (i < paso) irA(i);
    else void avanzar(i);
  };

  const alIniciarToque = (e: TouchEvent) => {
    toqueInicial.current = e.touches[0]?.clientX ?? null;
  };
  const alTerminarToque = (e: TouchEvent) => {
    const inicio = toqueInicial.current;
    toqueInicial.current = null;
    const fin = e.changedTouches[0]?.clientX;
    if (inicio === null || fin === undefined || paso === ultimo) return;
    if (inicio - fin > UMBRAL_DESLIZAR_PX) void avanzar();
    else if (fin - inicio > UMBRAL_DESLIZAR_PX && paso > 0) irA(paso - 1);
  };

  // ── Presentación ──────────────────────────────────────────────────────────────────────────────────────────────────

  const nombre = definicion.name;
  const vehiculo = solicitud.placa ?? solicitud.vin ?? "";
  const etiquetaVehiculo = solicitud.placa ? "Placa" : "VIN";
  const solicitante = solicitud.actores.find((a) => a.nombre)?.nombre ?? null;
  const motivo = bloqueoDe(paso);
  const siguienteDeshabilitado = motivo !== undefined;
  const base = pasos[paso];
  const etiquetaSiguiente = base.id === "requisitos" ? "Siguiente" : "Continuar";

  return (
    <ProveedorWizard value={api}>
      <div className="min-h-screen bg-background font-sans text-foreground">
        <header className="wizard-header flex items-center justify-between border-b border-border bg-background py-3">
          <Link href="/" aria-label="FLIT 2.0 — Ir al Dashboard">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={ASSETS.logo} alt="FLIT 2.0" className="h-9 w-auto" />
          </Link>
          <div className="hidden items-center gap-6 text-sm md:flex">
            {solicitante && (
              <span>
                <b>{solicitante}</b>
              </span>
            )}
            {vehiculo && (
              <span>
                {etiquetaVehiculo} <b>{vehiculo}</b>
              </span>
            )}
            <span>{nombre}</span>
            <Bell role="img" className="h-6 w-6 text-primary" aria-label="Notificaciones" />
          </div>
        </header>

        <main className="wizard-main w-full py-8">
          <nav aria-label="Ruta de navegación">
            <p className="text-sm font-medium text-muted-foreground">
              <Link href="/" className="hover:text-primary">Dashboard</Link> &gt; Trámites &gt; {nombre} &gt;{" "}
              <span aria-current="page" className="border-b-2 border-primary text-primary">{base.miga}</span>
            </p>
          </nav>
          <h1 className="mt-2 text-[32px] font-bold">Trámite de {nombre}</h1>

          <div className="wizard-cols mt-6">
            {/* Barra lateral */}
            <aside className="wizard-sidebar" aria-label="Resumen y navegación del trámite">
              <div className="flex flex-col items-center gap-2 text-center">
                <div className="flex items-center justify-center gap-3">
                  <span aria-hidden className="grid h-12 w-12 shrink-0 place-items-center rounded-full bg-primary/10 text-primary">
                    <Car className="h-6 w-6" />
                  </span>
                  <p className="font-bold text-primary">Mi trámite</p>
                </div>
                {vehiculo && <p className="rounded-full bg-primary/10 px-3 py-0.5 text-sm font-semibold text-primary">{vehiculo}</p>}
                <p className="text-xs font-light text-muted-foreground">
                  Radicado <b data-testid="radicado">{solicitud.codigoPublico}</b>
                </p>
              </div>
              <div className="space-y-3 text-sm">
                <p className="text-center font-bold text-primary">Configuración del trámite</p>
                <OrganismoDelTramite cliente={cliente} solicitud={solicitud} editable={editable && paso === 0 && !solicitud.datos.diagnostico} />
                <div>
                  <span className="mb-1 block text-xs font-light text-muted-foreground">Tipo de Trámite</span>
                  <input className="field" readOnly value={nombre} aria-label="Tipo de Trámite" />
                </div>
                {organismoElegido(solicitud.organismoId) && (
                  <ValorTramite cliente={cliente} organismoId={solicitud.organismoId} tipoCodigo={definicion.code} onSinTarifa={setSinTarifa} />
                )}
              </div>
              <hr className="border-border" />
              <ol className="wizard-step-nav space-y-1" aria-label="Pasos del trámite">
                {pasos.map((p, i) => {
                  const hecho = i < paso;
                  const activo = i === paso;
                  const clicable = editable && !activo && i <= alcanzado && paso !== ultimo;
                  return (
                    <li key={p.id}>
                      <button
                        type="button"
                        disabled={!clicable}
                        onClick={() => alTocarNodo(i)}
                        aria-current={activo ? "step" : undefined}
                        className={boton("ghost", "flex h-auto w-full items-center justify-start gap-3 px-2 py-1 hover:bg-primary/5 disabled:cursor-default disabled:opacity-100", "none")}
                      >
                        <span
                          className={`grid h-9 w-9 shrink-0 place-items-center rounded-full border-2 text-sm font-semibold transition-colors ${
                            hecho ? "border-success bg-success text-primary-foreground" : activo ? "border-primary text-primary" : "border-disabled text-disabled"
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
                Paso {paso + 1} de {pasos.length}: {base.titulo}
              </p>
            </aside>

            {/* Área de trabajo: carrusel horizontal, cada paso ocupa el 100 % del ancho */}
            <div className="min-w-0 space-y-6">
              {solicitud.definicionCambio && (
                <p role="status" className="flex gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
                  <Info aria-hidden className="h-5 w-5 shrink-0" />
                  <span>La información de este trámite se actualizó. Revisa los datos de tus pasos antes de continuar.</span>
                </p>
              )}
              {conflicto && (
                <div role="alert" className="flex flex-col items-start gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
                  <p className="flex gap-2">
                    <TriangleAlert aria-hidden className="h-5 w-5 shrink-0" />
                    <span>Este trámite cambió mientras lo editabas. Recarga para continuar con lo último guardado.</span>
                  </p>
                  <button
                    type="button"
                    className={boton("back", "min-h-11 px-6", "none")}
                    onClick={() => {
                      void api.recargar().then(() => setConflicto(false));
                    }}
                  >
                    Recargar
                  </button>
                </div>
              )}

              <div className="wizard-slider overflow-x-hidden" onTouchStart={alIniciarToque} onTouchEnd={alTerminarToque}>
                <div className="flex items-start transition-transform duration-300 ease-out motion-reduce:transition-none" style={{ transform: `translateX(-${paso * 100}%)` }}>
                  {pasos.map((p, i) => {
                    const visible = i === paso || i === saliendo;
                    return (
                      <div
                        key={p.id}
                        ref={(el) => {
                          slides.current[i] = el;
                        }}
                        className="wizard-slide w-full min-w-0 shrink-0 space-y-6"
                        data-active={visible}
                        inert={i !== paso}
                        aria-hidden={i !== paso}
                      >
                        <h2
                          ref={(el) => {
                            titulos.current[i] = el;
                          }}
                          tabIndex={-1}
                          className="sr-only"
                        >
                          Paso {i + 1} · {p.titulo}
                        </h2>
                        {p.secciones.length === 0 && <p className="text-sm text-muted-foreground">Este trámite no pide información en este paso.</p>}
                        {p.secciones.map((s) => (
                          <Seccion key={s.clave} seccion={s} registro={registro} activa={i === paso} intentoAvance={i === paso ? intento : 0} onEstado={setEstados} />
                        ))}
                      </div>
                    );
                  })}
                </div>
              </div>

              <div aria-live="polite">{errorGuardado && <p role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">{errorGuardado}</p>}</div>

              {paso < ultimo && editable && pasos[paso].id !== "resumen" && (
                <Nav
                  onBack={retroceder}
                  onNext={() => void avanzar()}
                  nextLabel={etiquetaSiguiente}
                  nextDisabled={siguienteDeshabilitado}
                  motivo={siguienteDeshabilitado ? motivo : undefined}
                  idMotivo={idMotivo}
                  ocupado={guardando}
                />
              )}
            </div>
          </div>
        </main>

        <ModalFlit abierto={saliendoDelTramite} onCerrar={() => setSaliendoDelTramite(false)} titulo="Salir del trámite" icono={LogOut} ancho="2xl" descripcion="Tu borrador se conserva: puedes consultarlo desde «Consultar mi trámite» con la placa y el radicado o la cédula.">
          <div className="mt-5 flex flex-wrap justify-center gap-3">
            <button type="button" className={boton("back", "h-11 px-6", "none")} onClick={() => setSaliendoDelTramite(false)}>
              Seguir aquí
            </button>
            <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={() => router.push("/")}>
              Salir
            </button>
          </div>
        </ModalFlit>

        <ModalFlit abierto={vencida} onCerrar={onSesionVencida} titulo="Tu sesión venció" icono={TriangleAlert} ancho="2xl" descripcion="Reanuda tu trámite con tu radicado y tu cédula. Lo que ya guardaste se conserva.">
          <div className="mt-5 flex justify-center">
            <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={onSesionVencida}>
              Reanudar mi trámite
            </button>
          </div>
        </ModalFlit>
      </div>
    </ProveedorWizard>
  );
}

interface SeccionProps {
  seccion: SeccionWizard;
  registro: RegistroRenderers;
  activa: boolean;
  intentoAvance: number;
  onEstado: React.Dispatch<React.SetStateAction<Record<string, EstadoSeccion>>>;
}

/**
 * Organismo del paso 1. Si el trámite ya lo tiene, se muestra fijo. Si no, el ciudadano lo busca aquí
 * (el modal de requisitos no lo pide) y queda fijo al consultar el vehículo.
 */
function OrganismoDelTramite({ cliente, solicitud, editable }: { cliente: ClientePortal; solicitud: Solicitud; editable: boolean }) {
  const elegido = organismoElegido(solicitud.organismoId);
  if (elegido || !editable) {
    return (
      <div>
        <span className="mb-1 block text-xs font-light text-muted-foreground">Organismo de Tránsito</span>
        <input className="field" readOnly value={solicitud.organismoNombre} aria-label="Organismo de Tránsito" />
      </div>
    );
  }
  return <ElegirOrganismo cliente={cliente} solicitud={solicitud} />;
}

function ElegirOrganismo({ cliente, solicitud }: { cliente: ClientePortal; solicitud: Solicitud }) {
  const queryClient = useQueryClient();
  const organismos = useOrganismos(cliente);
  const asignar = useMutation({
    mutationFn: (organismo: Organismo) => cliente.solicitudes.asignarOrganismo(solicitud.solicitudId, { organismoId: organismo.id, version: solicitud.version }),
    onSuccess: (actualizada) => {
      queryClient.setQueryData(claveSolicitud(solicitud.solicitudId), actualizada);
    },
  });

  return (
    <div>
      <span className="mb-1 block text-xs font-light text-muted-foreground">Organismo de Tránsito</span>
      {organismos.isSuccess && (
        <SelectorOrganismo organismos={organismos.data ?? []} valor={null} onChange={(organismo) => asignar.mutate(organismo)} disabled={asignar.isPending} />
      )}
      {organismos.isPending && <p className="text-xs text-muted-foreground">Cargando organismos…</p>}
      {(organismos.isError || asignar.isError) && <p role="alert" className="mt-1 text-xs text-alert">{mensajeDe(organismos.error ?? asignar.error)}</p>}
    </div>
  );
}

/** Monta el renderer de la sección y le da un `onEstado` estable que escribe en el mapa de estados del cascarón. */
const Seccion = memo(function Seccion({ seccion, registro, activa, intentoAvance, onEstado }: SeccionProps) {
  const clave = seccion.clave;
  const informar = useCallback(
    (estado: EstadoSeccion) =>
      onEstado((previo) => {
        const actual = previo[clave];
        return actual && actual.completo === estado.completo && actual.bloqueo === estado.bloqueo ? previo : { ...previo, [clave]: estado };
      }),
    [clave, onEstado],
  );
  const Renderer = registro[seccion.sectionType];
  if (!Renderer) return null;
  return <Renderer seccion={seccion} activa={activa} intentoAvance={intentoAvance} onEstado={informar} />;
});
