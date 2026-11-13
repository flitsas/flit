"use client";

import { useId } from "react";
import { CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";

/** Búsqueda de la grilla por placa o VIN (HU #14562 AC28). Filtra en el cliente; no consulta la API. */
export function BuscadorGrilla({ valor, onCambiar }: { valor: string; onCambiar: (valor: string) => void }) {
  const id = useId();
  return (
    <div className="flex max-w-xs flex-col gap-1">
      <label htmlFor={id} className={ETIQUETA_CLS}>
        Buscar por placa o VIN
      </label>
      <input
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
