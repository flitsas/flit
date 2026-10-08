"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { CarLoader } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { SwitchToggle } from "@/components/ui/SwitchToggle";
import {
  CAMPOS_PARAMETROS_MOTOR,
  ETIQUETAS_PARAMETROS_MOTOR,
  describirLimite,
  getParametrosMotorLote,
  interpretarErrorParametrosMotor,
  putParametrosMotorLote,
  validarParametrosMotor,
  type CampoParametroMotor,
  type ErrorParametrosMotor,
  type ParametrosMotorLote,
} from "@/lib/api/admin-plataforma-consolidado-lotes";
import { formatFechaHora } from "@/lib/format/date";

type Estado = "loading" | "error" | "ready";
type Valores = Record<CampoParametroMotor, string>;
type Errores = Partial<Record<CampoParametroMotor, string>>;

interface Borrador {
  valores: Valores;
  isActive: boolean;
}

/** Qué controla cada campo (la parte del rango sale de `limites`). */
const AYUDAS: Record<CampoParametroMotor, string> = {
  maxItemsPerBatch: "Una selección resuelta por encima de este número no crea el lote (422 «seleccion_excede_tope»).",
  maxPdfsPerPart: "Cuántos PDF lleva como máximo cada parte ZIP.",
  maxMbPerPart: "MB de PDF en claro que admite cada parte ZIP.",
  itemSlots: "Trámites que el motor genera a la vez en cada instancia.",
  itemTimeoutSeconds: "Segundos que puede tardar la generación de un trámite.",
  itemLeaseSeconds: "Segundos que un trámite queda reservado por quien lo procesa.",
  maxItemAttempts: "Intentos por trámite antes de omitirlo.",
  retryDelaySeconds: "Segundos de espera entre intentos de un trámite.",
  partTimeoutSeconds: "Segundos que puede tardar el empaquetado de una parte.",
  partLeaseSeconds: "Segundos que una parte queda reservada por quien la empaqueta.",
  maxPartAttempts: "Intentos por parte antes de marcarla fallida.",
  retentionHours: "Horas que se conservan las partes después de terminar el lote.",
};

const UNIDADES: Record<CampoParametroMotor, string> = {
  maxItemsPerBatch: "trámites",
  maxPdfsPerPart: "PDF",
  maxMbPerPart: "MB",
  itemSlots: "carriles",
  itemTimeoutSeconds: "s",
  itemLeaseSeconds: "s",
  maxItemAttempts: "intentos",
  retryDelaySeconds: "s",
  partTimeoutSeconds: "s",
  partLeaseSeconds: "s",
  maxPartAttempts: "intentos",
  retentionHours: "h",
};

const GRUPOS: { titulo: string; campos: CampoParametroMotor[] }[] = [
  { titulo: "Lote y partes", campos: ["maxItemsPerBatch", "maxPdfsPerPart", "maxMbPerPart", "retentionHours"] },
  {
    titulo: "Trámites",
    campos: ["itemSlots", "itemTimeoutSeconds", "itemLeaseSeconds", "maxItemAttempts", "retryDelaySeconds"],
  },
  { titulo: "Partes", campos: ["partTimeoutSeconds", "partLeaseSeconds", "maxPartAttempts"] },
];

export const AVISO_MOTOR_APAGADO =
  "Con el motor apagado no se crean lotes nuevos (las solicitudes reciben 503 «motor_inactivo») y los lotes en curso quedan en pausa hasta encenderlo.";

const idDe = (campo: CampoParametroMotor) => `parametros-motor-${campo}`;

function aBorrador(p: ParametrosMotorLote): Borrador {
  return {
    valores: Object.fromEntries(CAMPOS_PARAMETROS_MOTOR.map((c) => [c, String(p[c])])) as Valores,
    isActive: p.isActive,
  };
}

function iguales(a: Borrador, b: Borrador): boolean {
  return a.isActive === b.isActive && CAMPOS_PARAMETROS_MOTOR.every((c) => a.valores[c] === b.valores[c]);
}

