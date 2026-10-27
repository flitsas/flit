import clsx from "clsx";

// Variantes del botón del diseño de referencia (components/ui/button.tsx), sin la dependencia de radix/cva.
const BASE =
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium cursor-pointer transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50 disabled:cursor-not-allowed [&_svg]:pointer-events-none [&_svg]:size-4 [&_svg]:shrink-0";

const VARIANTES = {
  flit: "rounded-full bg-primary text-primary-foreground font-semibold hover:bg-primary/90",
  back: "rounded-full bg-secondary text-secondary-foreground font-semibold hover:bg-secondary/90",
  institutional: "rounded-full bg-secondary-foreground text-secondary font-semibold hover:bg-secondary-foreground/90",
  ghost: "hover:bg-accent hover:text-accent-foreground",
  link: "text-primary underline-offset-4 hover:underline",
} as const;

export function boton(variante: keyof typeof VARIANTES, clases?: string, tamano: "default" | "none" = "default"): string {
  return clsx(BASE, VARIANTES[variante], tamano === "default" && "h-9 px-4 py-2", clases);
}
