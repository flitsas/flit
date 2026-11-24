"use client";

// «Programar envío» de una consulta guardada (Reportes 2.0, ronda 2): la consulta llega por correo cada semana o cada
// mes, a la hora de Colombia que se elija.
//
// MISMO PATRÓN QUE TRÁMITES («Programación y alertas», `frontend/components/atom/modules/_reportes/scheduling/
// SchedulingPanel.tsx` + `ScheduleForm.tsx`): modal centrado con ícono de calendario, título y subtítulo; formulario en
// dos columnas desde `sm` (Periodicidad | Día · Hora | Formato), destinatarios en fichas («Presiona Enter o coma para
// agregar») y la casilla «Activo». Quien usa los dos productos encuentra la misma interacción para la misma tarea.
// Aquí se usa el `Modal` de `@flit/ui`: cuerpo con scroll propio y pie fijo, así «Guardar» sigue a la vista a 360 px.
//
// Como Trámites: la hora es un selector de horas en punto (00:00 … 23:00) y el máximo es de 10 destinatarios. Lo único
// distinto, aprobado por S0: el formato es siempre Excel (la consulta va como adjunto) y se puede pegar una lista
// entera de correos. Como en Trámites, un correo se valida al agregarlo (Enter, coma o salir del campo); si quedó uno inválido
// sin agregar, Guardar se apaga y el error está a la vista.

import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { InlineAlert, Modal } from "@flit/ui";
import { Icono } from "@flit/ui/Icono";
import { BOTON_PELIGRO_CLS, BOTON_PRIMARIO_CLS, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
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

export const MAX_DESTINATARIOS = 10;

/** Las horas que ofrece el selector, como en Trámites: en punto, de 00:00 a 23:00 («HH:mm», que es lo que guarda §4). */
export const HORAS: string[] = Array.from({ length: 24 }, (_, h) => `${String(h).padStart(2, "0")}:00`);

/**
 * Opciones de hora para un envío. Una programación vieja con minutos (p. ej. 11:20) conserva su hora como opción extra,
 * en su lugar del día: editarla por otra cosa no debe cambiarle la hora sin que nadie lo pida.
 */
export function opcionesHora(horaOriginal?: string | null): string[] {
  const extra = horaOriginal ? horaOriginal.slice(0, 5) : null;
  return extra && !HORAS.includes(extra) ? [...HORAS, extra].sort() : HORAS;
}

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
  /** La hora con la que llegó el envío al editarlo: si tiene minutos, sigue ofreciéndose. */
  horaOriginal?: string;
  destinatarios: string[];
  activa: boolean;
}

const BORRADOR_NUEVO: Borrador = { periodicidad: "semanal", diaSemana: 1, diaMes: 1, hora: "07:00", destinatarios: [], activa: true };

function desde(p: ConsultaProgramada): Borrador {
  return {
    id: p.id,
    periodicidad: p.periodicidad,
    diaSemana: p.diaSemana ?? 1,
    diaMes: p.diaMes ?? 1,
    hora: horaCorta(p.hora),
    horaOriginal: horaCorta(p.hora),
    destinatarios: [...p.destinatarios],
    activa: p.activa,
  };
}

