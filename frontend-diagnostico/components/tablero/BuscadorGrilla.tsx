"use client";

import { useId, type Ref } from "react";
import { CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";

/**
 * Búsqueda de la grilla por placa o VIN (HU #14562 AC28). Filtra en el cliente; no consulta la API. `ref` al campo: «Limpiar
 * filtros» le devuelve el foco (HU #14584 §6.6).
 */
export function BuscadorGrilla({ valor, onCambiar, ref }: { valor: string; onCambiar: (valor: string) => void; ref?: Ref<HTMLInputElement> }) {
  const id = useId();
  return (
    <div className="flex max-w-xs flex-col gap-1 max-[640px]:w-full max-[640px]:max-w-none">
      <label htmlFor={id} className={ETIQUETA_CLS}>
        Buscar por placa o VIN
      </label>
      <input
        ref={ref}
        id={id}
        type="search"
        value={valor}
        onChange={(e) => onCambiar(e.target.value)}
        autoComplete="off"
        spellCheck={false}
        className={CAMPO_CLS}
      />
    </div>
  );
}
