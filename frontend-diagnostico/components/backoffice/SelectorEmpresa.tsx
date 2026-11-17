"use client";

import { useId, useState } from "react";
import { Search } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";
import { esEmpresaValida } from "@/lib/configuracion";

export interface SelectorEmpresaProps {
  /** Compañía elegida (uuid) o vacío. */
  valor: string;
  /** Se llama solo con un identificador válido; nunca con uno vacío o mal formado. */
  onElegir: (empresa: string) => void;
}

/**
 * Selector explícito de la compañía del backoffice (HU #13802, F06-C14). Diagnóstico no expone todavía un listado de
 * compañías (ni la API ni el frontend tienen esa fuente), así que se elige por su identificador, como en el contrato de la
 * pantalla 10 (HU #13744): uuid distinto de cero, validado antes de consultar. El elegido viaja como ?empresa= en cada llamada.
 */
export function SelectorEmpresa({ valor, onElegir }: SelectorEmpresaProps) {
  const id = useId();
  const [texto, setTexto] = useState(valor);
  const [invalida, setInvalida] = useState(false);

  return (
    <form
      noValidate
      aria-label="Compañía del backoffice"
      className="flex flex-col gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        const empresa = texto.trim();
        if (!esEmpresaValida(empresa)) {
          setInvalida(true);
          return;
        }
        setInvalida(false);
        onElegir(empresa);
      }}
    >
      <label htmlFor={`${id}-empresa`} className={ETIQUETA_CLS}>
        Identificador de la compañía
      </label>
      <div className="flex flex-wrap items-start gap-2">
        <input
          id={`${id}-empresa`}
          type="text"
          inputMode="text"
          spellCheck={false}
          autoComplete="off"
          placeholder="00000000-0000-0000-0000-000000000000"
          value={texto}
          onChange={(e) => setTexto(e.target.value)}
          aria-invalid={invalida || undefined}
          aria-describedby={invalida ? `${id}-error` : `${id}-ayuda`}
          className={`${CAMPO_CLS} max-w-md font-mono`}
        />
        <button type="submit" className={BOTON_PRIMARIO_CLS}>
          <Search className="h-4 w-4" aria-hidden="true" />
          Ver lotes
        </button>
      </div>
      <p id={`${id}-ayuda`} className={AYUDA_CLS}>
        Escribe el identificador de la compañía cliente. Cada consulta queda registrada con tu usuario y la compañía.
      </p>
      {invalida && (
        <InlineAlert tone="error" compact id={`${id}-error`}>
          Escribe un identificador válido de compañía (uuid).
        </InlineAlert>
      )}
    </form>
  );
}
