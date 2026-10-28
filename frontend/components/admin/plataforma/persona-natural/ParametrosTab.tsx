"use client";

// HU #14075 AC3/AC4 — Pestaña «Parámetros» del portal de Persona Natural: vigencias, topes de consulta, captcha y
// contacto de soporte. Un valor vacío significa «predeterminado del portal». Cada número se valida contra su rango
// (que se ve en el campo) y no deja guardar fuera de él. El guardado lleva la versión que se leyó: si otra persona
// cambió la configuración (409) se avisa con la opción de recargar y NO se pierde lo escrito hasta que se decida.
// Sin datos bancarios ni de pago (decisión del PO: otra épica).
import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshCcw, RotateCcw, Save } from "lucide-react";
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
import { PN_FIELD_ERROR_CLS, PN_HINT_CLS, PN_INPUT_CLS, PN_INPUT_ERROR_CLS, PN_LABEL_CLS } from "./pn-ui";
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
        <form onSubmit={guardar} noValidate className="flex max-w-3xl flex-col gap-6" aria-label="Ajustes del portal">
          <p className="text-sm text-[#59677D] dark:text-white/78">
            Estos ajustes definen cómo se comporta el portal para el ciudadano. Si dejas un campo vacío, el portal usa su
            valor habitual.
            {servidor.actualizadoEn && <> Última actualización: {formatFechaHora(servidor.actualizadoEn)}.</>}
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

          <fieldset className="grid gap-4 sm:grid-cols-2">
            <legend className="mb-2 text-sm font-bold text-flit-primary dark:text-white">Tiempos y límites</legend>
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
                    placeholder="Valor habitual"
                    aria-invalid={Boolean(errores[campo])}
                    aria-describedby={hintId}
                    className={`${PN_INPUT_CLS} ${errores[campo] ? PN_INPUT_ERROR_CLS : ""}`}
                  />
                  <span id={hintId} className={errores[campo] ? PN_FIELD_ERROR_CLS : PN_HINT_CLS} role={errores[campo] ? "alert" : undefined}>
                    {errores[campo] ?? `${ayuda} Permitido: de ${r.min} a ${r.max} ${r.unidad}.`}
                  </span>
                </label>
              );
            })}
            <label className={PN_LABEL_CLS}>
              Verificación anti-robots (captcha)
              <select value={form.captcha} onChange={(e) => cambiar("captcha", e.target.value as Formulario["captcha"])} className={PN_INPUT_CLS}>
                <option value="">Lo habitual del portal</option>
                <option value="si">Activada</option>
                <option value="no">Desactivada</option>
              </select>
              <span className={PN_HINT_CLS}>Pide a la persona demostrar que no es un programa automático.</span>
            </label>
          </fieldset>

          <fieldset className="grid gap-4 sm:grid-cols-2">
            <legend className="mb-2 text-sm font-bold text-flit-primary dark:text-white">Contacto de soporte</legend>
            <p className="-mt-1 text-xs text-[#59677D] sm:col-span-2 dark:text-white/70">
              Datos para que el ciudadano pueda pedir ayuda. Son opcionales.
            </p>
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

          {errorGeneral && <InlineAlert tone="error">{errorGeneral}</InlineAlert>}

          <div className="flex flex-wrap items-center gap-3">
            <PnButton type="submit" variant="primary" icon={Save} loading={busy} disabled={!hayCambios}>
              Guardar ajustes
            </PnButton>
            {hayCambios && (
              <PnButton variant="secondary" icon={RotateCcw} onClick={descartar} disabled={busy}>
                Descartar cambios
              </PnButton>
            )}
            {!hayCambios && <span className={PN_HINT_CLS}>No hay cambios por guardar.</span>}
          </div>
        </form>
      )}
    </UiStateBoundary>
  );
}