const primerCampoConError = (errores: Errores) => CAMPOS_PARAMETROS_MOTOR.find((c) => errores[c]);

/**
 * HU #13420 — Super Admin → Plataforma → Descarga masiva. Ver y editar los parámetros del motor de
 * lotes de consolidados. Valida con los `limites` del GET antes del PUT (AC3), pinta el 400 junto
 * al campo, ante el 409 conserva lo escrito y ofrece recargar la versión vigente (AC4) y advierte
 * qué pasa al apagar el motor (AC6). Estados cargando / error / lleno (AC7).
 */
export function ParametrosMotorLotePanel() {
  const [estado, setEstado] = useState<Estado>("loading");
  const [errorCarga, setErrorCarga] = useState<ErrorParametrosMotor | null>(null);
  const [persistido, setPersistido] = useState<ParametrosMotorLote | null>(null);
  const [borrador, setBorrador] = useState<Borrador | null>(null);
  const [errores, setErrores] = useState<Errores>({});
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [conflicto, setConflicto] = useState(false);
  const [guardando, setGuardando] = useState(false);
  const [recargando, setRecargando] = useState(false);
  const [resultado, setResultado] = useState("");
  /** Campo al que mover el foco tras pintar errores (contador para repetir el mismo campo). */
  const [foco, setFoco] = useState<{ campo: CampoParametroMotor; n: number } | null>(null);

  const cargar = useCallback(async (signal?: AbortSignal) => {
    setEstado("loading");
    try {
      const p = await getParametrosMotorLote(signal);
      if (signal?.aborted) return;
      setPersistido(p);
      setBorrador(aBorrador(p));
      setErrores({});
      setErrorGeneral(null);
      setConflicto(false);
      setEstado("ready");
    } catch (err) {
      if (signal?.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
      setErrorCarga(interpretarErrorParametrosMotor(err));
      setEstado("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API (mismo patrón que ConfirmacionRuntConfigPanel)
    void cargar(controller.signal);
    return () => controller.abort();
  }, [cargar]);

  useEffect(() => {
    if (foco) document.getElementById(idDe(foco.campo))?.focus();
  }, [foco]);

  const sucio = useMemo(
    () => persistido !== null && borrador !== null && !iguales(aBorrador(persistido), borrador),
    [persistido, borrador],
  );

  const marcarErrores = (nuevos: Errores) => {
    setErrores(nuevos);
    const primero = primerCampoConError(nuevos);
    if (primero) setFoco((f) => ({ campo: primero, n: (f?.n ?? 0) + 1 }));
  };

  const setValor = (campo: CampoParametroMotor, valor: string) => {
    setBorrador((b) => (b ? { ...b, valores: { ...b.valores, [campo]: valor } } : b));
    setErrores((e) => (e[campo] ? { ...e, [campo]: undefined } : e));
  };

  const descartar = () => {
    if (persistido) setBorrador(aBorrador(persistido));
    setErrores({});
    setErrorGeneral(null);
    setResultado("");
  };

  /** AC4 — trae la versión vigente (rowVersion, autor) sin tocar lo que el usuario escribió. */
  const recargarConservando = async () => {
    setRecargando(true);
    try {
      const p = await getParametrosMotorLote();
      setPersistido(p);
      setConflicto(false);
      setResultado(
        "Se cargó la versión vigente de los parámetros. Tus valores se conservaron: revísalos y guarda de nuevo.",
      );
    } catch (err) {
      setResultado(interpretarErrorParametrosMotor(err).mensaje);
    } finally {
      setRecargando(false);
    }
  };

  const guardar = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!borrador || !persistido) return;
    setResultado("");
    setErrorGeneral(null);

    const locales = validarParametrosMotor(borrador.valores, persistido.limites);
    if (primerCampoConError(locales)) {
      marcarErrores(locales);
      setResultado("No se guardó: revisa los campos marcados.");
      return;
    }

    setGuardando(true);
    setErrores({});
    try {
      const numeros = Object.fromEntries(
        CAMPOS_PARAMETROS_MOTOR.map((c) => [c, Number(borrador.valores[c].trim())]),
      ) as Record<CampoParametroMotor, number>;
      const guardado = await putParametrosMotorLote({
        ...numeros,
        isActive: borrador.isActive,
        rowVersion: persistido.rowVersion,
      });
      setPersistido(guardado);
      setBorrador(aBorrador(guardado));
      setConflicto(false);
      setResultado(
        guardado.isActive
          ? "Parámetros guardados. El motor los aplica en su siguiente ciclo."
          : `Parámetros guardados. ${AVISO_MOTOR_APAGADO}`,
      );
    } catch (err) {
      const r = interpretarErrorParametrosMotor(err);
      if (r.tipo === "invalido") {
        marcarErrores(r.errores);
        setErrorGeneral(r.general);
        setResultado(`No se guardó: ${r.mensaje.toLowerCase()}`);
      } else if (r.tipo === "conflicto") {
        setConflicto(true);
        setResultado("No se guardó: otro Super Admin cambió los parámetros.");
      } else {
        setErrorGeneral(r.mensaje);
        setResultado(`No se guardó. ${r.mensaje}`);
      }
    } finally {
      setGuardando(false);
    }
  };

  if (estado === "loading") {
    return (
      <div className="py-16" data-testid="parametros-motor-loading">
        <CarLoader label="Cargando los parámetros del motor…" />
      </div>
    );
  }

  if (estado === "error" || !borrador || !persistido) {
    const r = errorCarga ?? interpretarErrorParametrosMotor(null);
    const titulo =
      r.tipo === "no_encontrado"
        ? "Parámetros no encontrados"
        : r.tipo === "sin_permiso"
          ? "Sin permiso"
          : "No se pudieron cargar los parámetros";
    return (
      <InlineAlert
        tone="error"
        title={titulo}
        action={
          r.tipo === "sin_permiso" ? undefined : (
            <button
              type="button"
              onClick={() => void cargar()}
              className="rounded-md text-xs font-semibold underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
            >
              Reintentar
            </button>
          )
        }
      >
        {r.mensaje}
      </InlineAlert>
    );
  }

  const ocupado = guardando || recargando;

  const campo = (c: CampoParametroMotor) => {
    const id = idDe(c);
    const error = errores[c];
    const limite = persistido.limites.find((l) => l.campo === c);
    const rango = describirLimite(limite);
    return (
      <div key={c} className="flex flex-col gap-1">
        <label htmlFor={id} className="text-xs font-semibold text-flit-primary dark:text-white">
          {ETIQUETAS_PARAMETROS_MOTOR[c]}
        </label>
        <div
          className={`flex items-center rounded-xl border bg-card ${
            error ? "border-[color:var(--badge-danger-fg)]" : "border-border"
          }`}
        >
          <input
            id={id}
            name={c}
            type="number"
            inputMode="numeric"
            step={1}
            min={limite?.minimo ?? undefined}
            max={limite?.maximo ?? undefined}
            value={borrador.valores[c]}
            onChange={(e) => setValor(c, e.target.value)}
            disabled={ocupado}
            aria-invalid={error ? true : undefined}
            aria-describedby={`${id}-hint${error ? ` ${id}-error` : ""}`}
            className="w-full min-w-0 rounded-xl bg-transparent px-3 py-2 font-mono text-sm text-flit-primary outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50 dark:text-white"
          />
          <span className="shrink-0 pr-3 text-[11px] text-muted-foreground" aria-hidden="true">
            {UNIDADES[c]}
          </span>
        </div>
        <p id={`${id}-hint`} className="text-[11px] leading-snug text-muted-foreground">
          {AYUDAS[c]}
          {rango ? ` ${rango}` : ""}
        </p>
        {error ? (
          <p id={`${id}-error`} className="text-[11px] font-semibold text-[color:var(--badge-danger-fg)]">
            {error}
          </p>
        ) : null}
      </div>
    );
  };

  return (
    <form
      onSubmit={(e) => void guardar(e)}
      noValidate
      aria-label="Parámetros del motor de descarga masiva"
      className="flex flex-col gap-5"
      data-testid="parametros-motor-form"
    >
      <div className="flex items-start justify-between gap-4 rounded-2xl border border-border bg-card p-4">
        <div className="flex flex-col gap-0.5">
          <label htmlFor="parametros-motor-isActive" className="text-sm font-semibold text-flit-primary dark:text-white">
            Motor encendido
          </label>
          <p id="parametros-motor-isActive-hint" className="text-[11px] leading-snug text-muted-foreground">
            Encendido, el motor crea y procesa lotes de descarga masiva. Apagado, no se crean lotes nuevos y los que
            están en curso esperan.
          </p>
        </div>
        <SwitchToggle
          id="parametros-motor-isActive"
          checked={borrador.isActive}
          onChange={(v) => setBorrador((b) => (b ? { ...b, isActive: v } : b))}
          label="Motor encendido"
          disabled={ocupado}
          describedById="parametros-motor-isActive-hint"
        />
      </div>

      {!borrador.isActive ? (
        <div data-testid="parametros-motor-aviso-apagado">
          <InlineAlert tone="warning" title="Motor apagado">
            {AVISO_MOTOR_APAGADO}
          </InlineAlert>
        </div>
      ) : null}

      {conflicto ? (
        <div data-testid="parametros-motor-conflicto">
          <InlineAlert
            tone="warning"
            title="Los parámetros cambiaron"
            action={
              <button
                type="button"
                onClick={() => void recargarConservando()}
                disabled={recargando}
                className="rounded-md text-xs font-semibold underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50"
              >
                {recargando ? "Recargando…" : "Recargar sin perder lo escrito"}
              </button>
            }
          >
            Otro Super Admin guardó los parámetros después de que los cargaste. Recarga para traer la versión vigente: lo
            que escribiste se conserva.
          </InlineAlert>
        </div>
      ) : null}

      {GRUPOS.map((g) => (
        <fieldset key={g.titulo} className="rounded-2xl border border-border bg-card p-4">
          <legend className="px-1 text-sm font-semibold text-flit-primary dark:text-white">{g.titulo}</legend>
          <div className="mt-2 grid gap-x-5 gap-y-4 [grid-template-columns:repeat(auto-fit,minmax(220px,1fr))]">
            {g.campos.map(campo)}
          </div>
        </fieldset>
      ))}

      {errorGeneral ? (
        <InlineAlert tone="error" title="No se guardó">
          {errorGeneral}
        </InlineAlert>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <button
          type="submit"
          disabled={ocupado || !sucio}
          className="rounded-full bg-gradient-to-r from-flit-tech to-flit-brand px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-50"
        >
          {guardando ? "Guardando…" : "Guardar parámetros"}
        </button>
        <button
          type="button"
          onClick={descartar}
          disabled={ocupado || !sucio}
          className="rounded-full border border-border px-4 py-2 text-xs font-semibold text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50 dark:text-white"
        >
          Descartar cambios
        </button>
        <span data-testid="parametros-motor-ultimo-cambio" className="text-[11px] text-muted-foreground">
          {persistido.updatedAt
            ? `Último cambio: ${persistido.updatedByName ?? "usuario no disponible"} · ${formatFechaHora(persistido.updatedAt)}`
            : "Sin cambios desde la configuración inicial."}
        </span>
      </div>

      <p
        data-testid="parametros-motor-resultado"
        role="status"
        aria-live="polite"
        className="text-xs font-semibold text-flit-primary dark:text-white"
      >
        {resultado}
      </p>
    </form>
  );
}
