"use client";

import { useId } from "react";
import { NOMBRE_MAXIMO } from "@/lib/diagnosticos/api";
import { AYUDA_CLS, CAMPO_CLS, ETIQUETA_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS } from "./estilos";

export interface SeccionInformacionGeneralProps {
  nombre: string;
  onNombre: (nombre: string) => void;
  error?: string;
  disabled?: boolean;
  compacta?: boolean;
}

/**
 * Sección Información general (HU #13752, F01-C1): el nombre del diagnóstico, obligatorio y de hasta 150 caracteres. Sin
 * maxLength para que un texto pegado más largo no se recorte en silencio: el contador y el error lo explican.
 */
export function SeccionInformacionGeneral({ nombre, onNombre, error, disabled = false, compacta = false }: SeccionInformacionGeneralProps) {
  const id = useId();
  const campoId = `${id}-nombre`;
  const ayudaId = `${id}-ayuda`;
  const errorId = `${id}-error`;
  const Titulo = compacta ? "h3" : "h2";
  const largo = nombre.trim().length;

  return (
    <section aria-labelledby={`${id}-titulo`} className={compacta ? "flex flex-col gap-3" : `${TARJETA_CLS} flex flex-col gap-4`}>
      <Titulo id={`${id}-titulo`} className={compacta ? ETIQUETA_CLS : "text-lg font-semibold text-flit-primary dark:text-white"}>
        Información general
      </Titulo>
      <div className="flex flex-col gap-1.5">
        <label htmlFor={campoId} className={ETIQUETA_CLS}>
          Nombre del diagnóstico{" "}
          <span aria-hidden="true" className="text-[var(--badge-danger-fg)]">
            *
          </span>
        </label>
        <input
          id={campoId}
          type="text"
          value={nombre}
          onChange={(e) => onNombre(e.target.value)}
          required
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : ayudaId}
          disabled={disabled}
          className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS}`}
        />
        {error ? (
          <p id={errorId} className={ERROR_CLS}>
            {error}
          </p>
        ) : (
          <p id={ayudaId} className={AYUDA_CLS}>
            {largo}/{NOMBRE_MAXIMO} caracteres.
          </p>
        )}
      </div>
    </section>
  );
}
