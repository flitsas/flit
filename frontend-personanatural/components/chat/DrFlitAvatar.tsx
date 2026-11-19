import clsx from "clsx";
import { Bot } from "lucide-react";

/**
 * Foto redonda de Dr. FLIT. Mientras no se entregue la imagen (`origen` nulo, ver ASSETS_DR_FLIT en lib/assets.ts) dibuja un
 * marcador: un ícono en un círculo. Con `nombre` se anuncia como imagen; sin él es decorativa (el botón ya tiene su etiqueta).
 */
export function DrFlitAvatar({ origen, tamano, nombre }: { origen: string | null; tamano: "h-14 w-14" | "h-10 w-10"; nombre?: string }) {
  if (origen) {
    // eslint-disable-next-line @next/next/no-img-element -- imagen de 40-56 px del diseño: no hay nada que optimizar
    return <img src={origen} alt={nombre ?? ""} className={clsx(tamano, "shrink-0 rounded-full object-cover")} />;
  }
  return (
    <span {...(nombre ? { role: "img", "aria-label": nombre } : { "aria-hidden": true })} data-marcador="dr-flit" className={clsx(tamano, "grid shrink-0 place-items-center rounded-full bg-primary-foreground/20 text-primary-foreground")}>
      <Bot aria-hidden className="h-6! w-6!" />
    </span>
  );
}
