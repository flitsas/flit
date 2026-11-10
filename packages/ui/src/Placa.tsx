import type { HTMLAttributes } from "react";
import { cn } from "./cn";
import { rutaIlustracion } from "./ilustraciones";

/**
 * Placa colombiana del prototipo «Diseño Inteligente» (`.placa` de propuesta.css), HU #14476: fondo amarillo
 * reflectivo (`--placa-fondo`, #ffd100) con el filete negro de `placa-colombia.webp`, los caracteres en Barlow
 * Condensed (`--font-placa`) como texto real —para que se lean, se copien y los anuncie el lector de pantalla— y la
 * ciudad en pequeño debajo si viene. Es el único amarillo de la propuesta: representa el objeto real.
 */
export type PlacaSize = "md" | "lg";

const SIZE: Record<PlacaSize, { caja: string; num: string; ciudad: string }> = {
  md: { caja: "w-[92px] rounded-[5px] px-1.5 pt-0.5 pb-[3px]", num: "text-[1.375rem]", ciudad: "text-[0.5rem]" },
  lg: { caja: "w-[156px] rounded-lg px-2.5 pt-1.5 pb-[7px]", num: "text-[2.25rem]", ciudad: "text-xs" },
};

/** «ABC123» → «ABC 123», como el prototipo; cualquier otro formato se deja tal cual (en mayúsculas). */
export function formatoPlaca(placa: string): string {
  const limpia = placa.trim().toUpperCase();
  return /^[A-Z]{3}\d{2}[0-9A-Z]$/.test(limpia) ? `${limpia.slice(0, 3)} ${limpia.slice(3)}` : limpia;
}

export interface PlacaProps extends Omit<HTMLAttributes<HTMLSpanElement>, "children"> {
  /** Placa del vehículo (con o sin espacio). */
  placa: string;
  /** Municipio de matrícula, en pequeño bajo los caracteres. */
  ciudad?: string | null;
  /** `md` (92 px, tablas y listas) o `lg` (156 px, cabecera del vehículo). */
  size?: PlacaSize;
  /**
   * Separa letras y números como el prototipo («ABC123» → «ABC 123»). Por defecto NO: el rediseño no cambia textos
   * visibles, así que la placa se muestra tal como llega. Solo para pantallas nuevas que lo pidan.
   */
  formatear?: boolean;
}

export function Placa({ placa, ciudad, size = "md", formatear = false, className, style, ...rest }: PlacaProps) {
  const s = SIZE[size];
  return (
    <span
      className={cn(
        "inline-flex aspect-[33/16] flex-col items-center justify-center gap-px whitespace-nowrap align-middle",
        "text-[color:var(--placa-texto)] shadow-[var(--shadow-card)] [font-family:var(--font-placa)]",
        s.caja,
        className,
      )}
      style={{
        background: `var(--placa-fondo) url("${rutaIlustracion("placa-colombia")}") center / 100% 100% no-repeat`,
        ...style,
      }}
      {...rest}
    >
      <span className={cn("font-bold leading-none tracking-[0.04em]", s.num)}>{formatear ? formatoPlaca(placa) : placa}</span>
      {ciudad ? (
        <span className={cn("font-semibold uppercase leading-none tracking-[0.1em]", s.ciudad)}>{ciudad}</span>
      ) : null}
    </span>
  );
}
