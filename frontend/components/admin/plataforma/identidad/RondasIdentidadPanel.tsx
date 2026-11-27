"use client";

import { buttonClass } from "@flit/ui/Button";
import { Icono } from "@flit/ui/Icono";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { OptionCard } from "@/components/admin/companies/ConfigUi";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { CarLoader } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { useToast } from "@/components/admin/Toast";
import {
  MAX_CORREOS,
  RONDAS_MAX,
  UMBRAL_MAX,
  UMBRAL_MIN,
  UMBRAL_POR_DEFECTO,
  RONDAS_MIN,
  getIdentityRoundSettings,
  interpretarErrorRondas,
  normalizarCorreo,
  putIdentityRoundSettings,
  validarNuevoCorreo,
  validarRondas,
  type CampoRondas,
  type DisparadorAlerta,
  type ErrorRondas,
  type IdentityRoundSettings,
} from "@/lib/api/admin-identity-round-settings";

type Estado = "loading" | "error" | "ready";

interface Borrador {
  rondas: string;
  disparador: DisparadorAlerta;
  correos: string[];
  limite: boolean;
  umbral: string;
}

type Errores = Partial<Record<CampoRondas, string>>;

export const AVISO_DATOS_COMPLETOS =
  "La alerta lleva los datos completos de la persona (nombre, documento y contacto) porque es para operación. Agrega solo correos de personal autorizado.";

const DISPARADORES: { valor: DisparadorAlerta; etiqueta: string; ayuda: string }[] = [
  { valor: "al_maximo", etiqueta: "Al llegar al máximo", ayuda: "Se avisa una sola vez, cuando se agotan las rondas." },
  { valor: "cada_ronda", etiqueta: "Cada ronda rechazada", ayuda: "Se avisa cada vez que una ronda es rechazada." },
];

function aBorrador(s: IdentityRoundSettings): Borrador {
  return {
    rondas: String(s.rondasMaximas),
    disparador: s.disparadorAlerta,
    correos: s.correosAlerta.map(normalizarCorreo),
    limite: s.limiteActivo ?? true,
    umbral: String(s.umbralAlertaSoporte ?? UMBRAL_POR_DEFECTO),
  };
}

function iguales(a: Borrador, b: Borrador): boolean {
  return (
    a.rondas.trim() === b.rondas.trim() &&
    a.disparador === b.disparador &&
    a.limite === b.limite &&
    a.umbral.trim() === b.umbral.trim() &&
    a.correos.length === b.correos.length &&
    a.correos.every((c, i) => c === b.correos[i])
  );
}

/**
 * SuperAdmin — configuración GLOBAL de rondas de validación de identidad: rondas máximas (1–10),
 * disparador de la alerta y correos de operación (máx. 20, sin duplicados). «Guardar» solo se
 * habilita con cambios válidos; los 400 del servidor se muestran junto al campo.
 */
