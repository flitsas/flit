import { StatusBadge } from "@flit/ui";
import type { Etiqueta } from "@/lib/etiquetas";

/** Chip de estado con la etiqueta y el tono de lib/etiquetas (§4.9). */
export function Chip({ e, title }: { e: Etiqueta; title?: string }) {
  return <StatusBadge label={e.texto} tone={e.tono} title={title} />;
}
