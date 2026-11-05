"use client";

import { useId } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import type { ModoDiagnostico, TipoTramite } from "@/lib/diagnosticos/api";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS, CAMPO_CLS, ETIQUETA_CLS, MARCA_CLS, OPCION_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS } from "./estilos";

export type EstadoTipos =
  | { tipo: "cargando" }
  | { tipo: "error"; mensaje: string; segundosRestantes: number }
  | { tipo: "listo"; tipos: TipoTramite[] };

export interface SeccionModoProps {
  modo: ModoDiagnostico;
  onModo: (modo: ModoDiagnostico) => void;
  tipoCodigo: string;
  onTipo: (codigo: string) => void;
  tipos: EstadoTipos;
  onReintentarTipos: () => void;
  errorModo?: string;
  errorTipo?: string;
  disabled?: boolean;
  compacta?: boolean;
}

const MODOS: ReadonlyArray<{ valor: ModoDiagnostico; nombre: string }> = [
  { valor: "SIMPLE", nombre: "Simple" },
  { valor: "CON_ANALISIS", nombre: "Con Análisis IA" },
];

/**
 * Sección Modo (HU #13752, F01-C5/C6): Simple o Con Análisis IA, excluyentes. Solo en Con Análisis IA se pide el tipo de
 * trámite, con los tipos de GET /tipos-tramite; si el catálogo responde 503, el error ofrece Reintentar cuando vence
 * Retry-After.
 */
export function SeccionModo({
  modo,
  onModo,
  tipoCodigo,
  onTipo,
  tipos,
  onReintentarTipos,
  errorModo,
  errorTipo,
  disabled = false,
  compacta = false,
}: SeccionModoProps) {
  const id = useId();
  const tipoId = `${id}-tipo`;
  const errorTipoId = `${id}-tipo-error`;
  const errorModoId = `${id}-modo-error`;
  const Titulo = compacta ? "h3" : "h2";

  return (
    <section className={compacta ? "flex flex-col gap-3" : `${TARJETA_CLS} flex flex-col gap-4`}>
      <fieldset aria-describedby={errorModo ? errorModoId : undefined} className="flex flex-col gap-2">
        <legend className="mb-1">
          <Titulo className={compacta ? ETIQUETA_CLS : "text-lg font-semibold text-flit-primary dark:text-white"}>Modo</Titulo>
        </legend>
        {!compacta && (
          <p className={AYUDA_CLS}>Simple consulta las fuentes y entrega el consolidado. Con Análisis IA suma el dictamen para un tipo de trámite.</p>
        )}
        <div className="grid gap-2 sm:grid-cols-2">
          {MODOS.map((m) => (
            <label key={m.valor} className={OPCION_CLS}>
              <input
                type="radio"
                name={`${id}-modo`}
                value={m.valor}
                checked={modo === m.valor}
                onChange={() => onModo(m.valor)}
                disabled={disabled}
                className={MARCA_CLS}
              />
              {m.nombre}
            </label>
          ))}
        </div>
        {errorModo && (
          <p id={errorModoId} className={ERROR_CLS}>
            {errorModo}
          </p>
        )}
      </fieldset>

      {modo === "CON_ANALISIS" && campoTipo()}
    </section>
  );

  function campoTipo() {
    if (tipos.tipo === "cargando")
      return (
        <p role="status" className={AYUDA_CLS}>
          Cargando los tipos de trámite…
        </p>
      );
    if (tipos.tipo === "error") {
      const esperando = tipos.segundosRestantes > 0;
      return (
        <InlineAlert
          tone="error"
          title="No se pudieron cargar los tipos de trámite"
          action={
            <button type="button" onClick={onReintentarTipos} disabled={esperando} className={`${BOTON_REINTENTAR_CLS} disabled:cursor-not-allowed disabled:opacity-50`}>
              {esperando ? `Reintentar en ${tipos.segundosRestantes} s` : "Reintentar"}
            </button>
          }
        >
          {tipos.mensaje}
        </InlineAlert>
      );
    }
    if (tipos.tipos.length === 0) return <p className={AYUDA_CLS}>No hay tipos de trámite disponibles para el análisis.</p>;
    return (
      <div className="flex flex-col gap-1.5">
        <label htmlFor={tipoId} className={ETIQUETA_CLS}>
          Tipo de trámite{" "}
          <span aria-hidden="true" className="text-[var(--badge-danger-fg)]">
            *
          </span>
        </label>
        <select
          id={tipoId}
          value={tipoCodigo}
          onChange={(e) => onTipo(e.target.value)}
          required
          aria-invalid={errorTipo ? true : undefined}
          aria-describedby={errorTipo ? errorTipoId : undefined}
          disabled={disabled}
          className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
        >
          <option value="">Elige un tipo de trámite</option>
          {tipos.tipos.map((t) => (
            <option key={t.codigo} value={t.codigo}>
              {t.nombre}
            </option>
          ))}
        </select>
        {errorTipo && (
          <p id={errorTipoId} className={ERROR_CLS}>
            {errorTipo}
          </p>
        )}
      </div>
    );
  }
}
