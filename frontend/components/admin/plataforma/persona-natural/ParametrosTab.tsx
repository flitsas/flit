"use client";

// HU #14075 AC3/AC4 — Pestaña «Parámetros» del portal de Persona Natural: vigencias, topes de consulta, captcha y
// contacto de soporte. Un valor vacío significa «predeterminado del portal». Cada número se valida contra su rango
// (que se ve en el campo) y no deja guardar fuera de él. El guardado lleva la versión que se leyó: si otra persona
// cambió la configuración (409) se avisa con la opción de recargar y NO se pierde lo escrito hasta que se decida.
// Sin datos bancarios ni de pago (decisión del PO: otra épica).
import { useCallback, useEffect, useMemo, useState } from "react";
import { Loader2, RefreshCcw, Save } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { formatFechaHora } from "@/lib/format/date";
import {
  RANGOS_PARAMETROS,
  SOPORTE_CORREO_MAX,
  SOPORTE_HORARIO_MAX,
  SOPORTE_TELEFONO_MAX,
  fetchParametrosPn,
  guardarParametrosPn,
  type GuardarParametrosPn,
  type ParametrosPn,
} from "@/lib/api/admin-pn-config";
import {
  PN_FIELD_ERROR_CLS,
  PN_HINT_CLS,
  PN_INPUT_CLS,
  PN_INPUT_ERROR_CLS,
  PN_LABEL_CLS,
  PN_NOTICE_CLS,
  PN_NOTICE_STYLE,
  PN_PRIMARY_BTN_CLS,
  PN_PRIMARY_BTN_STYLE,
  PN_CANCEL_BTN_CLS,
} from "./pn-ui";
import { pnError } from "./pnErrors";

type CampoNumerico = keyof typeof RANGOS_PARAMETROS;
const CAMPOS_NUMERICOS: { campo: CampoNumerico; label: string; ayuda: string }[] = [
  { campo: "sesionVigenciaMinutos", label: "Vigencia de la sesión (minutos)", ayuda: "Cuánto dura la sesión de una persona en el portal." },
  { campo: "borradorVigenciaDias", label: "Vigencia del borrador (días)", ayuda: "Cuánto se conserva un trámite sin radicar." },
  { campo: "topeConsultasCiudadanoDia", label: "Consultas por ciudadano al día", ayuda: "Tope diario de consultas de una misma persona." },
  { campo: "topeConsultasGlobalDia", label: "Consultas globales al día", ayuda: "Tope diario de consultas de todo el portal." },
];

/** Estado del formulario: todo como texto, para conservar lo que la persona escribió aunque sea inválido. */
interface Formulario {
  sesionVigenciaMinutos: string;
  borradorVigenciaDias: string;
  topeConsultasCiudadanoDia: string;
  topeConsultasGlobalDia: string;
  captcha: "" | "si" | "no";
  soporteCorreo: string;
  soporteTelefono: string;
  soporteHorario: string;
}

function aFormulario(p: ParametrosPn): Formulario {
  return {
    sesionVigenciaMinutos: p.sesionVigenciaMinutos?.toString() ?? "",
    borradorVigenciaDias: p.borradorVigenciaDias?.toString() ?? "",
    topeConsultasCiudadanoDia: p.topeConsultasCiudadanoDia?.toString() ?? "",
    topeConsultasGlobalDia: p.topeConsultasGlobalDia?.toString() ?? "",
    captcha: p.captchaActivo === null ? "" : p.captchaActivo ? "si" : "no",
    soporteCorreo: p.soporteCorreo ?? "",
    soporteTelefono: p.soporteTelefono ?? "",
    soporteHorario: p.soporteHorario ?? "",
  };
}

const CORREO = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const TELEFONO = /^[0-9+()\- ]{7,30}$/;

type Errores = Partial<Record<keyof Formulario, string>>;

