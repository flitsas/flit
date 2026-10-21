"use client";

import { useCallback, useEffect, useId, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, Rocket } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { ApiError } from "@/lib/api";
import { FUENTES, obtenerConfiguracion, type Configuracion } from "@/lib/configuracion";
import type { CabeceraEntrada } from "@/lib/diagnosticos/api";
import {
  TIPOS_DOCUMENTO_LOCATARIO,
  accesoNegadoExpress,
  campoVehiculo,
  crearExpress,
  entradaExpress,
  estimarExpress,
  type EstimadoFuente,
  type ExpressEntrada,
} from "@/lib/express/api";
import { DocumentoDeclarado, enmascararDocumento } from "@/components/comun/DocumentoDeclarado";
import { ParametrizacionCompacta } from "@/components/parametrizacion/ParametrizacionCompacta";
import { CAMPO_INVALIDO_CLS, ERROR_CLS, INSIGNIA_CLS } from "@/components/parametrizacion/estilos";
import {
  AYUDA_CLS,
  BOTON_PRIMARIO_CLS,
  BOTON_REINTENTAR_CLS,
  CAMPO_CLS,
  ETIQUETA_CLS,
} from "@/components/configuracion/estilos";

export interface PanelExpressProps {
  /** Identificador ya validado en la captura (placa o VIN normalizado). */
  identificador: string;
  fecha?: Date;
  /** Un 422 de la API sobre el identificador: la captura lo pinta en su campo. */
  onErrorIdentificador: (mensaje: string) => void;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; configuracion: Configuracion };
type Fase =
  | { tipo: "editando" }
  | { tipo: "enviando"; texto: string }
  | { tipo: "confirmar"; entrada: ExpressEntrada; estimado: EstimadoFuente[] };
type ErrorGeneral = { titulo: string; mensaje: string; reintentar?: () => void };
type ErroresLocatario = { tipo?: string; documento?: string };

/** Botón «Anterior» del prototipo: azul marino. */
const BOTON_ANTERIOR_CLS = BOTON_PRIMARIO_CLS.replace("bg-[var(--flit-brand-ink)]", "bg-flit-primary");

/**
 * Panel del Diagnóstico Express (HU #13755, F02-H8): pide GET /configuracion y abre la parametrización compacta (#13752)
 * con el nombre autogenerado, más el documento opcional del locatario, rotulado «Declarado por el cliente» y enmascarado
 * tras escribirlo. Con estimado_consultas = ANTES_DE_LANZAR muestra el estimado de POST /express/estimado antes de lanzar;
 * lanza con POST /express y, con el 201, navega a /diagnosticos/{id}. Estados: cargando, error con Reintentar y lleno.
 */
