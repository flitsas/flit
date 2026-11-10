"use client";

import { Search } from "lucide-react";
import { useRef, useState, type FormEvent } from "react";
import { boton } from "@/components/landing/estilos";
import { FORMATO_CODIGO } from "@/lib/caja/tipos";
import { MensajeCaja } from "./MensajeCaja";

type Props = {
  buscando: boolean;
  /** Mensaje del último intento (no encontrado, otro organismo, ya pagado, tope de intentos…). */
  mensaje: { texto: string; tono: "error" | "aviso" } | null;
  /** Recibe el código normalizado (mayúsculas, sin espacios). */
  onBuscar: (codigo: string) => void;
  /** Mientras dure el bloqueo por intentos no se puede buscar. */
  bloqueado?: boolean;
};

/** Campo del ID del trámite (`PN-AAAA-NNNNNN`). Enter busca; el foco empieza aquí. */
export function BuscadorCaja({ buscando, mensaje, onBuscar, bloqueado = false }: Props) {
  const [texto, setTexto] = useState("");
  const [formatoInvalido, setFormatoInvalido] = useState(false);
  const entrada = useRef<HTMLInputElement>(null);

  function enviar(e: FormEvent) {
    e.preventDefault();
    const codigo = texto.trim().toUpperCase();
    // Una búsqueda sin la forma del código no llega al servidor: cada fallo cuenta para el tope de intentos.
    if (!FORMATO_CODIGO.test(codigo)) {
      setFormatoInvalido(true);
      entrada.current?.focus();
      return;
    }
    setFormatoInvalido(false);
    onBuscar(codigo);
  }

  const mensajeVisible = formatoInvalido ? ({ texto: "No encontramos ese trámite.", tono: "error" } as const) : mensaje;

  return (
    <form onSubmit={enviar} noValidate aria-labelledby="titulo-buscar-caja" className="rounded-[9px] border border-border bg-card p-6">
      <h2 id="titulo-buscar-caja" className="mb-4 text-xl font-semibold">
        Buscar trámite
      </h2>
      <label htmlFor="id-tramite-caja" className="mb-1 block text-sm font-medium">
        ID del trámite
      </label>
      <div className="flex flex-wrap items-start gap-3">
        <div className="min-w-[14rem] flex-1">
          <input
            ref={entrada}
            id="id-tramite-caja"
            // El foco inicial es el campo del ID: la persona de caja llega a buscar.
            autoFocus
            className="field"
            inputMode="text"
            autoComplete="off"
            spellCheck={false}
            placeholder="PN-2026-000123"
            value={texto}
            maxLength={20}
            aria-invalid={mensajeVisible?.tono === "error" ? true : undefined}
            aria-describedby={mensajeVisible ? "mensaje-buscar-caja" : undefined}
            onChange={(e) => {
              setTexto(e.target.value);
              setFormatoInvalido(false);
            }}
          />
        </div>
        <button type="submit" className={boton("flit", "min-h-11 px-6", "none")} disabled={buscando || bloqueado || texto.trim() === ""}>
          <Search aria-hidden />
          Buscar
        </button>
      </div>
      {buscando && (
        <p role="status" className="mt-3 text-sm text-muted-foreground">
          Buscando el trámite…
        </p>
      )}
      {mensajeVisible && !buscando && (
        <div className="mt-3">
          <MensajeCaja id="mensaje-buscar-caja" tono={mensajeVisible.tono}>
            {mensajeVisible.texto}
          </MensajeCaja>
        </div>
      )}
    </form>
  );
}