function validar(f: Formulario): { errores: Errores; payload: GuardarParametrosPn | null } {
  const errores: Errores = {};
  const numeros = {} as Record<CampoNumerico, number | null>;
  for (const { campo } of CAMPOS_NUMERICOS) {
    const r = RANGOS_PARAMETROS[campo];
    const texto = f[campo].trim();
    if (texto === "") {
      numeros[campo] = null;
      continue;
    }
    const n = Number(texto);
    if (!/^\d+$/.test(texto) || n < r.min || n > r.max) {
      errores[campo] = `Debe ser un número entero entre ${r.min} y ${r.max} ${r.unidad}.`;
    } else {
      numeros[campo] = n;
    }
  }
  const correo = f.soporteCorreo.trim();
  if (correo && (correo.length > SOPORTE_CORREO_MAX || !CORREO.test(correo))) {
    errores.soporteCorreo = "El correo de soporte no tiene un formato válido.";
  }
  const telefono = f.soporteTelefono.trim();
  if (telefono && !TELEFONO.test(telefono)) {
    errores.soporteTelefono = `Use de 7 a ${SOPORTE_TELEFONO_MAX} caracteres: dígitos, +, paréntesis, guiones y espacios.`;
  }
  if (f.soporteHorario.trim().length > SOPORTE_HORARIO_MAX) {
    errores.soporteHorario = `El horario admite hasta ${SOPORTE_HORARIO_MAX} caracteres.`;
  }
  if (Object.keys(errores).length > 0) return { errores, payload: null };
  return {
    errores,
    payload: {
      ...numeros,
      captchaActivo: f.captcha === "" ? null : f.captcha === "si",
      soporteCorreo: correo || null,
      soporteTelefono: telefono || null,
      soporteHorario: f.soporteHorario.trim() || null,
    },
  };
}

