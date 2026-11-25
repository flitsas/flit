"use client";

import { useId, useState, type FormEvent } from "react";
import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { AYUDA_CLS, CAMPO_CLS, CAMPO_INVALIDO_CLS, ERROR_CLS, ETIQUETA_CLS } from "@/components/comun/estilos";
import { campoDeError, conTope, mensajeErrorFijo } from "@/lib/errores";
import {
  ERRORES_CONFIGURACION,
  MAXIMO_ABSOLUTO_LOTE,
  guardarConfiguracionOperativa,
  type ConfiguracionImpuestos,
} from "@/lib/configuracion/api";

/** Forma mínima de un correo (la API vuelve a validarlo: 422 CORREO_INVALIDO). */
const CORREO_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type Campo = "maximo" | "correos";
type Errores = Partial<Record<Campo, string>>;

const lineas = (texto: string) => texto.split("\n").map((l) => l.trim());

function validar(maximo: string, correos: string, contractual: number): Errores {
  const e: Errores = {};
  const m = maximo.trim();
  if (m) {
    const n = Number(m);
    if (!Number.isInteger(n) || n < 1 || n > MAXIMO_ABSOLUTO_LOTE) e.maximo = ERRORES_CONFIGURACION.TOPE_INVALIDO;
    else if (n > contractual) e.maximo = conTope(ERRORES_CONFIGURACION.TOPE_SUPERA_CONTRACTUAL, contractual);
  }
  const invalida = lineas(correos).findIndex((l) => l !== "" && !CORREO_RE.test(l));
  if (invalida >= 0) e.correos = `La línea ${invalida + 1} no es un correo válido.`;
  return e;
}

export interface PestanaOperativaProps {
  configuracion: ConfiguracionImpuestos;
  puedeEditar: boolean;
  /** Compañía elegida por el Super Admin (o null para la de la sesión). */
  empresa: string | null;
  onGuardado: (configuracion: ConfiguracionImpuestos) => void;
}

/**
 * Pestaña «Operativa» (spec-visual §3.9; D-CONFIG): máximo de placas por lote de la compañía (≤ contractual; vacío =
 * el contractual) y correos del aviso «Lote terminado», uno por línea. Sin impuestos.configuracion.editar, solo lectura
 * con los campos deshabilitados. Los errores van junto a su campo (aria-invalid + aria-describedby); un fallo al guardar
 * conserva lo digitado.
 */