function aEntrada(consultaId: string, b: Borrador): GuardarProgramacion {
  return {
    id: b.id,
    consultaId,
    periodicidad: b.periodicidad,
    diaSemana: b.periodicidad === "semanal" ? b.diaSemana : null,
    diaMes: b.periodicidad === "mensual" ? b.diaMes : null,
    hora: b.hora,
    destinatarios: b.destinatarios,
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
  /** La consulta guardada (propia) cuyo envío se programa; `null` cierra el modal. */
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
  // Quedó en el campo de destinatarios un correo que no se pudo agregar (inválido): bloquea Guardar, porque se leería
  // como incluido y no lo está. Solo se enciende tras un intento de agregar, que siempre deja su error a la vista.
  const [pendiente, setPendiente] = useState(false);
  const [errorGuardar, setErrorGuardar] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [porBorrar, setPorBorrar] = useState<string | null>(null);
  const [errorTarjeta, setErrorTarjeta] = useState<{ id: string; mensaje: string } | null>(null);
  // La tarjeta con una pausa o un borrado en vuelo: sus controles se apagan y el modal no se cierra.
  const [operando, setOperando] = useState<string | null>(null);
  const nuevoRef = useRef<HTMLButtonElement>(null);
  const enfocarNuevo = useRef(false);
  // Control de una tarjeta al que vuelve el foco tras pausar o borrar (se deshabilita mientras tanto y el foco se caía).
  const reenfocar = useRef<string | null>(null);

  const consultaId = consulta?.id ?? null;
  // La consulta vigente, para descartar respuestas que lleguen de una consulta anterior.
  const vigente = useRef(consultaId);
  useEffect(() => {
    vigente.current = consultaId;
  }, [consultaId]);

  // Tras guardar, cancelar o borrar, el control que tenía el foco desaparece: el foco vuelve a «Nuevo envío».
  useEffect(() => {
    if (enfocarNuevo.current && nuevoRef.current) {
      enfocarNuevo.current = false;
      nuevoRef.current.focus();
      return;
    }
    if (reenfocar.current) {
      const control = document.querySelector<HTMLElement>(`[data-testid="${reenfocar.current}"]`);
      if (control && !(control as HTMLButtonElement).disabled) {
        reenfocar.current = null;
        control.focus();
      }
    }
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

  // Al cambiar de consulta, el modal arranca limpio.
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
    setPendiente(false);
    setErrorGuardar(null);
  }

  const deEsta = useMemo(() => (todas ?? []).filter((p) => p.consultaId === consultaId), [todas, consultaId]);

  const reemplazar = useCallback((p: ConsultaProgramada) => {
    setTodas((prev) => [...(prev ?? []).filter((x) => x.id !== p.id), p]);
  }, []);

  async function guardar(b: Borrador) {
    if (!consultaId) return;
    setGuardando(true);
    setErrorGuardar(null);
    try {
      const guardada = await guardarProgramacion(aEntrada(consultaId, b), empresa);
      if (vigente.current !== consultaId) return;
      reemplazar(guardada);
      setBorrador(null);
      setPendiente(false);
      enfocarNuevo.current = true;
      const n = b.destinatarios.length;
      setAviso(`Listo: ${describirFrecuencia(guardada).toLowerCase()}, a ${n === 1 ? "1 destinatario" : `${n} destinatarios`}.`);
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
    reenfocar.current = `${PREFIJO}-activo-${p.id}`;
    try {
      const guardada = await guardarProgramacion({ ...aEntrada(p.consultaId, desde(p)), activa }, empresa);
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
    // Si falla, el foco vuelve a «Borrar envío» (la confirmación sigue abierta y el error se lee en la tarjeta).
    reenfocar.current = `${PREFIJO}-borrar-si-${p.id}`;
    try {
      await borrarProgramacion(p.id, empresa);
      if (vigente.current !== p.consultaId) return;
      reenfocar.current = null;
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

  const puedeGuardar =
    !!borrador && borrador.destinatarios.length > 0 && !pendiente && /^\d{2}:\d{2}$/.test(borrador.hora) && !guardando;
  const cancelar = () => {
    setBorrador(null);
    setPendiente(false);
    setErrorGuardar(null);
    enfocarNuevo.current = true;
  };

  return (
    <Modal
      open={consulta !== null}
      onClose={onCerrar}
      title="Programar envío"
      icono="calendario"
      description={consulta ? <>«{consulta.nombre}» llega por correo, con su Excel, cuando tú digas.</> : undefined}
      size="xl"
      busy={guardando || operando !== null}
      footer={
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
              <button
                type="button"
                className="min-h-9 rounded-[9px] border border-[color:var(--di-border-control)] px-4 text-sm font-semibold text-flit-primary disabled:opacity-50"
                onClick={cancelar}
                disabled={guardando}
              >
                Cancelar
              </button>
              <button
                type="button"
                className={BOTON_PRIMARIO_CLS}
                disabled={!puedeGuardar}
                aria-busy={guardando}
                onClick={() => borrador && void guardar(borrador)}
                data-testid={`${PREFIJO}-guardar`}
              >
                {guardando ? "Guardando…" : "Guardar"}
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

        {/* Arriba, lo que ya existe. */}
        <section aria-label="Envíos de esta consulta" className="flex flex-col gap-3">
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
            <p className="min-h-16 text-sm text-[color:var(--di-text-tertiary)]" role="status">
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
                  bloqueada={borrador !== null}
                  soloLectura={soloLectura}
                  error={errorTarjeta?.id === p.id ? errorTarjeta.mensaje : null}
                  onEditar={() => {
                    setAviso(null);
                    setErrorGuardar(null);
                    setPendiente(false);
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
        </section>

        {/* Debajo, «Nuevo envío» o la edición en línea. */}
        {!soloLectura &&
          (borrador ? (
            <Formulario borrador={borrador} onCambio={setBorrador} onPendiente={setPendiente} />
          ) : (
            <button
              ref={nuevoRef}
              type="button"
              className={`${BOTON_PRIMARIO_CLS} self-start`}
              // Sin la lista cargada no se ofrece crear: se podría duplicar un envío que ya existe sin verlo.
              disabled={todas === null}
              onClick={() => {
                setAviso(null);
                setErrorGuardar(null);
                setPendiente(false);
                setBorrador({ ...BORRADOR_NUEVO, destinatarios: [] });
              }}
              data-testid={`${PREFIJO}-nuevo`}
            >
              <Icono nombre="calendario" className="h-4 w-4" />
              Nuevo envío
            </button>
          ))}
      </div>
    </Modal>
  );
}

function Formulario({
  borrador,
  onCambio,
  onPendiente,
}: {
  borrador: Borrador;
  onCambio: (b: Borrador) => void;
  onPendiente: (pendiente: boolean) => void;
}) {
  const set = (parcial: Partial<Borrador>) => onCambio({ ...borrador, ...parcial });
  // Al abrir, el botón que tenía el foco («Nuevo envío» o «Editar») desaparece: el foco va al primer control.
  const raiz = useRef<HTMLFieldSetElement>(null);
  useEffect(() => {
    raiz.current?.querySelector<HTMLElement>("select, input, textarea, button")?.focus();
  }, []);

  return (
    <fieldset
      ref={raiz}
      className="flex min-w-0 flex-col gap-4 rounded-[9px] border border-[color:var(--di-border)] p-4"
      data-testid={`${PREFIJO}-formulario`}
    >
      <legend className="px-1 text-sm font-semibold text-[color:var(--di-heading)]">{borrador.id ? "Editar envío" : "Nuevo envío"}</legend>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <label className={ETIQUETA_CLS}>
          Periodicidad
          <select
            className={INPUT_CLS}
            value={borrador.periodicidad}
            onChange={(e) => set({ periodicidad: e.target.value as Periodicidad })}
            data-testid={`${PREFIJO}-periodicidad`}
          >
            <option value="semanal">Semanal</option>
            <option value="mensual">Mensual</option>
          </select>
        </label>

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
            Día del mes (1-28)
            <select className={INPUT_CLS} value={borrador.diaMes} onChange={(e) => set({ diaMes: Number(e.target.value) })} data-testid={`${PREFIJO}-dia-mes`}>
              {DIAS_MES.map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>
          </label>
        )}

        <label className={ETIQUETA_CLS}>
          Hora (Colombia)
          <select className={INPUT_CLS} value={borrador.hora} onChange={(e) => set({ hora: e.target.value })} data-testid={`${PREFIJO}-hora`}>
            {opcionesHora(borrador.horaOriginal).map((h) => (
              <option key={h} value={h}>
                {h}
              </option>
            ))}
          </select>
        </label>

        <div className={ETIQUETA_CLS}>
          <span>Formato</span>
          <p
            className="flex h-10 items-center rounded-[9px] border border-[color:var(--di-border)] bg-secondary px-3 text-sm font-normal text-flit-primary"
            data-testid={`${PREFIJO}-formato`}
          >
            Excel
          </p>
          <span className="font-normal">El resultado va como Excel adjunto.</span>
        </div>
      </div>

      <Destinatarios valor={borrador.destinatarios} onCambio={(destinatarios) => set({ destinatarios })} onPendiente={onPendiente} />

      <label className="flex min-h-9 items-center gap-2 text-sm text-flit-primary">
        <input
          type="checkbox"
          checked={borrador.activa}
          onChange={(e) => set({ activa: e.target.checked })}
          className="h-4 w-4 accent-flit-brand"
          data-testid={`${PREFIJO}-activo`}
        />
        Activo
      </label>
    </fieldset>
  );
}

/**
 * Destinatarios en fichas, como `RecipientsInput` de Trámites: Enter, coma o salir del campo agregan; Backspace en
 * vacío quita la última. Además acepta pegar una lista entera (de Excel o de un correo) y nombra los inválidos.
 */
function Destinatarios({
  valor,
  onCambio,
  onPendiente,
}: {
  valor: string[];
  onCambio: (v: string[]) => void;
  onPendiente: (pendiente: boolean) => void;
}) {
  const [borrador, setBorrador] = useState("");
  // Al desmontarse (cerrar, cancelar o guardar) no deja Guardar bloqueado para la próxima vez.
  const pendienteRef = useRef(onPendiente);
  useEffect(() => {
    pendienteRef.current = onPendiente;
  }, [onPendiente]);
  useEffect(() => () => pendienteRef.current(false), []);
  const [error, setError] = useState<string | null>(null);
  const id = useId();

  function agregar(texto: string) {
    const { validos, invalidos } = separarCorreos(texto);
    const nuevos = validos.filter((c) => !valor.includes(c));
    const cupo = MAX_DESTINATARIOS - valor.length;
    const entran = nuevos.slice(0, Math.max(0, cupo));
    if (entran.length > 0) onCambio([...valor, ...entran]);
    const avisos: string[] = [];
    if (invalidos.length > 0)
      avisos.push(`${invalidos.length === 1 ? "Este correo no es válido" : "Estos correos no son válidos"}: ${invalidos.join(", ")}.`);
    if (nuevos.length > entran.length) avisos.push(`No puedes indicar más de ${MAX_DESTINATARIOS} destinatarios.`);
    setError(avisos.length > 0 ? avisos.join(" ") : null);
    // Lo inválido se queda en el campo para corregirlo, sin volver a escribir lo bueno; mientras tanto, Guardar no.
    setBorrador(invalidos.join(", "));
    onPendiente(invalidos.length > 0);
  }

  return (
    <div className="flex min-w-0 flex-col gap-1">
      <label htmlFor={id} className={ETIQUETA_CLS}>
        Destinatarios
      </label>
      <div
        className={`flex min-h-[42px] flex-wrap items-center gap-1.5 rounded-[9px] border bg-card px-2 py-1.5 ${error ? "border-destructive" : "border-input"}`}
        data-testid={`${PREFIJO}-destinatarios`}
      >
        {valor.map((correo) => (
          <span
            key={correo}
            data-testid={`${PREFIJO}-chip`}
            className="flex max-w-full items-center gap-1 rounded-full bg-[var(--badge-info-bg)] py-1 pl-2.5 pr-1 text-xs font-medium text-[color:var(--badge-info-fg)]"
          >
            <span className="break-all">{correo}</span>
            <button
              type="button"
              aria-label={`Quitar ${correo}`}
              onClick={() => onCambio(valor.filter((c) => c !== correo))}
              className="grid h-7 w-7 shrink-0 place-items-center rounded-full hover:bg-[color:var(--di-accent-soft)]"
            >
              <Icono nombre="cerrar" className="h-3 w-3" />
            </button>
          </span>
        ))}
        <input
          id={id}
          type="text"
          inputMode="email"
          value={borrador}
          aria-invalid={error ? true : undefined}
          aria-describedby={`${id}-ayuda${error ? ` ${id}-error` : ""}`}
          placeholder={valor.length === 0 ? "correo@empresa.com" : ""}
          className="min-w-[10rem] flex-1 bg-transparent py-1 text-base text-flit-primary outline-none placeholder:text-muted-foreground sm:text-sm"
          onChange={(e) => {
            setError(null);
            onPendiente(false);
            if (e.target.value.endsWith(",")) agregar(e.target.value);
            else setBorrador(e.target.value);
          }}
          onPaste={(e) => {
            const pegado = e.clipboardData.getData("text");
            if (!/[\s,;]/.test(pegado.trim())) return;
            e.preventDefault();
            agregar(`${borrador} ${pegado}`);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              agregar(borrador);
            } else if (e.key === "Backspace" && !borrador && valor.length > 0) {
              onCambio(valor.slice(0, -1));
            }
          }}
          onBlur={() => {
            if (borrador.trim()) agregar(borrador);
          }}
          data-testid={`${PREFIJO}-destinatarios-input`}
        />
      </div>
      <p id={`${id}-ayuda`} className="text-[11px] text-muted-foreground">
        Presiona Enter o coma para agregar. Máximo {MAX_DESTINATARIOS} destinatarios.
      </p>
      {error && (
        <p id={`${id}-error`} role="alert" className="text-xs font-medium text-destructive" data-testid={`${PREFIJO}-correos-invalidos`}>
          {error}
        </p>
      )}
    </div>
  );
}

function Tarjeta({
  p,
  confirmando,
  ocupada,
  bloqueada,
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
  /** Hay un envío abierto en el formulario: las demás tarjetas no se tocan mientras tanto. */
  bloqueada: boolean;
  /** SuperAdmin: el estado va como texto y no hay acciones. */
  soloLectura: boolean;
  error: string | null;
  onEditar: () => void;
  onPausar: (activa: boolean) => void;
  onPedirBorrado: () => void;
  onCancelarBorrado: () => void;
  onBorrar: () => void;
}) {
  const frecuencia = describirFrecuencia(p);
  const inactiva = ocupada || bloqueada;
  return (
    <li className="flex flex-col gap-2 rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-4" data-testid={`${PREFIJO}-envio-${p.id}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-sm font-semibold text-[color:var(--di-heading)]">{frecuencia}</p>
          <p className="break-all text-xs text-[color:var(--di-text-tertiary)]">{p.destinatarios.join(", ")}</p>
        </div>
        {soloLectura ? (
          // En solo lectura el estado es texto: una casilla deshabilitada se lee como algo que se podría marcar.
          <span className="text-xs font-semibold text-muted-foreground" data-testid={`${PREFIJO}-estado-${p.id}`}>
            {p.activa ? "Activo" : "En pausa"}
          </span>
        ) : (
          <label className="flex min-h-9 items-center gap-2 text-xs font-semibold text-flit-primary">
            <input
              type="checkbox"
              checked={p.activa}
              disabled={inactiva}
              onChange={(e) => onPausar(e.target.checked)}
              aria-label={`Envío activo: ${frecuencia}`}
              className="h-4 w-4 accent-flit-brand"
              data-testid={`${PREFIJO}-activo-${p.id}`}
            />
            {p.activa ? "Activo" : "En pausa"}
          </label>
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
          // Escape cancela la confirmación, no cierra el modal entero.
          onKeyDown={(e) => {
            if (e.key !== "Escape") return;
            // Corta la propagación antes del `keydown` de `document` donde escucha la trampa de foco del Modal.
            e.stopPropagation();
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
          <button
            type="button"
            className="min-h-9 rounded-[9px] px-3 text-xs font-semibold text-[color:var(--di-action)] underline underline-offset-[3px] disabled:opacity-50"
            onClick={onEditar}
            disabled={inactiva}
            aria-label={`Editar el envío ${frecuencia.toLowerCase()}`}
            data-testid={`${PREFIJO}-editar-${p.id}`}
          >
            Editar
          </button>
          <button
            type="button"
            className="min-h-9 rounded-[9px] px-3 text-xs font-semibold text-muted-foreground hover:text-destructive disabled:opacity-50"
            onClick={onPedirBorrado}
            disabled={inactiva}
            aria-label={`Borrar el envío ${frecuencia.toLowerCase()}`}
            data-testid={`${PREFIJO}-borrar-${p.id}`}
          >
            Borrar
          </button>
        </div>
      )}
    </li>
  );
}
