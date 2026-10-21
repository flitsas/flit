"use client";

import { useId, useRef, useState, type FormEvent } from "react";
import { Zap } from "lucide-react";
import { errorIdentificador } from "@/lib/express/api";
import { limpiarIdentificador } from "@/lib/validation/fieldRules";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, CAMPO_CLS, ETIQUETA_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS } from "@/components/parametrizacion/estilos";
import { PanelExpress } from "./PanelExpress";

export interface CapturaExpressProps {
  /** Fecha del nombre autogenerado; por defecto, ahora. */
  fecha?: Date;
}

/**
 * Bloque Express del home (HU #13755, F02-H8): el usuario escribe una placa o un VIN, que se ve normalizado mientras
 * escribe (mayúsculas, solo letras y números, sin recortar), y con «Diagnosticar» se valida con la regla de Trámites y el
 * rechazo por longitud de Diagnóstico antes de abrir el panel de parametrización. El servidor vuelve a validar: su 422
 * del identificador se pinta en este mismo campo. Editar el identificador cierra el panel (el nombre dependía de él).
 */
export function CapturaExpress({ fecha }: CapturaExpressProps) {
  const id = useId();
  const campoRef = useRef<HTMLInputElement>(null);
  const [valor, setValor] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [abierto, setAbierto] = useState<string | null>(null);

  const campoId = `${id}-identificador`;
  const ayudaId = `${id}-ayuda`;
  const errorId = `${id}-error`;

  function alEnviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const mensaje = errorIdentificador(valor);
    setError(mensaje);
    if (mensaje) {
      setAbierto(null);
      campoRef.current?.focus();
      return;
    }
    setAbierto(valor);
  }

  function alErrorApi(mensaje: string) {
    setError(mensaje);
    campoRef.current?.focus();
  }

  return (
    <section aria-labelledby={`${id}-titulo`} className={`${TARJETA_CLS} flex flex-col gap-4`}>
      <div className="flex items-center gap-2">
        <Zap className="h-5 w-5 text-flit-brand" aria-hidden="true" />
        <h2 id={`${id}-titulo`} className="text-lg font-semibold text-flit-primary dark:text-white">
          Diagnóstico Express
        </h2>
      </div>
      <form onSubmit={alEnviar} noValidate aria-label="Captura del vehículo" className="flex flex-col gap-1.5">
        <label htmlFor={campoId} className={ETIQUETA_CLS}>
          Placa o VIN{" "}
          <span aria-hidden="true" className="text-[var(--badge-danger-fg)]">
            *
          </span>
        </label>
        <div className="flex flex-col gap-2 sm:flex-row">
          <input
            ref={campoRef}
            id={campoId}
            type="text"
            value={valor}
            onChange={(e) => {
              setValor(limpiarIdentificador(e.target.value));
              setError(null);
              setAbierto(null);
            }}
            required
            autoComplete="off"
            spellCheck={false}
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? errorId : ayudaId}
            placeholder="ABC123"
            className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} font-semibold uppercase tracking-widest sm:max-w-xs`}
          />
          <button type="submit" className={BOTON_PRIMARIO_CLS}>
            Diagnosticar
          </button>
        </div>
        {error ? (
          <p id={errorId} className={ERROR_CLS}>
            {error}
          </p>
        ) : (
          <p id={ayudaId} className={AYUDA_CLS}>
            Escribe la placa o el VIN de un vehículo para diagnosticarlo sin preparar un archivo.
          </p>
        )}
      </form>
      {abierto && <PanelExpress key={abierto} identificador={abierto} fecha={fecha} onErrorIdentificador={alErrorApi} />}
    </section>
  );
}