export function ParametrosTab() {
  const { show } = useToast();
  const [servidor, setServidor] = useState<ParametrosPn | null>(null);
  const [form, setForm] = useState<Formulario | null>(null);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [errores, setErrores] = useState<Errores>({});
  const [busy, setBusy] = useState(false);
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [conflicto, setConflicto] = useState<ParametrosPn | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const p = await fetchParametrosPn(signal);
      if (signal?.aborted) return;
      setServidor(p);
      setForm(aFormulario(p));
      setErrores({});
      setConflicto(null);
      setErrorGeneral(null);
      setStatus("ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const hayCambios = useMemo(
    () => servidor !== null && form !== null && JSON.stringify(aFormulario(servidor)) !== JSON.stringify(form),
    [servidor, form],
  );

  function cambiar<K extends keyof Formulario>(campo: K, valor: Formulario[K]) {
    setForm((f) => (f ? { ...f, [campo]: valor } : f));
    setErrores((e) => ({ ...e, [campo]: undefined }));
  }

  async function guardar(e: React.FormEvent) {
    e.preventDefault();
    if (!form || !servidor) return;
    const { errores: nuevos, payload } = validar(form);
    setErrores(nuevos);
    setErrorGeneral(null);
    if (!payload) return;
    setBusy(true);
    try {
      const guardado = await guardarParametrosPn(payload, servidor.rowVersion);
      setServidor(guardado);
      setForm(aFormulario(guardado));
      setConflicto(null);
      show("Parámetros guardados.", "success");
    } catch (err) {
      const info = pnError(err);
      if (info.status === 409 && info.body?.actual) {
        // No se toca el formulario: lo escrito se conserva hasta que la persona decida recargar.
        setConflicto(info.body.actual as ParametrosPn);
      } else if (info.status === 400 && typeof info.body?.campo === "string") {
        setErrores({ [info.body.campo]: info.message });
      } else {
        setErrorGeneral(info.message);
      }
    } finally {
      setBusy(false);
    }
  }

  function recargarActual() {
    if (!conflicto) return;
    setServidor(conflicto);
    setForm(aFormulario(conflicto));
    setErrores({});
    setConflicto(null);
  }

  if (status === "loading") return <CarLoaderModal label="Cargando parámetros…" />;

  return (
    <UiStateBoundary
      status={status}
      errorMessage="No se pudieron cargar los parámetros del portal."
      onRetry={() => void load()}
    >
      {form && servidor && (
        <form onSubmit={guardar} noValidate className="flex max-w-3xl flex-col gap-6" aria-label="Parámetros del portal">
          <p className="text-sm text-[#59677D] dark:text-white/78">
            Un campo vacío usa el valor predeterminado del portal.
            {servidor.actualizadoEn && <> Última actualización: {formatFechaHora(servidor.actualizadoEn)}.</>}
          </p>

          {conflicto && (
            <div role="alert" className={`${PN_NOTICE_CLS} flex flex-col gap-2`} style={PN_NOTICE_STYLE}>
              <p>
                Otra persona cambió esta configuración mientras usted editaba (versión {conflicto.rowVersion}). Lo que
                escribió se conserva en pantalla, pero para guardarlo debe recargar los valores actuales y volver a
                aplicar sus cambios.
              </p>
              <button type="button" onClick={recargarActual} className={`${PN_CANCEL_BTN_CLS} w-fit bg-white`}>
                <RefreshCcw className="mr-1.5 inline h-3.5 w-3.5" aria-hidden="true" />
                Recargar valores actuales
              </button>
            </div>
          )}

          <fieldset className="grid gap-4 sm:grid-cols-2">
            <legend className="mb-2 text-sm font-bold text-flit-primary dark:text-white">Vigencias y topes</legend>
            {CAMPOS_NUMERICOS.map(({ campo, label, ayuda }) => {
              const r = RANGOS_PARAMETROS[campo];
              const hintId = `pn-param-${campo}-hint`;
              return (
                <label key={campo} className={PN_LABEL_CLS}>
                  {label}
                  <input
                    inputMode="numeric"
                    autoComplete="off"
                    value={form[campo]}
                    onChange={(e) => cambiar(campo, e.target.value)}
                    placeholder="Predeterminado"
                    aria-invalid={Boolean(errores[campo])}
                    aria-describedby={hintId}
                    className={`${PN_INPUT_CLS} ${errores[campo] ? PN_INPUT_ERROR_CLS : ""}`}
                  />
                  <span id={hintId} className={errores[campo] ? PN_FIELD_ERROR_CLS : PN_HINT_CLS} role={errores[campo] ? "alert" : undefined}>
                    {errores[campo] ?? `${ayuda} Rango permitido: ${r.min} a ${r.max} ${r.unidad}.`}
                  </span>
                </label>
              );
            })}
            <label className={PN_LABEL_CLS}>
              Captcha
              <select value={form.captcha} onChange={(e) => cambiar("captcha", e.target.value as Formulario["captcha"])} className={PN_INPUT_CLS}>
                <option value="">Predeterminado del portal</option>
                <option value="si">Activo</option>
                <option value="no">Inactivo</option>
              </select>
              <span className={PN_HINT_CLS}>Protección contra automatizaciones en el portal.</span>
            </label>
          </fieldset>

          <fieldset className="grid gap-4 sm:grid-cols-2">
            <legend className="mb-2 text-sm font-bold text-flit-primary dark:text-white">Contacto de soporte</legend>
            <label className={PN_LABEL_CLS}>
              Correo de soporte
              <input
                type="email"
                value={form.soporteCorreo}
                onChange={(e) => cambiar("soporteCorreo", e.target.value)}
                maxLength={SOPORTE_CORREO_MAX}
                aria-invalid={Boolean(errores.soporteCorreo)}
                className={`${PN_INPUT_CLS} ${errores.soporteCorreo ? PN_INPUT_ERROR_CLS : ""}`}
              />
              {errores.soporteCorreo && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.soporteCorreo}</span>}
            </label>
            <label className={PN_LABEL_CLS}>
              Teléfono de soporte
              <input
                type="tel"
                value={form.soporteTelefono}
                onChange={(e) => cambiar("soporteTelefono", e.target.value)}
                maxLength={SOPORTE_TELEFONO_MAX}
                aria-invalid={Boolean(errores.soporteTelefono)}
                className={`${PN_INPUT_CLS} ${errores.soporteTelefono ? PN_INPUT_ERROR_CLS : ""}`}
              />
              {errores.soporteTelefono && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.soporteTelefono}</span>}
            </label>
            <label className={`${PN_LABEL_CLS} sm:col-span-2`}>
              Horario de atención
              <input
                value={form.soporteHorario}
                onChange={(e) => cambiar("soporteHorario", e.target.value)}
                maxLength={SOPORTE_HORARIO_MAX}
                aria-invalid={Boolean(errores.soporteHorario)}
                className={`${PN_INPUT_CLS} ${errores.soporteHorario ? PN_INPUT_ERROR_CLS : ""}`}
              />
              <span className={PN_HINT_CLS}>Por ejemplo: lunes a viernes, 8:00 a. m. a 5:00 p. m.</span>
              {errores.soporteHorario && <span role="alert" className={PN_FIELD_ERROR_CLS}>{errores.soporteHorario}</span>}
            </label>
          </fieldset>

          {errorGeneral && (
            <p role="alert" className="text-sm font-semibold text-[#C2410C]">
              {errorGeneral}
            </p>
          )}

          <div className="flex items-center gap-3">
            <button
              type="submit"
              disabled={busy || !hayCambios}
              className={PN_PRIMARY_BTN_CLS}
              style={PN_PRIMARY_BTN_STYLE}
            >
              {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Save className="h-4 w-4" aria-hidden="true" />}
              Guardar parámetros
            </button>
            {!hayCambios && <span className={PN_HINT_CLS}>No hay cambios por guardar.</span>}
          </div>
        </form>
      )}
    </UiStateBoundary>
  );
}
