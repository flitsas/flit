import { AlertTriangle, CheckCircle2, Info } from "lucide-react";
import type { ReactNode } from "react";

export type TonoCaja = "error" | "aviso" | "exito";

const ESTILO: Record<TonoCaja, string> = {
  // Rojo #C10000 (flit-red), amarillo #F9AC00 (flit-yellow) y verde #8CC63F (flit-green) salen de los tokens del portal.
  error: "border-destructive bg-card text-destructive",
  aviso: "border-warning bg-warning text-secondary",
  exito: "border-success bg-success text-secondary",
};

const ICONO = { error: AlertTriangle, aviso: Info, exito: CheckCircle2 } as const;

/** Mensaje corto de la caja. Los errores se anuncian al instante (`alert`); el resto, con cortesía (`status`). */
export function MensajeCaja({ tono, children, id }: { tono: TonoCaja; children: ReactNode; id?: string }) {
  const Icono = ICONO[tono];
  return (
    <div
      id={id}
      role={tono === "error" ? "alert" : "status"}
      className={`flex items-start gap-2 rounded-[9px] border px-4 py-3 text-sm font-semibold ${ESTILO[tono]}`}
    >
      <Icono aria-hidden className="mt-0.5 h-4 w-4 shrink-0" />
      <p>{children}</p>
    </div>
  );
}