export function RondasIdentidadPanel() {
  const [estado, setEstado] = useState<Estado>("loading");
  const [errorCarga, setErrorCarga] = useState<ErrorRondas | null>(null);
  const [persistido, setPersistido] = useState<IdentityRoundSettings | null>(null);
  const [borrador, setBorrador] = useState<Borrador | null>(null);
  const [errores, setErrores] = useState<Errores>({});
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [nuevoCorreo, setNuevoCorreo] = useState("");
  const [errorNuevo, setErrorNuevo] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const { show } = useToast();
  const rondasRef = useRef<HTMLInputElement>(null);
  const correoRef = useRef<HTMLInputElement>(null);

  const cargar = useCallback(async (signal?: AbortSignal) => {
    setEstado("loading");
    try {
      const s = await getIdentityRoundSettings(signal);
      if (signal?.aborted) return;
      setPersistido(s);
      setBorrador(aBorrador(s));
      setErrores({});
      setErrorGeneral(null);
      setEstado("ready");
    } catch (err) {
      if (signal?.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
      setErrorCarga(interpretarErrorRondas(err));
      setEstado("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API (mismo patrón que ParametrosMotorLotePanel)
    void cargar(controller.signal);
    return () => controller.abort();
  }, [cargar]);

  const errorRondasLocal = borrador ? validarRondas(borrador.rondas) : null;
  const errorRondas = errores.rondasMaximas ?? errorRondasLocal;

  const sucio = useMemo(
    () => persistido !== null && borrador !== null && !iguales(aBorrador(persistido), borrador),
    [persistido, borrador],
  );
  const puedeGuardar = sucio && !errorRondasLocal && !guardando;

  const cambiarRondas = (v: string) => {
    setBorrador((b) => (b ? { ...b, rondas: v } : b));
    setErrores((e) => ({ ...e, rondasMaximas: undefined }));
  };

  const agregarCorreo = () => {
    if (!borrador) return;
    const err = validarNuevoCorreo(nuevoCorreo, borrador.correos);
    if (err) {
      setErrorNuevo(err);
      correoRef.current?.focus();
      return;
    }
    setBorrador({ ...borrador, correos: [...borrador.correos, normalizarCorreo(nuevoCorreo)] });
    setNuevoCorreo("");
    setErrorNuevo(null);
    setErrores((e) => ({ ...e, correosAlerta: undefined }));
    correoRef.current?.focus();
  };

  const quitarCorreo = (correo: string) => {
    setBorrador((b) => (b ? { ...b, correos: b.correos.filter((c) => c !== correo) } : b));
    setErrores((e) => ({ ...e, correosAlerta: undefined }));
    correoRef.current?.focus();
  };

  const descartar = () => {
    if (persistido) setBorrador(aBorrador(persistido));
    setErrores({});
    setErrorGeneral(null);
    setNuevoCorreo("");
    setErrorNuevo(null);
  };

  const guardar = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!borrador || !puedeGuardar) return;
    setErrorGeneral(null);
    setErrores({});
    setGuardando(true);
    try {
      const guardado = await putIdentityRoundSettings({
        rondasMaximas: Number(borrador.rondas.trim()),
        disparadorAlerta: borrador.disparador,
        correosAlerta: borrador.correos,
        limiteActivo: borrador.limite,
        umbralAlertaSoporte: Number(borrador.umbral.trim()),
      });
      setPersistido(guardado);
      setBorrador(aBorrador(guardado));
      show("Configuración guardada. Aplica a las próximas validaciones de identidad.", "success");
    } catch (err) {
      const r = interpretarErrorRondas(err);
      if (r.tipo === "invalido") {
        setErrores(r.errores);
        setErrorGeneral(r.general);
        show("No se guardó: revisa los campos marcados.", "error");
        if (r.errores.rondasMaximas) rondasRef.current?.focus();
        else if (r.errores.correosAlerta) correoRef.current?.focus();
      } else {
        setErrorGeneral(r.mensaje);
        show(`No se guardó. ${r.mensaje}`, "error");
      }
    } finally {
      setGuardando(false);
    }
  };

  if (estado === "loading") {
    return (
      <div className="py-16" data-testid="rondas-identidad-loading">
        <CarLoader label="Cargando la configuración de rondas…" />
      </div>
    );
  }

  if (estado === "error" || !borrador || !persistido) {
    const r = errorCarga ?? interpretarErrorRondas(null);
    return (
      <InlineAlert
        tone="error"
        title={r.tipo === "sin_permiso" ? "Sin permiso" : "No se pudo cargar la configuración"}
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

  const errorCorreos = errores.correosAlerta;
  const llena = borrador.correos.length >= MAX_CORREOS;

  return (
    <form
      onSubmit={(e) => void guardar(e)}
      noValidate
      aria-label="Rondas de validación de identidad"
      className="flex flex-col gap-5"
      data-testid="rondas-identidad-form"
    >
      <div className="rounded-2xl border border-border bg-card p-4">
        <label htmlFor="rondas-maximas" className="text-sm font-semibold text-flit-primary">
          Rondas máximas
        </label>
        <p id="rondas-maximas-hint" className="mt-0.5 text-xs leading-snug text-muted-foreground">
          Cuántas veces se puede repetir la validación de identidad de una persona antes de pasar a operación. Entre{" "}
          {RONDAS_MIN} y {RONDAS_MAX}.
        </p>
        <input
          ref={rondasRef}
          id="rondas-maximas"
          name="rondasMaximas"
          type="number"
          inputMode="numeric"
          step={1}
          min={RONDAS_MIN}
          max={RONDAS_MAX}
          value={borrador.rondas}
          onChange={(e) => cambiarRondas(e.target.value)}
          disabled={guardando}
          aria-invalid={errorRondas ? true : undefined}
          aria-describedby={`rondas-maximas-hint${errorRondas ? " rondas-maximas-error" : ""}`}
          className={`mt-2 w-28 rounded-xl border bg-card px-3 py-2 font-mono text-sm text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50 ${
            errorRondas ? "border-[color:var(--badge-danger-fg)]" : "border-border"
          }`}
        />
        {errorRondas ? (
          <p id="rondas-maximas-error" role="alert" className="mt-1 text-xs font-semibold text-[color:var(--badge-danger-fg)]">
            {errorRondas}
          </p>
        ) : null}
      </div>

      <div data-testid="rondas-identidad-limite">
        <ToggleSwitch
          id="limite-rondas-activo"
          label="Límite de rondas"
          description={
            borrador.limite
              ? "Cuando una persona llega al máximo de rondas, no puede pedir otra validación de identidad."
              : "Sin límite: ninguna validación se bloquea por rondas. Operación recibe una alerta cada cierto número de validaciones de la persona."
          }
          hint="Por defecto: activado."
          state={{ on: "Activo", off: "Sin límite", onTone: "success", offTone: "warning" }}
          checked={borrador.limite}
          disabled={guardando}
          onChange={(checked) => {
            setBorrador((b) => (b ? { ...b, limite: checked } : b));
          }}
        />
        {!borrador.limite ? (
          <div className="mt-3 flex flex-col gap-1">
            <label htmlFor="umbral-alerta" className="text-xs font-semibold text-flit-primary">
              Avisar a operación cada
            </label>
            <input
              id="umbral-alerta"
              name="umbralAlertaSoporte"
              type="number"
              inputMode="numeric"
              step={1}
              min={UMBRAL_MIN}
              max={UMBRAL_MAX}
              value={borrador.umbral}
              onChange={(e) => {
                setBorrador((b) => (b ? { ...b, umbral: e.target.value } : b));
              }}
              disabled={guardando}
              aria-invalid={errores.umbralAlertaSoporte ? true : undefined}
              className="w-28 rounded-xl border border-border bg-card px-3 py-2 font-mono text-sm text-flit-primary"
            />
            <span className="text-xs text-muted-foreground">validaciones de la misma persona ({UMBRAL_MIN} a {UMBRAL_MAX}).</span>
            {errores.umbralAlertaSoporte ? (
              <p role="alert" className="text-xs font-semibold text-[color:var(--badge-danger-fg)]">
                {errores.umbralAlertaSoporte}
              </p>
            ) : null}
          </div>
        ) : null}
      </div>

      <fieldset className="rounded-2xl border border-border bg-card p-4" aria-describedby="disparador-hint">
        <legend className="px-1 text-sm font-semibold text-flit-primary">Cuándo avisar a operación</legend>
        <p id="disparador-hint" className="mt-0.5 text-xs leading-snug text-muted-foreground">
          Define en qué momento se envía la alerta a los correos de operación.
        </p>
        <div className="mt-3 grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label="Cuándo avisar a operación">
          {DISPARADORES.map((d) => (
            <OptionCard
              key={d.valor}
              type="radio"
              name="disparadorAlerta"
              label={d.etiqueta}
              description={d.ayuda}
              checked={borrador.disparador === d.valor}
              disabled={guardando}
              onChange={() => {
                setBorrador((b) => (b ? { ...b, disparador: d.valor } : b));
              }}
            />
          ))}
        </div>
        {errores.disparadorAlerta ? (
          <p role="alert" className="mt-2 text-xs font-semibold text-[color:var(--badge-danger-fg)]">
            {errores.disparadorAlerta}
          </p>
        ) : null}
      </fieldset>

      <div className="rounded-2xl border border-border bg-card p-4">
        <h2 className="text-sm font-semibold text-flit-primary">Correos de operación</h2>
        <p id="correos-hint" className="mt-0.5 text-xs leading-snug text-muted-foreground">
          Reciben la alerta. Máximo {MAX_CORREOS}, sin repetir.
        </p>

        <div className="mt-3" data-testid="rondas-identidad-aviso-datos">
          <InlineAlert tone="info" vivo={false} title="La alerta incluye datos personales completos">
            {AVISO_DATOS_COMPLETOS}
          </InlineAlert>
        </div>

        <div className="mt-3 flex flex-col gap-1">
          <label htmlFor="correo-nuevo" className="text-xs font-semibold text-flit-primary">
            Agregar correo
          </label>
          <div className="flex flex-wrap items-start gap-2">
            <input
              ref={correoRef}
              id="correo-nuevo"
              type="email"
              autoComplete="off"
              value={nuevoCorreo}
              onChange={(e) => {
                setNuevoCorreo(e.target.value);
                setErrorNuevo(null);
              }}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  agregarCorreo();
                }
              }}
              disabled={guardando || llena}
              aria-invalid={errorNuevo || errorCorreos ? true : undefined}
              aria-describedby={`correos-hint${errorNuevo ? " correo-nuevo-error" : ""}${errorCorreos ? " correos-error" : ""}`}
              placeholder="operacion@empresa.com"
              className={`min-w-[16rem] flex-1 rounded-xl border bg-card px-3 py-2 text-sm text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50 ${
                errorNuevo || errorCorreos ? "border-[color:var(--badge-danger-fg)]" : "border-border"
              }`}
            />
            <button
              type="button"
              onClick={agregarCorreo}
              disabled={guardando || llena}
              className="rounded-full border border-border px-4 py-2 text-xs font-semibold text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50"
            >
              Agregar
            </button>
          </div>
          {errorNuevo ? (
            <p id="correo-nuevo-error" role="alert" className="text-xs font-semibold text-[color:var(--badge-danger-fg)]">
              {errorNuevo}
            </p>
          ) : null}
          {errorCorreos ? (
            <p id="correos-error" role="alert" className="text-xs font-semibold text-[color:var(--badge-danger-fg)]">
              {errorCorreos}
            </p>
          ) : null}
        </div>

        {borrador.correos.length === 0 ? (
          <p
            data-testid="rondas-identidad-vacio"
            className="mt-3 rounded-xl border border-dashed border-border px-4 py-5 text-center text-xs text-muted-foreground"
          >
            Sin correos configurados: nadie recibirá la alerta. Agrega al menos uno.
          </p>
        ) : (
          <ul aria-label="Correos configurados" className="mt-3 flex flex-col gap-2" data-testid="rondas-identidad-correos">
            {borrador.correos.map((c) => (
              <li
                key={c}
                className="flex items-center justify-between gap-3 rounded-xl border border-border px-3 py-2 text-sm text-flit-primary"
              >
                <span className="min-w-0 break-all">{c}</span>
                <button
                  type="button"
                  onClick={() => quitarCorreo(c)}
                  disabled={guardando}
                  aria-label={`Quitar ${c}`}
                  className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-muted-foreground hover:text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50"
                >
                  <Icono nombre="cerrar" className="h-4 w-4" />
                </button>
              </li>
            ))}
          </ul>
        )}
        <p className="mt-2 text-xs text-muted-foreground">
          {borrador.correos.length} de {MAX_CORREOS} correos.
        </p>
      </div>

      {errorGeneral ? (
        <InlineAlert tone="error" title="No se guardó">
          {errorGeneral}
        </InlineAlert>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <button type="submit" disabled={!puedeGuardar} className={buttonClass({ size: "sm" })}>
          {guardando ? "Guardando…" : "Guardar"}
        </button>
        <button
          type="button"
          onClick={descartar}
          disabled={guardando || !sucio}
          className="rounded-full border border-border px-4 py-2 text-xs font-semibold text-flit-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:opacity-50"
        >
          Descartar cambios
        </button>
      </div>


    </form>
  );
}
