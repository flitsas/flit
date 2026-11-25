"use client";

import { useEffect, useId, useState, type FormEvent } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, CAMPO_CLS, CAMPO_INVALIDO_CLS, ERROR_CLS, ETIQUETA_CLS } from "@/components/comun/estilos";
import {
  ERRORES_BACKOFFICE,
  ETIQUETA_ORIGEN_CREDENCIAL,
  LITERALES_BACKOFFICE,
  cargarCredenciales,
  obtenerEstadoCredenciales,
  type EstadoCredenciales,
} from "@/lib/backoffice/api";
import { campoDeError, mensajeErrorFijo } from "@/lib/errores";
import { rotular } from "@/lib/rotulos";

const AYUDA = "Rota las credenciales en la consola del proveedor antes de cargarlas. Al guardar se validan con el proveedor y no se vuelven a mostrar.";
/** Límite del contrato (CargaCredenciales: maxLength 256). */
const LONGITUD_MAXIMA = 256;

const FECHA = new Intl.DateTimeFormat("es-CO", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "America/Bogota" });
const fecha = (iso: string) => FECHA.format(new Date(iso)).replace(",", "");

type Lectura = { tipo: "cargando" } | { tipo: "error" } | { tipo: "listo"; estado: EstadoCredenciales };
type CampoCredencial = "apiKey" | "webhookSecret";

export interface FormularioCredencialesProps {
  tenantId: string;
  /** Estado de aprovisionamiento de la compañía (para el aviso de rotación en REQUIERE_CREDENCIALES). */
  estado: string | null;
  /** Tras guardar (204): quien lo monta relee el aprovisionamiento. */
  onGuardado?: () => void;
}

/**
 * Credenciales del proveedor de la compañía (spec-visual §3.10, molde CredencialImpuestoDiagnostico; D-409, DA-2=b).
 * La UI nunca recibe los valores: GET …/credenciales solo dice si están, el origen, el prefijo y la fecha. «Cargar
 * credenciales» o «Reemplazar» abre un formulario con campos tipo contraseña (sin autocompletar ni corrector); lo escrito
 * vive solo en el estado local, no se registra ni se guarda en el navegador y se vacía al guardar (204) o cancelar.
 */
