import { cn } from "./cn";

/** Barra de progreso determinada (HU #14562 · UI-1): role="progressbar" con valores y texto accesibles. */
export interface BarraProgresoProps {
  etiqueta: string;
  valor: number;
  maximo: number;
  textoValor?: string;
  className?: string;
}

export function BarraProgreso({ etiqueta, valor, maximo, textoValor, className }: BarraProgresoProps) {
  const max = Math.max(0, maximo);
  const actual = Math.min(Math.max(0, valor), max);
  const pct = max === 0 ? 0 : Math.round((actual / max) * 100);
  return (
    <div
      role="progressbar"
      aria-label={etiqueta}
      aria-valuenow={actual}
      aria-valuemin={0}
      aria-valuemax={max}
      aria-valuetext={textoValor}
      className={cn("h-2 w-full overflow-hidden rounded-full bg-[var(--kit-fondo)]", className)}
    >
      <div
        data-parte="relleno"
        style={{ width: `${pct}%` }}
        className="h-full rounded-full bg-[color:var(--kit-azul-relleno)] transition-[width] duration-[var(--di-motion-base)] ease-[var(--di-ease)] motion-reduce:transition-none"
      />
    </div>
  );
}
