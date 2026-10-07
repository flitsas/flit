import { AlertCircle } from "lucide-react";
import type { LinkTerminalKind } from "@/lib/captura-manual/types";

const COPY: Record<LinkTerminalKind, { title: string; body: string }> = {
  expirada: {
    title: "Este enlace venció",
    body: "Pídele a FLIT 2.0 que te envíe uno nuevo para continuar.",
  },
  reemplazado: {
    title: "Este enlace fue reemplazado",
    body: "Te enviamos uno más reciente. Usa el último correo que recibiste.",
  },
  estado_invalido: {
    title: "Este enlace ya no se puede usar",
    body: "Ya se usó o no está disponible. Si necesitas repetir el proceso, pídele a FLIT 2.0 un enlace nuevo.",
  },
  not_found: {
    title: "No encontramos este enlace",
    body: "Revisa que lo hayas copiado completo o pídele a FLIT 2.0 uno nuevo.",
  },
};

/** Estado terminal del enlace: mensaje corto, sin datos personales y sin pasos. */
export function LinkTerminal({ kind }: { kind: LinkTerminalKind }) {
  const { title, body } = COPY[kind];
  return (
    <div role="alert" className="flex flex-col items-center gap-3 py-6 text-center" data-testid={`terminal-${kind}`}>
      <AlertCircle aria-hidden="true" className="size-10 text-flit-alert" />
      <h1 className="text-xl font-bold text-flit-primary">{title}</h1>
      <p className="text-base text-muted-foreground">{body}</p>
    </div>
  );
}
