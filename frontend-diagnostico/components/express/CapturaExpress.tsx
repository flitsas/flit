"use client";

import { useId, useRef, useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { FileSpreadsheet, Zap } from "lucide-react";
import { errorIdentificador } from "@/lib/express/api";
import { limpiarIdentificador } from "@/lib/validation/fieldRules";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_SECUNDARIO_CLS, CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";
import { CAMPO_INVALIDO_CLS, ERROR_CLS } from "@/components/parametrizacion/estilos";

/**
 * Entrada a la creación desde la tarjeta del título del inicio (HU #13755; HU #14085): el usuario escribe una placa o un
 * VIN, que se ve normalizado mientras escribe (mayúsculas, solo letras y números, sin recortar), y con «Diagnosticar» se
 * valida con la regla de Trámites y el rechazo por longitud de Diagnóstico; si es válido abre /diagnosticos/nuevo en
 * Express con la placa precargada, donde se ajustan los parámetros. «Carga masiva» abre la misma pantalla en Masivo.
 */
export function CapturaExpress() {
  const router = useRouter();
  const id = useId();
  const campoRef = useRef<HTMLInputElement>(null);
  const [valor, setValor] = useState("");
  const [error, setError] = useState<string | null>(null);

  const campoId = `${id}-identificador`;
  const ayudaId = `${id}-ayuda`;
  const errorId = `${id}-error`;

  function alEnviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const mensaje = errorIdentificador(valor);
    setError(mensaje);
    if (mensaje) {
      campoRef.current?.focus();
      return;
    }
    router.push(`/diagnosticos/nuevo?modo=express&placa=${encodeURIComponent(valor)}`);
  }

  return (
    <form onSubmit={alEnviar} noValidate aria-label="Nuevo diagnóstico" className="flex w-full flex-col gap-1.5 lg:max-w-xl">
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
          }}
          required
          autoComplete="off"
          spellCheck={false}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : ayudaId}
          placeholder="ABC123"
          className={`${CAMPO_CLS} ${CAMPO_INVALIDO_CLS} font-semibold uppercase tracking-widest sm:flex-1`}
        />
        <button type="submit" className={BOTON_PRIMARIO_CLS}>
          <Zap className="h-4 w-4" aria-hidden="true" />
          Diagnosticar
        </button>
        <Link href="/diagnosticos/nuevo?modo=masivo" className={BOTON_SECUNDARIO_CLS}>
          <FileSpreadsheet className="h-4 w-4" aria-hidden="true" />
          Carga masiva
        </Link>
      </div>
      {error ? (
        <p id={errorId} className={ERROR_CLS}>
          {error}
        </p>
      ) : (
        <p id={ayudaId} className={AYUDA_CLS}>
          Un vehículo por placa o VIN, o varios desde la plantilla Excel con «Carga masiva».
        </p>
      )}
    </form>
  );
}
