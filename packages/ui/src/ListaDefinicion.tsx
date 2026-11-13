import type { ReactNode } from "react";
import { cn } from "./cn";

/** Lista de definición dt/dd (HU #14562 · UI-1): 1 o 2 columnas; un valor ausente muestra su texto vacío. */
export interface ItemDefinicion {
  termino: ReactNode;
  valor?: ReactNode;
  vacio?: string;
}

export interface ListaDefinicionProps {
  items: ItemDefinicion[];
  columnas?: 1 | 2;
  vacio?: string;
  vacioLista?: string;
  className?: string;
}

const ausente = (v: ReactNode) => v === null || v === undefined || v === "" || v === false;

export function ListaDefinicion({
  items,
  columnas = 1,
  vacio = "Sin dato",
  vacioLista = "Sin información",
  className,
}: ListaDefinicionProps) {
  if (items.length === 0) return <p className="text-sm text-[color:var(--di-text-tertiary)]">{vacioLista}</p>;
  return (
    <dl
      data-columnas={String(columnas)}
      className={cn("grid gap-x-6 gap-y-2 text-sm", columnas === 2 ? "sm:grid-cols-2" : "grid-cols-1", className)}
    >
      {items.map((item, i) => (
        <div key={i} className="flex min-w-0 flex-col gap-0.5">
          <dt className="text-xs font-medium text-[color:var(--di-text-tertiary)]">{item.termino}</dt>
          {ausente(item.valor) ? (
            <dd className="text-[color:var(--di-text-tertiary)]">{item.vacio ?? vacio}</dd>
          ) : (
            <dd className="break-words text-[color:var(--di-text-primary)]">{item.valor}</dd>
          )}
        </div>
      ))}
    </dl>
  );
}
