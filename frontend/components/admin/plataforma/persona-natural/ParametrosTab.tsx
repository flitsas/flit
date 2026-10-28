"use client";

// HU #14075 AC3/AC4 — Pestaña «Parámetros» del portal de Persona Natural: vigencias, topes de consulta, captcha y
// contacto de soporte. Un valor vacío significa «predeterminado del portal». Cada número se valida contra su rango
// (que se ve en el campo) y no deja guardar fuera de él. El guardado lleva la versión que se leyó: si otra persona
// cambió la configuración (409) se avisa con la opción de recargar y NO se pierde lo escrito hasta que se decida.
// Sin datos bancarios ni de pago (decisión del PO: otra épica).
import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshCcw, RotateCcw, Save } from "lucide-react";
import type { ReactNode } from "react";
import { InlineAlert } from "@/components/atom/InlineAlert";
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
import { ConfigCard, FIELD_CLASS, FIELD_LABEL, HELP_TEXT } from "@/components/admin/companies/ConfigUi";
import { CreateButton } from "@/components/atom/CreateButton";
import { ADMIN_BRAND_OUTLINE_BTN_CLS } from "@/components/admin/admin-ui-styles";
import { PnButton } from "./pn-componentes";
import { pnError } from "./pnErrors";

type CampoNumerico = keyof typeof RANGOS_PARAMETROS;
const CAMPOS_NUMERICOS: { campo: CampoNumerico; label: string; ayuda: string }[] = [
  {
    campo: "sesionVigenciaMinutos",
    label: "Duración de la sesión (minutos)",
    ayuda: "Cuánto tiempo puede una persona usar el portal antes de tener que ingresar de nuevo.",
  },
  {
    campo: "borradorVigenciaDias",
    label: "Días que se guarda un trámite sin enviar",
    ayuda: "Cuánto tiempo se conserva un trámite empezado y no enviado.",
  },
  {
    campo: "topeConsultasCiudadanoDia",
    label: "Consultas por persona al día",
    ayuda: "Máximo de consultas que una misma persona puede hacer en un día.",
  },
  {
    campo: "topeConsultasGlobalDia",
    label: "Consultas de todo el portal al día",
    ayuda: "Máximo de consultas que puede recibir el portal en un día, entre todas las personas.",
  },
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
      errores[campo] = `Escribe un número entero entre ${r.min} y ${r.max} ${r.unidad}.`;
    } else {
      numeros[campo] = n;
    }
  }
  const correo = f.soporteCorreo.trim();
  if (correo && (correo.length > SOPORTE_CORREO_MAX || !CORREO.test(correo))) {
    errores.soporteCorreo = "Escribe un correo válido, por ejemplo soporte@empresa.com.";
  }
  const telefono = f.soporteTelefono.trim();
  if (telefono && !TELEFONO.test(telefono)) {
    errores.soporteTelefono = `Escribe un teléfono de 7 a ${SOPORTE_TELEFONO_MAX} caracteres: solo números, +, paréntesis, guiones y espacios.`;
  }
  if (f.soporteHorario.trim().length > SOPORTE_HORARIO_MAX) {
    errores.soporteHorario = `El horario puede tener hasta ${SOPORTE_HORARIO_MAX} caracteres.`;
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

function CampoTexto({
  id,
  label,
  type = "text",
  value,
  max,
  error,
  ayuda,
  onChange,
}: {
  id: string;
  label: string;
  type?: string;
  value: string;
  max: number;
  error?: string;
  ayuda: ReactNode;
  onChange: (v: string) => void;
}) {
  const hintId = `${id}-hint`;
  return (
    <div>
      <label htmlFor={id} className={FIELD_LABEL}>
        {label}
      </label>
      <input
        id={id}
        type={type}
        value={value}
        maxLength={max}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={Boolean(error)}
        aria-describedby={hintId}
        className={FIELD_CLASS}
        style={{ borderColor: error ? "#FF4E00" : "#DFE5ED" }}
      />
      <p
        id={hintId}
        role={error ? "alert" : undefined}
        className={`mt-1 ${error ? "text-xs font-semibold text-[#C2410C]" : HELP_TEXT}`}
      >
        {error ?? ayuda}
      </p>
    </div>
  );
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
      show("Ajustes guardados. El portal ya usa los valores nuevos.", "success");
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

  function descartar() {
    if (!servidor) return;
    setForm(aFormulario(servidor));
    setErrores({});
    setErrorGeneral(null);
  }

  function recargarActual() {
    if (!conflicto) return;
    setServidor(conflicto);
    setForm(aFormulario(conflicto));
    setErrores({});
    setConflicto(null);
  }

  if (status === "loading") return <CarLoaderModal label="Cargando ajustes…" />;

  return (
    <UiStateBoundary
      status={status}
      errorMessage="No se pudieron cargar los ajustes del portal."
      onRetry={() => void load()}
    >
      {form && servidor && (
        <form
          onSubmit={guardar}
          noValidate
          className="flex max-w-5xl flex-col gap-6 pb-28"
          aria-label="Ajustes del portal"
        >
          <p className="text-sm text-[#59677D] dark:text-white/78">
            Estos ajustes definen cómo se comporta el portal para el ciudadano. Si dejas un campo vacío, el portal usa su
            valor habitual.
          </p>

          {conflicto && (
            <InlineAlert
              tone="warning"
              title="Alguien más cambió estos ajustes"
              action={
                <PnButton variant="secondary" icon={RefreshCcw} onClick={recargarActual}>
                  Cargar valores actuales
                </PnButton>
              }
            >
              Otra persona guardó cambios mientras editabas. Lo que escribiste sigue en pantalla, pero para guardarlo
              debes cargar los valores actuales y volver a aplicar tus cambios.
            </InlineAlert>
          )}

          <ConfigCard
            id="pn-ajustes-tiempos"
            title="Tiempos y límites"
            description="Cuánto dura la sesión de una persona, cuánto se guarda un trámite sin enviar y cuántas consultas se permiten."
          >
            <div className="grid gap-4 md:grid-cols-2">
              {CAMPOS_NUMERICOS.map(({ campo, label, ayuda }) => {
                const r = RANGOS_PARAMETROS[campo];
                const hintId = `pn-param-${campo}-hint`;
                const inputId = `pn-param-${campo}`;
                return (
                  <div key={campo}>
                    <label htmlFor={inputId} className={FIELD_LABEL}>
                      {label}
                    </label>
                    <div className="relative">
                      <input
                        id={inputId}
                        inputMode="numeric"
                        autoComplete="off"
                        value={form[campo]}
                        onChange={(e) => cambiar(campo, e.target.value)}
                        aria-invalid={Boolean(errores[campo])}
                        aria-describedby={hintId}
                        className={`${FIELD_CLASS} pr-24`}
                        style={{ borderColor: errores[campo] ? "#FF4E00" : "#DFE5ED" }}
                      />
                      <span
                        aria-hidden="true"
                        className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs font-semibold text-[#59677D] dark:text-white/70"
                      >
                        {r.unidad}
                      </span>
                    </div>
                    <p
                      id={hintId}
                      role={errores[campo] ? "alert" : undefined}
                      className={`mt-1 ${errores[campo] ? "text-xs font-semibold text-[#C2410C]" : HELP_TEXT}`}
                    >
                      {errores[campo] ??
                        `${ayuda} Permitido: de ${r.min} a ${r.max} ${r.unidad}. Vacío: usa el valor habitual del portal.`}
                    </p>
                  </div>
                );
              })}
            </div>
          </ConfigCard>

          <ConfigCard
            id="pn-ajustes-captcha"
            title="Verificación anti-robots"
            description="Pide a la persona demostrar que no es un programa automático antes de usar el portal."
          >
            <div className="max-w-sm">
              <label htmlFor="pn-param-captcha" className={FIELD_LABEL}>
                Verificación anti-robots (captcha)
              </label>
              <select
                id="pn-param-captcha"
                value={form.captcha}
                onChange={(e) => cambiar("captcha", e.target.value as Formulario["captcha"])}
                className={FIELD_CLASS}
                style={{ borderColor: "#DFE5ED" }}
              >
                <option value="">Lo habitual del portal</option>
                <option value="si">Activada</option>
                <option value="no">Desactivada</option>
              </select>
              <p className={`mt-1 ${HELP_TEXT}`}>«Lo habitual del portal» deja que el portal decida.</p>
            </div>
          </ConfigCard>

          <ConfigCard
            id="pn-ajustes-soporte"
            title="Contacto de soporte"
            description="Datos para que el ciudadano pueda pedir ayuda. Son opcionales."
          >
            <div className="grid gap-4 md:grid-cols-2">
              <CampoTexto
                id="pn-param-correo"
                label="Correo de soporte"
                type="email"
                value={form.soporteCorreo}
                max={SOPORTE_CORREO_MAX}
                error={errores.soporteCorreo}
                ayuda="Por ejemplo: soporte@empresa.com"
                onChange={(v) => cambiar("soporteCorreo", v)}
              />
              <CampoTexto
                id="pn-param-telefono"
                label="Teléfono de soporte"
                type="tel"
                value={form.soporteTelefono}
                max={SOPORTE_TELEFONO_MAX}
                error={errores.soporteTelefono}
                ayuda="Con indicativo si aplica."
                onChange={(v) => cambiar("soporteTelefono", v)}
              />
              <div className="md:col-span-2">
                <CampoTexto
                  id="pn-param-horario"
                  label="Horario de atención"
                  value={form.soporteHorario}
                  max={SOPORTE_HORARIO_MAX}
                  error={errores.soporteHorario}
                  ayuda="Por ejemplo: lunes a viernes, 8:00 a. m. a 5:00 p. m."
                  onChange={(v) => cambiar("soporteHorario", v)}
                />
              </div>
            </div>
          </ConfigCard>

          {errorGeneral && <InlineAlert tone="error">{errorGeneral}</InlineAlert>}

          <div className="flex flex-wrap items-center justify-between gap-3 rounded-[18px] border border-[#DFE5ED] bg-white px-4 py-3 shadow-[0_8px_24px_rgba(22,39,68,0.08)] dark:border-white/10 dark:bg-[#0B0F14]">
            <p className={HELP_TEXT} role="status">
              {hayCambios ? "Hay cambios sin guardar." : "No hay cambios por guardar."}
              {servidor.actualizadoEn && <> Última actualización: {formatFechaHora(servidor.actualizadoEn)}.</>}
            </p>
            <div className="flex flex-wrap items-center gap-2">
              {hayCambios && (
                <button type="button" onClick={descartar} disabled={busy} className={ADMIN_BRAND_OUTLINE_BTN_CLS}>
                  <RotateCcw className="h-4 w-4" aria-hidden="true" />
                  Descartar cambios
                </button>
              )}
              <CreateButton
                type="submit"
                icon={Save}
                label={busy ? "Guardando…" : "Guardar ajustes"}
                disabled={busy || !hayCambios}
              />
            </div>
          </div>
        </form>
      )}
    </UiStateBoundary>
  );
}
