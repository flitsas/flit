"use client";

// «Programar envío» de una consulta guardada (Reportes 2.0, ronda 2): la consulta llega por correo cada semana o cada
// mes, a la hora de Colombia que se elija.
//
// Va en el PanelLateral del kit y no en un modal: junta la lista de envíos de la consulta y el formulario, y su pie fijo
// deja «Guardar» al alcance aunque en el celular se abra el teclado. Pausar guarda al instante desde la tarjeta; borrar
// se confirma en la propia tarjeta. Lo que dice el último envío llega del servidor ya en lenguaje de negocio.

import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { InlineAlert, PanelLateral } from "@flit/ui";
import { SelectorSegmentado } from "@flit/ui/SelectorSegmentado";
import { Interruptor } from "@/components/ui/Interruptor";
import { BOTON_PELIGRO_CLS, BOTON_PRIMARIO_CLS, ETIQUETA_CLS, FECHA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import {
  borrarProgramacion,
  guardarProgramacion,
  listarProgramaciones,
  mensajeDeError,
  type ConsultaProgramada,
  type GuardarProgramacion,
  type Periodicidad,
  type SavedQuery,
} from "@/lib/api/consultas-personalizadas";
import { formatoFechaHora } from "@/lib/format/date";

/** Día de la semana ISO, como el contrato §4: 1 = lunes … 7 = domingo. NO es el `DayOfWeek` de .NET (0 = domingo). */
export const DIAS_SEMANA: { valor: number; nombre: string }[] = [
  { valor: 1, nombre: "Lunes" },
  { valor: 2, nombre: "Martes" },
  { valor: 3, nombre: "Miércoles" },
  { valor: 4, nombre: "Jueves" },
  { valor: 5, nombre: "Viernes" },
  { valor: 6, nombre: "Sábado" },
  { valor: 7, nombre: "Domingo" },
];

const DIAS_MES = Array.from({ length: 28 }, (_, i) => i + 1);

const CORREO = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

/** Trocea lo pegado (coma, punto y coma, espacios o saltos de línea) en correos únicos, sin distinguir mayúsculas. */
export function separarCorreos(texto: string): { validos: string[]; invalidos: string[] } {
  const vistos = new Set<string>();
  const validos: string[] = [];
  const invalidos: string[] = [];
  for (const crudo of texto.split(/[\s,;]+/)) {
    const correo = crudo.trim().toLowerCase();
    if (!correo || vistos.has(correo)) continue;
    vistos.add(correo);
    (CORREO.test(correo) ? validos : invalidos).push(correo);
  }
  return { validos, invalidos };
}

const horaCorta = (hora: string) => hora.slice(0, 5);

/** «Cada lunes a las 07:00» / «El día 5 de cada mes a las 07:00». */
export function describirFrecuencia(p: Pick<ConsultaProgramada, "periodicidad" | "diaSemana" | "diaMes" | "hora">): string {
  const hora = `a las ${horaCorta(p.hora)}`;
  if (p.periodicidad === "semanal") {
    const dia = DIAS_SEMANA.find((d) => d.valor === p.diaSemana)?.nombre.toLowerCase() ?? "semana";
    return `Cada ${dia} ${hora}`;
  }
  return `El día ${p.diaMes ?? 1} de cada mes ${hora}`;
}

interface Borrador {
  id?: string;
  periodicidad: Periodicidad;
  diaSemana: number;
  diaMes: number;
  hora: string;
  destinatarios: string;
  activa: boolean;
}

const BORRADOR_NUEVO: Borrador = { periodicidad: "semanal", diaSemana: 1, diaMes: 1, hora: "07:00", destinatarios: "", activa: true };

function desde(p: ConsultaProgramada): Borrador {
  return {
    id: p.id,
    periodicidad: p.periodicidad,
    diaSemana: p.diaSemana ?? 1,
    diaMes: p.diaMes ?? 1,
    hora: horaCorta(p.hora),
    destinatarios: p.destinatarios.join("\n"),
    activa: p.activa,
  };
}

function aEntrada(consultaId: string, b: Borrador, destinatarios: string[]): GuardarProgramacion {
  return {
    id: b.id,
    consultaId,
    periodicidad: b.periodicidad,
    diaSemana: b.periodicidad === "semanal" ? b.diaSemana : null,
    diaMes: b.periodicidad === "mensual" ? b.diaMes : null,
    hora: b.hora,
    destinatarios,
    activa: b.activa,
  };
}

const PREFIJO = "cmp-query-programar";

export function ProgramarEnvio({
  consulta,
  empresa,
  onCerrar,
  soloLectura = false,
}: {
  /** La consulta guardada (propia) cuyo envío se programa; `null` cierra el panel. */
  consulta: SavedQuery | null;
  empresa: string | null;
  onCerrar: () => void;
  /**
   * El SuperAdmin ve los envíos (sirve para soporte) pero no los crea, edita, pausa ni borra: el servidor se lo
   * rechaza (`SUPERADMIN_SOLO_LECTURA`). Sin esto vería controles que fallan siempre.
   */
  soloLectura?: boolean;
}) {
  const [todas, setTodas] = useState<ConsultaProgramada[] | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);
  const [recarga, setRecarga] = useState(0);
  const [borrador, setBorrador] = useState<Borrador | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [errorGuardar, setErrorGuardar] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [porBorrar, setPorBorrar] = useState<string | null>(null);
  const [errorTarjeta, setErrorTarjeta] = useState<{ id: string; mensaje: string } | null>(null);
  // La tarjeta con una pausa o un borrado en vuelo: sus controles se apagan y el panel no se cierra (REX).
  const [operando, setOperando] = useState<string | null>(null);
  const nuevoRef = useRef<HTMLButtonElement>(null);
  const enfocarNuevo = useRef(false);

  const consultaId = consulta?.id ?? null;
  // La consulta vigente, para descartar respuestas que lleguen de una consulta anterior.
  const vigente = useRef(consultaId);
  useEffect(() => {
    vigente.current = consultaId;
  }, [consultaId]);

  // Tras guardar o borrar, el control que tenía el foco desaparece: el foco vuelve a «Nuevo envío».
  useEffect(() => {
    if (!enfocarNuevo.current || !nuevoRef.current) return;
    enfocarNuevo.current = false;
    nuevoRef.current.focus();
  });

  useEffect(() => {
    if (!consultaId) return undefined;
    const controller = new AbortController();
    listarProgramaciones(empresa, controller.signal)
      .then((datos) => {
        if (controller.signal.aborted) return;
        setTodas(datos);
        setErrorCarga(null);
      })
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setErrorCarga(mensajeDeError(e, "No se pudieron cargar los envíos programados."));
      });
    return () => controller.abort();
  }, [consultaId, empresa, recarga]);

  // Al cambiar de consulta, el panel arranca limpio.
  const [abiertaPara, setAbiertaPara] = useState(consultaId);
  if (abiertaPara !== consultaId) {
    setAbiertaPara(consultaId);
    setTodas(null);
    setErrorCarga(null);
    setBorrador(null);
    setAviso(null);
    setPorBorrar(null);
    setErrorTarjeta(null);
    setOperando(null);
  }

  const deEsta = useMemo(() => (todas ?? []).filter((p) => p.consultaId === consultaId), [todas, consultaId]);

  const reemplazar = useCallback((p: ConsultaProgramada) => {
    setTodas((prev) => [...(prev ?? []).filter((x) => x.id !== p.id), p]);
  }, []);

  async function guardar(b: Borrador, destinatarios: string[]) {
    if (!consultaId) return;
    setGuardando(true);
    setErrorGuardar(null);
    try {
      const guardada = await guardarProgramacion(aEntrada(consultaId, b, destinatarios), empresa);
      if (vigente.current !== consultaId) return;
      reemplazar(guardada);
      setBorrador(null);
      enfocarNuevo.current = true;
      setAviso(`Listo: ${describirFrecuencia(guardada).toLowerCase()}, a ${destinatarios.length === 1 ? "1 destinatario" : `${destinatarios.length} destinatarios`}.`);
    } catch (e: unknown) {
      // El formulario se conserva: el error se lee junto a «Guardar» y se puede corregir sin volver a escribir.
      setErrorGuardar(mensajeDeError(e, "No se pudo guardar el envío."));
    } finally {
      setGuardando(false);
    }
  }

  async function pausar(p: ConsultaProgramada, activa: boolean) {
    if (operando) return;
    setErrorTarjeta(null);
    setOperando(p.id);
    try {
      const guardada = await guardarProgramacion({ ...aEntrada(p.consultaId, desde(p), p.destinatarios), activa }, empresa);
      if (vigente.current === p.consultaId) reemplazar(guardada);
    } catch (e: unknown) {
      if (vigente.current === p.consultaId) setErrorTarjeta({ id: p.id, mensaje: mensajeDeError(e, "No se pudo cambiar el envío.") });
    } finally {
      setOperando(null);
    }
  }

  async function borrar(p: ConsultaProgramada) {
    if (operando) return;
    setErrorTarjeta(null);
    setOperando(p.id);
    try {
      await borrarProgramacion(p.id, empresa);
      if (vigente.current !== p.consultaId) return;
      setTodas((prev) => (prev ?? []).filter((x) => x.id !== p.id));
      setPorBorrar(null);
      setAviso("Envío borrado.");
      enfocarNuevo.current = true;
    } catch (e: unknown) {
      if (vigente.current === p.consultaId) setErrorTarjeta({ id: p.id, mensaje: mensajeDeError(e, "No se pudo borrar el envío.") });
    } finally {
      setOperando(null);
    }
  }

  const correos = useMemo(() => separarCorreos(borrador?.destinatarios ?? ""), [borrador?.destinatarios]);
  const puedeGuardar = !!borrador && correos.validos.length > 0 && correos.invalidos.length === 0 && /^\d{2}:\d{2}$/.test(borrador.hora) && !guardando;

  return (
    <PanelLateral
      abierto={consulta !== null}
      onCerrar={onCerrar}
      titulo="Programar envío"
      descripcion={consulta ? <>La consulta «{consulta.nombre}» llega por correo, con su Excel, cuando tú digas.</> : undefined}
      ancho={560}
      ocupado={guardando || operando !== null}
      pie={
        borrador ? (
          <div className="flex flex-col gap-2">
            {errorGuardar && (
              <div data-testid={`${PREFIJO}-error`}>
                <InlineAlert tone="error" compact>
                  {errorGuardar}
                </InlineAlert>
              </div>
            )}
            <div className="flex flex-wrap justify-end gap-2">
              <button type="button" className="min-h-9 rounded-[9px] px-3 text-sm font-semibold text-[color:var(--di-text-tertiary)] hover:text-[color:var(--di-action)]" onClick={() => { setBorrador(null); setErrorGuardar(null); }}>
                Cancelar
              </button>
              <button
                type="button"
                className={BOTON_PRIMARIO_CLS}
                disabled={!puedeGuardar}
                onClick={() => borrador && void guardar(borrador, correos.validos)}
                data-testid={`${PREFIJO}-guardar`}
              >
                {guardando ? "Guardando…" : "Guardar envío"}
              </button>
            </div>
          </div>
        ) : undefined
      }
    >
      <div className="flex flex-col gap-4" data-testid={PREFIJO}>
        {soloLectura && (
          <p className="rounded-[9px] bg-[var(--badge-info-bg)] px-3 py-2 text-xs text-[color:var(--badge-info-fg)]" data-testid={`${PREFIJO}-solo-lectura`}>
            El SuperAdmin consulta los envíos; los crea y edita la empresa.
          </p>
        )}
        {aviso && (
          <InlineAlert tone="success" compact>
            {aviso}
          </InlineAlert>
        )}

        {borrador ? (
          <Formulario borrador={borrador} onCambio={setBorrador} invalidos={correos.invalidos} validos={correos.validos.length} />
        ) : (
          <>
            {errorCarga ? (
              <InlineAlert
                tone="error"
                title="No se pudieron cargar los envíos"
                action={
                  <button type="button" className="font-semibold underline" onClick={() => setRecarga((n) => n + 1)}>
                    Reintentar
                  </button>
                }
              >
                {errorCarga}
              </InlineAlert>
            ) : todas === null ? (
              <p className="min-h-24 text-sm text-[color:var(--di-text-tertiary)]" role="status">
                Cargando los envíos…
              </p>
            ) : deEsta.length === 0 ? (
              <p className="rounded-[9px] border border-dashed border-[color:var(--di-border)] px-4 py-4 text-sm text-[color:var(--di-text-tertiary)]" data-testid={`${PREFIJO}-vacio`}>
                Esta consulta todavía no se envía por correo.
              </p>
            ) : (
              <ul className="flex flex-col gap-3" data-testid={`${PREFIJO}-lista`}>
                {deEsta.map((p) => (
                  <Tarjeta
                    key={p.id}
                    p={p}
                    confirmando={porBorrar === p.id}
                    ocupada={operando === p.id}
                    soloLectura={soloLectura}
                    error={errorTarjeta?.id === p.id ? errorTarjeta.mensaje : null}
                    onEditar={() => {
                      setAviso(null);
                      setErrorGuardar(null);
                      setBorrador(desde(p));
                    }}
                    onPausar={(activa) => void pausar(p, activa)}
                    onPedirBorrado={() => setPorBorrar(p.id)}
                    onCancelarBorrado={() => setPorBorrar(null)}
                    onBorrar={() => void borrar(p)}
                  />
                ))}
              </ul>
            )}
            {!soloLectura && (
            <button
              ref={nuevoRef}
              type="button"
              className={`${BOTON_PRIMARIO_CLS} self-start`}
              // Sin la lista cargada no se ofrece crear: se podría duplicar un envío que ya existe sin verlo.
              disabled={todas === null}
              onClick={() => {
                setAviso(null);
                setErrorGuardar(null);
                setBorrador({ ...BORRADOR_NUEVO });
              }}
              data-testid={`${PREFIJO}-nuevo`}
            >
              Nuevo envío
            </button>
            )}
          </>
        )}
      </div>
    </PanelLateral>
  );
}