export function FormularioCredenciales({ tenantId, estado, onGuardado }: FormularioCredencialesProps) {
  const uid = useId();
  const [lectura, setLectura] = useState<Lectura>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [abierto, setAbierto] = useState(false);
  const [apiKey, setApiKey] = useState("");
  const [secreto, setSecreto] = useState("");
  const [visible, setVisible] = useState(false);
  const [errorCampo, setErrorCampo] = useState<{ campo: CampoCredencial; texto: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState(false);
  const [guardando, setGuardando] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    obtenerEstadoCredenciales(tenantId, controller.signal).then(
      (e) => {
        if (!controller.signal.aborted) setLectura({ tipo: "listo", estado: e });
      },
      () => {
        if (!controller.signal.aborted) setLectura({ tipo: "error" });
      },
    );
    return () => controller.abort();
  }, [tenantId, intento]);

  const vaciar = () => {
    setApiKey("");
    setSecreto("");
    setVisible(false);
    setErrorCampo(null);
  };
  const cerrar = () => {
    vaciar();
    setError(null);
    setAbierto(false);
  };
  const abrir = () => {
    setExito(false);
    setError(null);
    setAbierto(true);
  };

  async function guardar(ev: FormEvent) {
    ev.preventDefault();
    if (guardando) return;
    setError(null);
    setExito(false);
    if (!apiKey.trim() && !secreto.trim()) {
      setErrorCampo({ campo: "apiKey", texto: ERRORES_BACKOFFICE.CREDENCIAL_REQUERIDA });
      return;
    }
    setErrorCampo(null);
    setGuardando(true);
    try {
      await cargarCredenciales(tenantId, { apiKey: apiKey.trim() || null, webhookSecret: secreto.trim() || null });
      cerrar();
      setExito(true);
      setIntento((n) => n + 1);
      onGuardado?.();
    } catch (e) {
      const texto = mensajeErrorFijo(e, "No se pudieron guardar las credenciales. Intenta de nuevo.", ERRORES_BACKOFFICE);
      const campo = campoDeError(e);
      if (campo === "apiKey" || campo === "webhookSecret") setErrorCampo({ campo, texto });
      else setError(texto);
    } finally {
      setGuardando(false);
    }
  }

  const idCampo = (c: CampoCredencial) => `${uid}-${c}`;
  const idError = (c: CampoCredencial) => `${uid}-${c}-error`;
  const ayudaId = `${uid}-ayuda`;
  const describe = (c: CampoCredencial) => [ayudaId, errorCampo?.campo === c ? idError(c) : null].filter(Boolean).join(" ");
  const invalido = (c: CampoCredencial) => (errorCampo?.campo === c ? true : undefined);

  let resumen;
  if (lectura.tipo === "cargando") resumen = <VehicleLoader label="Cargando las credenciales…" />;
  else if (lectura.tipo === "error")
    resumen = (
      <InlineAlert
        tone="error"
        title="No se pudo leer el estado de las credenciales"
        action={
          <button
            type="button"
            className={BOTON_REINTENTAR_CLS}
            onClick={() => {
              setLectura({ tipo: "cargando" });
              setIntento((n) => n + 1);
            }}
          >
            Reintentar
          </button>
        }
      >
        Revisa tu conexión e intenta de nuevo.
      </InlineAlert>
    );
  else {
    const e = lectura.estado;
    const apiKeyTexto = e.tieneApiKey
      ? [
          LITERALES_BACKOFFICE.credencialConfigurada,
          e.actualizadoEn ? ` el ${fecha(e.actualizadoEn)}` : "",
          e.apiKeyPrefijo ? ` · empieza por ${e.apiKeyPrefijo}` : "",
        ].join("")
      : LITERALES_BACKOFFICE.credencialNoConfigurada;
    resumen = (
      <div className="flex flex-wrap items-start gap-3">
        <ul className="flex min-w-0 flex-1 flex-col gap-1 text-sm text-flit-primary">
          <li>{`API key: ${apiKeyTexto}`}</li>
          <li>{`Secreto del webhook: ${e.tieneWebhookSecret ? LITERALES_BACKOFFICE.credencialConfigurada : LITERALES_BACKOFFICE.credencialNoConfigurada}`}</li>
          {e.origen && <li>{`Origen: ${rotular({ contexto: "origen de la credencial", codigo: e.origen, respaldo: ETIQUETA_ORIGEN_CREDENCIAL })}`}</li>}
        </ul>
        {!abierto && (
          <Button variant="secondary" size="sm" onClick={abrir}>
            {e.tieneApiKey || e.tieneWebhookSecret ? "Reemplazar" : "Cargar credenciales"}
          </Button>
        )}
      </div>
    );
  }

  return (
    <div role="group" aria-labelledby={`${uid}-titulo`} className="flex flex-col gap-3">
      <h2 id={`${uid}-titulo`} className="text-base font-semibold text-flit-primary">
        Credenciales
      </h2>
      {estado === "REQUIERE_CREDENCIALES" && (
        <InlineAlert tone="warning" vivo={false}>
          {LITERALES_BACKOFFICE.avisoRotacionCredenciales}
        </InlineAlert>
      )}
      {resumen}
      {abierto && (
        <form noValidate onSubmit={(ev) => void guardar(ev)} className="flex flex-col gap-3" aria-label="Cargar credenciales">
          <div className="flex flex-col gap-1">
            <label htmlFor={idCampo("apiKey")} className={ETIQUETA_CLS}>
              API key
            </label>
            <div className="flex gap-2">
              <input
                id={idCampo("apiKey")}
                type={visible ? "text" : "password"}
                autoComplete="off"
                spellCheck={false}
                autoCapitalize="none"
                autoCorrect="off"
                maxLength={LONGITUD_MAXIMA}
                value={apiKey}
                onChange={(ev) => setApiKey(ev.target.value)}
                disabled={guardando}
                aria-invalid={invalido("apiKey")}
                aria-describedby={describe("apiKey")}
                className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
              />
              <Button variant="ghost" size="sm" aria-pressed={visible} onClick={() => setVisible((v) => !v)} className="shrink-0">
                {visible ? "Ocultar API key" : "Mostrar API key"}
              </Button>
            </div>
            {errorCampo?.campo === "apiKey" && (
              <p id={idError("apiKey")} className={ERROR_CLS}>
                {errorCampo.texto}
              </p>
            )}
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor={idCampo("webhookSecret")} className={ETIQUETA_CLS}>
              Secreto del webhook (opcional)
            </label>
            <input
              id={idCampo("webhookSecret")}
              type="password"
              autoComplete="off"
              spellCheck={false}
              autoCapitalize="none"
              autoCorrect="off"
              maxLength={LONGITUD_MAXIMA}
              value={secreto}
              onChange={(ev) => setSecreto(ev.target.value)}
              disabled={guardando}
              aria-invalid={invalido("webhookSecret")}
              aria-describedby={describe("webhookSecret")}
              className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
            />
            {errorCampo?.campo === "webhookSecret" && (
              <p id={idError("webhookSecret")} className={ERROR_CLS}>
                {errorCampo.texto}
              </p>
            )}
          </div>
          <p id={ayudaId} className={AYUDA_CLS}>
            {AYUDA}
          </p>
          {error && (
            <InlineAlert tone="error" title="No se guardaron las credenciales">
              {error}
            </InlineAlert>
          )}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" size="sm" cargando={guardando}>
              Validar y guardar
            </Button>
            <Button variant="navy" size="sm" onClick={cerrar} disabled={guardando}>
              Cancelar
            </Button>
          </div>
        </form>
      )}
      <div role="status" aria-live="polite" aria-atomic="true">
        {exito && (
          <InlineAlert tone="success" vivo={false}>
            Credenciales guardadas y validadas.
          </InlineAlert>
        )}
      </div>
    </div>
  );
}
