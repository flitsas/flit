import { AlertTriangle, Check, Clock3 } from "lucide-react";
import type { EstadoPasoTracking } from "@/lib/api/cliente";

export interface PasoVisible {
  titulo: string;
  estado: EstadoPasoTracking;
  /** Ya formateada (dd/mm/aaaa hh:mm) o null si el paso aún no ocurrió. */
  fecha: string | null;
}

const ESTILOS: Record<EstadoPasoTracking, string> = {
  done: "border-success bg-success text-primary-foreground",
  current: "border-warning bg-warning/10 text-warning motion-safe:animate-pulse",
  rejected: "border-alert bg-alert/10 text-alert",
  pending: "border-disabled text-disabled",
  skipped: "border-disabled border-dashed text-disabled",
};
const TEXTOS: Record<EstadoPasoTracking, string> = {
  done: "text-success",
  current: "text-warning",
  rejected: "text-alert",
  pending: "text-disabled",
  skipped: "text-disabled",
};
const ETIQUETA_ESTADO: Record<EstadoPasoTracking, string> = {
  done: "Completado",
  current: "En curso",
  rejected: "Rechazado",
  pending: "Pendiente",
  skipped: "Omitido",
};

/** Línea de seguimiento del radicado con título, fecha y hora por paso (mismo diseño del repo de referencia). A 360 px se apila en 2 columnas. */
export function RadicadoTracking({ pasos }: { pasos: PasoVisible[] }) {
  return (
    <ol aria-label="Tracking del radicado" className="my-5 grid grid-cols-2 gap-4 sm:grid-cols-5 sm:gap-2">
      {pasos.map((paso, i) => (
        <li key={`${i}-${paso.titulo}`} data-status={paso.estado} aria-current={paso.estado === "current" || paso.estado === "rejected" ? "step" : undefined} className="relative flex min-w-0 flex-col items-center gap-2 text-center">
          {i < pasos.length - 1 && <span aria-hidden className={`timeline-connector absolute top-5 hidden h-0.5 sm:block ${paso.estado === "done" ? "bg-success" : "bg-disabled/40"}`} />}
          <span className={`relative grid h-10 w-10 place-items-center rounded-full border-2 ${ESTILOS[paso.estado]}`}>
            {paso.estado === "done" ? <Check aria-hidden className="h-5 w-5" /> : paso.estado === "rejected" ? <AlertTriangle aria-hidden className="h-5 w-5" /> : paso.estado === "current" ? <Clock3 aria-hidden className="h-5 w-5" /> : i + 1}
          </span>
          <span className={`text-xs font-semibold leading-snug ${TEXTOS[paso.estado]}`}>
            {paso.titulo}
            <span className="sr-only">: {ETIQUETA_ESTADO[paso.estado]}</span>
          </span>
          <span className="text-[10px] text-foreground/70">{paso.estado === "skipped" ? "Omitido" : (paso.fecha ?? "Sin fecha")}</span>
        </li>
      ))}
    </ol>
  );
}