function Formulario({
  borrador,
  onCambio,
  validos,
  invalidos,
}: {
  borrador: Borrador;
  onCambio: (b: Borrador) => void;
  validos: number;
  invalidos: string[];
}) {
  const idCorreos = useId();
  const idAyudaCorreos = useId();
  const set = (parcial: Partial<Borrador>) => onCambio({ ...borrador, ...parcial });
  // Al abrir, el botón que tenía el foco («Nuevo envío» o «Editar») desaparece: el foco va al primer control.
  const raiz = useRef<HTMLDivElement>(null);
  useEffect(() => {
    raiz.current?.querySelector<HTMLElement>("button, select, input, textarea")?.focus();
  }, []);

  return (
    <div ref={raiz} className="flex flex-col gap-4" data-testid={`${PREFIJO}-formulario`}>
      <SelectorSegmentado<Periodicidad>
        etiqueta="Frecuencia"
        opciones={[
          { valor: "semanal", label: "Cada semana" },
          { valor: "mensual", label: "Cada mes" },
        ]}
        valor={borrador.periodicidad}
        onCambio={(periodicidad) => set({ periodicidad })}
      />

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {borrador.periodicidad === "semanal" ? (
          <label className={ETIQUETA_CLS}>
            Día de la semana
            <select className={INPUT_CLS} value={borrador.diaSemana} onChange={(e) => set({ diaSemana: Number(e.target.value) })} data-testid={`${PREFIJO}-dia-semana`}>
              {DIAS_SEMANA.map((d) => (
                <option key={d.valor} value={d.valor}>
                  {d.nombre}
                </option>
              ))}
            </select>
          </label>
        ) : (
          <label className={ETIQUETA_CLS}>
            Día del mes
            <select className={INPUT_CLS} value={borrador.diaMes} onChange={(e) => set({ diaMes: Number(e.target.value) })} data-testid={`${PREFIJO}-dia-mes`}>
              {DIAS_MES.map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>
            <span className="font-normal">Hasta el 28, para que el envío salga también en febrero.</span>
          </label>
        )}
        <label className={ETIQUETA_CLS}>
          Hora (Colombia)
          <input type="time" className={FECHA_CLS} value={borrador.hora} onChange={(e) => set({ hora: e.target.value })} data-testid={`${PREFIJO}-hora`} />
        </label>
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor={idCorreos} className={ETIQUETA_CLS}>
          Destinatarios
        </label>
        <textarea
          id={idCorreos}
          className={TEXTAREA_CLS}
          value={borrador.destinatarios}
          onChange={(e) => set({ destinatarios: e.target.value })}
          placeholder="correo@empresa.com, otro@empresa.com"
          aria-invalid={invalidos.length > 0 ? true : undefined}
          aria-describedby={idAyudaCorreos}
          data-testid={`${PREFIJO}-destinatarios`}
        />
        <div id={idAyudaCorreos} aria-live="polite">
          {invalidos.length > 0 ? (
            <InlineAlert tone="error" compact vivo={false}>
              <span data-testid={`${PREFIJO}-correos-invalidos`}>
                {invalidos.length === 1 ? "Este correo no es válido" : "Estos correos no son válidos"}: <span className="break-all">{invalidos.join(", ")}</span>
              </span>
            </InlineAlert>
          ) : (
            <span className="text-[11px] text-muted-foreground">
              {validos === 0 ? "Uno o varios correos, separados por coma o en líneas distintas." : validos === 1 ? "1 destinatario." : `${validos} destinatarios.`}
            </span>
          )}
        </div>
      </div>

      <Interruptor
        activo={borrador.activa}
        onCambio={(activa) => set({ activa })}
        etiqueta="Envío activo"
        ayuda="Si lo pausas, se conserva pero no sale hasta que lo vuelvas a activar."
      />
    </div>
  );
}

function Tarjeta({
  p,
  confirmando,
  ocupada,
  soloLectura,
  error,
  onEditar,
  onPausar,
  onPedirBorrado,
  onCancelarBorrado,
  onBorrar,
}: {
  p: ConsultaProgramada;
  confirmando: boolean;
  ocupada: boolean;
  soloLectura: boolean;
  error: string | null;
  onEditar: () => void;
  onPausar: (activa: boolean) => void;
  onPedirBorrado: () => void;
  onCancelarBorrado: () => void;
  onBorrar: () => void;
}) {
  const frecuencia = describirFrecuencia(p);
  return (
    <li className="flex flex-col gap-2 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-4" data-testid={`${PREFIJO}-envio-${p.id}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-sm font-semibold text-[color:var(--di-heading)]">{frecuencia}</p>
          <p className="break-all text-xs text-[color:var(--di-text-tertiary)]">{p.destinatarios.join(", ")}</p>
        </div>
        {/* Etiqueta fija y propia de la tarjeta: el estado lo dice aria-checked (REX). */}
        {soloLectura ? (
          // En solo lectura el estado es texto: un interruptor apagado se lee como algo que se podría mover.
          <span className="text-xs font-semibold text-muted-foreground" data-testid={`${PREFIJO}-estado-${p.id}`}>
            {p.activa ? "Activo" : "En pausa"}
          </span>
        ) : (
          <Interruptor activo={p.activa} onCambio={onPausar} etiqueta={`Envío activo: ${frecuencia}`} deshabilitado={ocupada} soloControl />
        )}
      </div>

      <dl className="grid grid-cols-1 gap-x-4 gap-y-1 text-xs sm:grid-cols-[auto_1fr]">
        <dt className="font-semibold text-muted-foreground">Próximo envío</dt>
        <dd>{p.activa ? formatoFechaHora(p.proximaEjecucion) : "En pausa"}</dd>
        <dt className="font-semibold text-muted-foreground">Último envío</dt>
        <dd>{p.ultimaEjecucion ? formatoFechaHora(p.ultimaEjecucion) : "Todavía no ha salido"}</dd>
        {p.ultimoResultado && (
          <>
            <dt className="font-semibold text-muted-foreground">Resultado</dt>
            <dd data-testid={`${PREFIJO}-resultado-${p.id}`}>{p.ultimoResultado}</dd>
          </>
        )}
      </dl>

      {error && (
        <InlineAlert tone="error" compact>
          {error}
        </InlineAlert>
      )}

      {soloLectura ? null : confirmando ? (
        <div
          className="flex flex-wrap items-center gap-2 rounded-[9px] bg-[var(--badge-danger-bg)] p-2"
          data-testid={`${PREFIJO}-confirmar-${p.id}`}
          // Escape cancela la confirmación, no cierra el panel entero.
          onKeyDown={(e) => {
            if (e.key !== "Escape") return;
            e.stopPropagation();
            e.nativeEvent.stopImmediatePropagation();
            onCancelarBorrado();
          }}
        >
          <span className="min-w-0 flex-1 text-xs">Se borrará este envío. La consulta guardada no se toca.</span>
          <button type="button" autoFocus className="min-h-9 rounded-[9px] px-3 text-xs font-semibold" onClick={onCancelarBorrado}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PELIGRO_CLS} onClick={onBorrar} disabled={ocupada} data-testid={`${PREFIJO}-borrar-si-${p.id}`}>
            {ocupada ? "Borrando…" : "Borrar envío"}
          </button>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          <button type="button" className="min-h-9 rounded-[9px] px-3 text-xs font-semibold text-[color:var(--di-action)] underline underline-offset-[3px]" onClick={onEditar} disabled={ocupada} aria-label={`Editar el envío ${frecuencia.toLowerCase()}`} data-testid={`${PREFIJO}-editar-${p.id}`}>
            Editar
          </button>
          <button type="button" className="min-h-9 rounded-[9px] px-3 text-xs font-semibold text-muted-foreground hover:text-destructive" onClick={onPedirBorrado} disabled={ocupada} aria-label={`Borrar el envío ${frecuencia.toLowerCase()}`} data-testid={`${PREFIJO}-borrar-${p.id}`}>
            Borrar
          </button>
        </div>
      )}
    </li>
  );
}