export function PestanaOperativa({ configuracion, puedeEditar, empresa, onGuardado }: PestanaOperativaProps) {
  const id = useId();
  const contractual = configuracion.contractual.maxRegistrosLote;
  const inicialMaximo = configuracion.operativa.maxRegistrosLote === null ? "" : String(configuracion.operativa.maxRegistrosLote);
  const inicialCorreos = configuracion.operativa.correosAviso.join("\n");
  const [maximo, setMaximo] = useState(inicialMaximo);
  const [correos, setCorreos] = useState(inicialCorreos);
  const [errores, setErrores] = useState<Errores>({});
  const [errorGuardar, setErrorGuardar] = useState<string | null>(null);
  const [guardado, setGuardado] = useState(false);
  const [guardando, setGuardando] = useState(false);

  const ayudaMaximoId = `${id}-maximo-ayuda`;
  const ayudaCorreosId = `${id}-correos-ayuda`;
  const vacioCorreosId = `${id}-correos-vacio`;
  const errorId = (c: Campo) => `${id}-${c}-error`;
  const sinCorreos = lineas(correos).every((l) => l === "");

  const describe = (c: Campo, ...ayudas: string[]) => [...ayudas, errores[c] ? errorId(c) : null].filter(Boolean).join(" ");

  const descartar = () => {
    setMaximo(inicialMaximo);
    setCorreos(inicialCorreos);
    setErrores({});
    setErrorGuardar(null);
    setGuardado(false);
  };

  async function guardar(ev: FormEvent) {
    ev.preventDefault();
    if (guardando) return;
    setGuardado(false);
    setErrorGuardar(null);
    const encontrados = validar(maximo, correos, contractual);
    setErrores(encontrados);
    if (Object.keys(encontrados).length > 0) return;
    setGuardando(true);
    try {
      const nueva = await guardarConfiguracionOperativa(
        { maxRegistrosLote: maximo.trim() ? Number(maximo.trim()) : null, correosAviso: lineas(correos).filter((l) => l !== "") },
        empresa,
      );
      setMaximo(nueva.operativa.maxRegistrosLote === null ? "" : String(nueva.operativa.maxRegistrosLote));
      setCorreos(nueva.operativa.correosAviso.join("\n"));
      setGuardado(true);
      onGuardado(nueva);
    } catch (e) {
      const texto = mensajeErrorFijo(e, "Revisa tu conexión e intenta de nuevo.", ERRORES_CONFIGURACION);
      const campo = campoDeError(e);
      if (campo === "maxRegistrosLote") setErrores({ maximo: texto });
      else if (campo === "correosAviso") setErrores({ correos: texto });
      else setErrorGuardar(texto);
    } finally {
      setGuardando(false);
    }
  }

  return (
    <form noValidate onSubmit={(ev) => void guardar(ev)} className="flex flex-col gap-5" aria-label="Configuración operativa">
      {!puedeEditar && (
        <InlineAlert tone="info" title="Solo lectura" vivo={false}>
          Puedes ver la configuración, pero no cambiarla.
        </InlineAlert>
      )}
      <div className="flex max-w-md flex-col gap-1">
        <label htmlFor={`${id}-maximo`} className={ETIQUETA_CLS}>
          Máximo de placas por lote
        </label>
        <input
          id={`${id}-maximo`}
          type="number"
          inputMode="numeric"
          min={1}
          max={contractual}
          step={1}
          value={maximo}
          onChange={(e) => setMaximo(e.target.value)}
          disabled={!puedeEditar || guardando}
          aria-invalid={errores.maximo ? true : undefined}
          aria-describedby={describe("maximo", ayudaMaximoId)}
          className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} tabular-nums`}
        />
        <p id={ayudaMaximoId} className={AYUDA_CLS}>
          {`Hasta ${new Intl.NumberFormat("es-CO").format(contractual)}, lo contratado.`}
        </p>
        {errores.maximo && (
          <p id={errorId("maximo")} className={ERROR_CLS}>
            {errores.maximo}
          </p>
        )}
      </div>
      <div className="flex max-w-xl flex-col gap-1">
        <label htmlFor={`${id}-correos`} className={ETIQUETA_CLS}>
          Correos para el aviso de lote terminado
        </label>
        <textarea
          id={`${id}-correos`}
          rows={4}
          value={correos}
          spellCheck={false}
          autoCapitalize="none"
          onChange={(e) => setCorreos(e.target.value)}
          disabled={!puedeEditar || guardando}
          aria-invalid={errores.correos ? true : undefined}
          aria-describedby={describe("correos", ayudaCorreosId, ...(sinCorreos ? [vacioCorreosId] : []))}
          className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} h-auto py-2`}
        />
        <p id={ayudaCorreosId} className={AYUDA_CLS}>
          Reciben un correo cuando un lote termina o se cierra.
        </p>
        {sinCorreos && (
          <p id={vacioCorreosId} className={AYUDA_CLS}>
            Nadie recibe el aviso por correo; el aviso en pantalla sigue llegando.
          </p>
        )}
        {errores.correos && (
          <p id={errorId("correos")} className={ERROR_CLS}>
            {errores.correos}
          </p>
        )}
      </div>
      {errorGuardar && (
        <InlineAlert tone="error" title="No se guardó la configuración">
          {errorGuardar}
        </InlineAlert>
      )}
      <div role="status" aria-live="polite" aria-atomic="true">
        {guardado && (
          <InlineAlert tone="success" vivo={false}>
            Configuración guardada.
          </InlineAlert>
        )}
      </div>
      {puedeEditar && (
        <div className="flex flex-wrap items-center justify-end gap-3">
          <p className={`${AYUDA_CLS} mr-auto`}>Los cambios aplican a los lotes nuevos.</p>
          <Button variant="secondary" size="sm" onClick={descartar} disabled={guardando}>
            Descartar
          </Button>
          <Button type="submit" size="sm" cargando={guardando}>
            Guardar configuración
          </Button>
        </div>
      )}
      {!puedeEditar && <p className={AYUDA_CLS}>Los cambios aplican a los lotes nuevos.</p>}
    </form>
  );
}