export function PanelExpress({ identificador, fecha, onErrorIdentificador }: PanelExpressProps) {
  const router = useRouter();
  const id = useId();
  const [intento, setIntento] = useState(0);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [fase, setFase] = useState<Fase>({ tipo: "editando" });
  const [errorGeneral, setErrorGeneral] = useState<ErrorGeneral | null>(null);
  // El documento solo vive en este estado en memoria: no se guarda en el navegador ni se registra en la consola.
  const [tipoDocumento, setTipoDocumento] = useState("");
  const [documento, setDocumento] = useState("");
  const [documentoEnfocado, setDocumentoEnfocado] = useState(false);
  const [errores, setErrores] = useState<ErroresLocatario>({});

  useEffect(() => {
    const controller = new AbortController();
    obtenerConfiguracion(controller.signal)
      .then((configuracion) => setCarga({ tipo: "listo", configuracion }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        const code = accesoNegadoExpress(e);
        if (code) return router.replace(`/403?code=${encodeURIComponent(code)}`);
        setCarga({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo cargar la configuración." });
      });
    return () => controller.abort();
  }, [intento, router]);

  const reintentarCarga = useCallback(() => {
    setCarga({ tipo: "cargando" });
    setIntento((i) => i + 1);
  }, []);

  function manejarError(e: unknown, reintentar: () => void) {
    setFase({ tipo: "editando" });
    const code = accesoNegadoExpress(e);
    if (code) {
      router.replace(`/403?code=${encodeURIComponent(code)}`);
      return;
    }
    const campo = campoVehiculo(e);
    const mensaje = e instanceof Error ? e.message : "No se pudo completar la acción.";
    if (campo === "identificador") return onErrorIdentificador(mensaje);
    if (campo === "tipo_documento_locatario") return setErrores({ tipo: mensaje });
    if (campo === "documento_locatario") return setErrores({ documento: mensaje });
    if (e instanceof ApiError && e.status === 403) return setErrorGeneral({ titulo: "Sin permiso para crear diagnósticos", mensaje });
    if (e instanceof ApiError && e.status === 422) return setErrorGeneral({ titulo: "Revisa la parametrización", mensaje });
    setErrorGeneral({ titulo: "No se pudo lanzar el diagnóstico", mensaje, reintentar });
  }

  function lanzar(entrada: ExpressEntrada) {
    setErrorGeneral(null);
    setFase({ tipo: "enviando", texto: "Creando el diagnóstico…" });
    crearExpress(entrada)
      .then((creado) => router.push(`/diagnosticos/${encodeURIComponent(creado.id)}`))
      .catch((e: unknown) => manejarError(e, () => lanzar(entrada)));
  }

  function estimar(entrada: ExpressEntrada) {
    setErrorGeneral(null);
    setFase({ tipo: "enviando", texto: "Calculando el estimado de consultas…" });
    estimarExpress(entrada)
      .then((r) => {
        if (r.mostrar_estimado && r.estimado_consultas?.length) setFase({ tipo: "confirmar", entrada, estimado: r.estimado_consultas });
        else lanzar(entrada);
      })
      .catch((e: unknown) => manejarError(e, () => estimar(entrada)));
  }

  function alContinuar(configuracion: Configuracion, cabecera: CabeceraEntrada) {
    if (documento.trim() && !tipoDocumento) {
      setErrores({ tipo: "Elige el tipo de documento del locatario." });
      document.getElementById(`${id}-tipo-doc`)?.focus();
      return;
    }
    setErrores({});
    const entrada = entradaExpress(cabecera, identificador, { tipo: tipoDocumento, documento });
    if (configuracion.operacion.estimado_consultas === "ANTES_DE_LANZAR") estimar(entrada);
    else lanzar(entrada);
  }

  if (carga.tipo === "cargando") return <VehicleLoader label="Cargando la parametrización…" />;
  if (carga.tipo === "error")
    return (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la parametrización"
        action={
          <button type="button" onClick={reintentarCarga} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );

  const configuracion = carga.configuracion;
  const ocupado = fase.tipo !== "editando";
  const tipoId = `${id}-tipo-doc`;
  const docId = `${id}-doc`;
  const rotuloId = `${id}-rotulo`;
  const valorVisible = documento && !documentoEnfocado ? enmascararDocumento(documento) : documento;

  return (
    <div className="flex flex-col gap-4">
      <ParametrizacionCompacta
        identificador={identificador}
        configuracion={configuracion}
        fecha={fecha}
        ocupado={ocupado}
        onContinuar={(cabecera) => alContinuar(configuracion, cabecera)}
      >
        <section aria-labelledby={`${id}-locatario`} className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <h3 id={`${id}-locatario`} className={ETIQUETA_CLS}>
              Locatario
            </h3>
            <span id={rotuloId} className={INSIGNIA_CLS}>
              Declarado por el cliente
            </span>
          </div>
          <p className={AYUDA_CLS}>Opcional. Se guarda cifrado y en pantalla solo se ven los 4 últimos caracteres.</p>
          <div className="grid gap-3 sm:grid-cols-[minmax(0,12rem)_minmax(0,1fr)]">
            <div className="flex flex-col gap-1.5">
              <label htmlFor={tipoId} className={ETIQUETA_CLS}>
                Tipo de documento del locatario
              </label>
              <select
                id={tipoId}
                value={tipoDocumento}
                onChange={(e) => {
                  setTipoDocumento(e.target.value);
                  setErrores((a) => ({ ...a, tipo: undefined }));
                }}
                disabled={ocupado}
                aria-invalid={errores.tipo ? true : undefined}
                aria-describedby={errores.tipo ? `${tipoId}-error` : undefined}
                className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
              >
                <option value="">Sin documento</option>
                {TIPOS_DOCUMENTO_LOCATARIO.map((t) => (
                  <option key={t} value={t}>
                    {t}
                  </option>
                ))}
              </select>
              {errores.tipo && (
                <p id={`${tipoId}-error`} className={ERROR_CLS}>
                  {errores.tipo}
                </p>
              )}
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor={docId} className={ETIQUETA_CLS}>
                Número de documento del locatario (opcional)
              </label>
              <input
                id={docId}
                type="text"
                value={valorVisible}
                onFocus={() => setDocumentoEnfocado(true)}
                onBlur={() => setDocumentoEnfocado(false)}
                onChange={(e) => {
                  setDocumento(e.target.value);
                  setErrores((a) => ({ ...a, documento: undefined }));
                }}
                disabled={ocupado}
                autoComplete="off"
                spellCheck={false}
                aria-invalid={errores.documento ? true : undefined}
                aria-describedby={errores.documento ? `${docId}-error ${rotuloId}` : rotuloId}
                className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} font-mono`}
              />
              {errores.documento && (
                <p id={`${docId}-error`} className={ERROR_CLS}>
                  {errores.documento}
                </p>
              )}
            </div>
          </div>
        </section>
      </ParametrizacionCompacta>

      {fase.tipo === "enviando" && <VehicleLoader label={fase.texto} />}

      {errorGeneral && (
        <InlineAlert
          tone="error"
          title={errorGeneral.titulo}
          action={
            errorGeneral.reintentar && (
              <button type="button" onClick={errorGeneral.reintentar} className={BOTON_REINTENTAR_CLS}>
                Reintentar
              </button>
            )
          }
        >
          {errorGeneral.mensaje}
        </InlineAlert>
      )}

      {fase.tipo === "confirmar" && (
        <section aria-labelledby={`${id}-estimado`} className="flex flex-col gap-3 rounded-xl border border-[var(--color-flit-gray)] p-4 dark:border-white/15">
          <h3 id={`${id}-estimado`} className="text-sm font-semibold text-flit-primary dark:text-white">
            Estimado de consultas
          </h3>
          <p className={AYUDA_CLS}>
            Consultas previstas por fuente para <span className="font-semibold tracking-widest">{identificador}</span>.
          </p>
          {fase.entrada.documento_locatario && (
            <p className={AYUDA_CLS}>
              Locatario: <DocumentoDeclarado tipo={fase.entrada.tipo_documento_locatario} documento={fase.entrada.documento_locatario} />
            </p>
          )}
          <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            {fase.estimado.map((e) => (
              <div key={e.fuente} className="flex flex-col rounded-xl border border-[var(--color-flit-gray)] px-3 py-2 dark:border-white/15">
                <dt className={AYUDA_CLS}>{FUENTES.find((f) => f.codigo === e.fuente)?.nombre ?? e.fuente}</dt>
                <dd className="text-lg font-semibold text-flit-primary dark:text-white">{e.consultas}</dd>
              </div>
            ))}
          </dl>
          <div className="flex flex-wrap justify-end gap-2">
            <button type="button" onClick={() => setFase({ tipo: "editando" })} className={BOTON_ANTERIOR_CLS}>
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
              Anterior
            </button>
            <button type="button" onClick={() => lanzar(fase.entrada)} className={BOTON_PRIMARIO_CLS}>
              <Rocket className="h-4 w-4" aria-hidden="true" />
              Lanzar diagnóstico
            </button>
          </div>
        </section>
      )}
    </div>
  );
}
