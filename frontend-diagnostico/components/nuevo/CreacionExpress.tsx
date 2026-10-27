"use client";

import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, ArrowRight, Rocket } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { ApiError } from "@/lib/api";
import type { Configuracion } from "@/lib/configuracion";
import { nombreExpress } from "@/lib/diagnosticos/nombreExpress";
import {
  accesoNegadoExpress,
  campoVehiculo,
  crearExpress,
  entradaExpress,
  errorIdentificador,
  estimarExpress,
  type EstimadoFuente,
  type ExpressEntrada,
} from "@/lib/express/api";
import { ListaEstimado } from "@/components/carga/EstimadoConsultas";
import { DocumentoDeclarado } from "@/components/comun/DocumentoDeclarado";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS } from "@/components/configuracion/estilos";
import { ParametrosDiagnostico } from "@/components/parametrizacion/ParametrosDiagnostico";
import type { Parametrizacion } from "@/components/parametrizacion/useParametrizacion";
import type { BorradorExpress } from "./borradores";
import { CamposExpress, type ErroresLocatario } from "./CamposExpress";
import { DosZonas } from "./DosZonas";

type Fase =
  | { tipo: "editando" }
  | { tipo: "enviando"; texto: string }
  | { tipo: "confirmar"; entrada: ExpressEntrada; estimado: EstimadoFuente[] };
type ErrorGeneral = { titulo: string; mensaje: string; reintentar?: () => void };

/** Botón «Anterior» del prototipo: azul marino. */
const BOTON_ANTERIOR_CLS = BOTON_PRIMARIO_CLS.replace("bg-[var(--flit-brand-ink)]", "bg-flit-primary");

/** Nombre autogenerado del Express («Express <placa> <dd/mm/aaaa>», HU #13752 F01-C10); vacío sin una placa válida. */
export function nombreAutogenerado(placa: string, fecha: Date = new Date()): string {
  return errorIdentificador(placa) ? "" : nombreExpress(placa, fecha);
}

export interface CreacionExpressProps {
  p: Parametrizacion;
  configuracion: Configuracion;
  borrador: BorradorExpress;
  onBorrador: (cambio: Partial<BorradorExpress>) => void;
  onOcupado: (ocupado: boolean) => void;
}

/**
 * Express en la pantalla única (HU #14085; reglas de la HU #13755): a la izquierda la placa o el VIN y el locatario, a la
 * derecha los parámetros con el nombre autogenerado, que sigue a la placa mientras el usuario no lo edite. «Continuar»
 * valida la placa con la regla de Trámites y el rechazo por longitud, los parámetros y el tipo de documento; con
 * estimado_consultas = ANTES_DE_LANZAR pide POST /express/estimado y lo muestra en Parámetros antes de lanzar; lanza con
 * POST /express y con el 201 navega al tablero. Los 422 se pintan en su campo y el resto de rechazos en un aviso, sin
 * perder lo digitado.
 */
export function CreacionExpress({ p, configuracion, borrador, onBorrador, onOcupado }: CreacionExpressProps) {
  const router = useRouter();
  const id = useId();
  const formId = `${id}-form`;
  const rejillaRef = useRef<HTMLDivElement>(null);
  const [fase, setFase] = useState<Fase>({ tipo: "editando" });
  const [errorGeneral, setErrorGeneral] = useState<ErrorGeneral | null>(null);
  const [errorPlaca, setErrorPlaca] = useState<string | null>(null);
  const [errores, setErrores] = useState<ErroresLocatario>({});
  const [enfocar, setEnfocar] = useState(0);
  const ocupado = fase.tipo !== "editando";
  const nombre = borrador.nombreManual ?? nombreAutogenerado(borrador.placa);

  useEffect(() => onOcupado(ocupado), [ocupado, onOcupado]);

  // Tras un error de campo, el foco va al primer campo inválido de cualquiera de las dos zonas (en el orden de la página).
  useEffect(() => {
    if (enfocar === 0) return;
    rejillaRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [enfocar]);

  function cambiar(cambio: Partial<BorradorExpress>) {
    onBorrador(cambio);
    if ("placa" in cambio) setErrorPlaca(null);
    if ("tipoDocumento" in cambio) setErrores((a) => ({ ...a, tipo: undefined }));
    if ("documento" in cambio) setErrores((a) => ({ ...a, documento: undefined }));
  }

  function manejarError(e: unknown, reintentar: () => void) {
    setFase({ tipo: "editando" });
    const code = accesoNegadoExpress(e);
    if (code) {
      router.replace(`/403?code=${encodeURIComponent(code)}`);
      return;
    }
    const mensaje = e instanceof Error ? e.message : "No se pudo completar la acción.";
    const campo = campoVehiculo(e);
    // Un 422 de un campo (del vehículo o de la cabecera) se pinta en ese campo y el foco va a él.
    if (campo || p.aplicarErrorApi(e)) {
      if (campo === "identificador") setErrorPlaca(mensaje);
      if (campo === "tipo_documento_locatario") setErrores({ tipo: mensaje });
      if (campo === "documento_locatario") setErrores({ documento: mensaje });
      setEnfocar((n) => n + 1);
      return;
    }
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

  function alEnviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (ocupado) return;
    const errorDePlaca = errorIdentificador(borrador.placa);
    const parametrosValidos = p.validar(nombre);
    const errorTipo = borrador.documento.trim() && !borrador.tipoDocumento ? "Elige el tipo de documento del locatario." : undefined;
    setErrorPlaca(errorDePlaca);
    setErrores({ tipo: errorTipo });
    if (errorDePlaca || !parametrosValidos || errorTipo) {
      setEnfocar((n) => n + 1);
      return;
    }
    const entrada = entradaExpress(p.cabecera(nombre), borrador.placa, { tipo: borrador.tipoDocumento, documento: borrador.documento });
    if (configuracion.operacion.estimado_consultas === "ANTES_DE_LANZAR") estimar(entrada);
    else lanzar(entrada);
  }

  return (
    <DosZonas
      rejillaRef={rejillaRef}
      vehiculos={<CamposExpress borrador={borrador} onCambio={cambiar} errorPlaca={errorPlaca} errores={errores} disabled={ocupado} formId={formId} />}
      parametros={
        <form id={formId} onSubmit={alEnviar} noValidate aria-label="Parámetros del diagnóstico express" className="flex flex-col gap-4">
          <ParametrosDiagnostico p={p} nombre={nombre} onNombre={(valor) => onBorrador({ nombreManual: valor })} disabled={ocupado}>
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

            {fase.tipo === "confirmar" ? (
              <section aria-labelledby={`${id}-estimado`} className="flex flex-col gap-3 rounded-xl border border-[var(--color-flit-gray)] p-4 dark:border-white/15">
                <h3 id={`${id}-estimado`} className="text-sm font-semibold text-flit-primary dark:text-white">
                  Estimado de consultas
                </h3>
                <p className={AYUDA_CLS}>
                  Consultas previstas por fuente para <span className="font-semibold tracking-widest">{fase.entrada.identificador}</span>.
                </p>
                {fase.entrada.documento_locatario && (
                  <p className={AYUDA_CLS}>
                    Locatario: <DocumentoDeclarado tipo={fase.entrada.tipo_documento_locatario} documento={fase.entrada.documento_locatario} />
                  </p>
                )}
                <ListaEstimado estimado={fase.estimado} />
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
            ) : (
              <div className="flex justify-end">
                <button type="submit" disabled={ocupado} className={BOTON_PRIMARIO_CLS}>
                  Continuar
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </button>
              </div>
            )}
          </ParametrosDiagnostico>
        </form>
      }
    />
  );
}
