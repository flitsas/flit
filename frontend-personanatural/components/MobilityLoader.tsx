import { Car } from "lucide-react";

// Cargador del diseño de referencia (transfer-step-trail): el carro orbita; con «reducir movimiento» queda quieto.
// Es el ÚNICO indicador de carga del portal: tres tamaños del mismo carrito.
//  - MobilityLoader: bloque (pantallas, modales, listas). `compacto` lo reduce para huecos pequeños.
//  - MobilityMini: solo el carro girando (20 px), para botones y campos; hereda el color del texto.
//  - MobilityEstado: el carro mini + un mensaje de una línea («Calculando…», «Guardando borrador…»).

export function MobilityLoader({ mensaje = "Cargando...", compacto = false }: { mensaje?: string; compacto?: boolean }) {
  return (
    <div role="status" aria-live="polite" className={compacto ? "flex items-center justify-center gap-3 py-2" : "flex flex-col items-center gap-5 py-8"}>
      <div className={compacto ? "mobility-track mobility-track-sm" : "mobility-track"}>
        <div className="mobility-orbit">
          <Car aria-hidden className={compacto ? "h-4 w-4 text-accent" : "h-6 w-6 text-accent"} />
        </div>
      </div>
      <p className="text-sm">{mensaje}</p>
    </div>
  );
}

/** El carro girando en pequeño. Decorativo: quien lo usa debe anunciar el estado (texto o `role="status"`). */
export function MobilityMini({ className = "" }: { className?: string }) {
  return (
    <span aria-hidden className={`mobility-track mobility-track-xs ${className}`.trim()}>
      <span className="mobility-orbit">
        <Car className="h-3 w-3" />
      </span>
    </span>
  );
}

/** Aviso de una línea con el carro: para «Calculando…», «Enviando…», «Guardando…» y similares. */
export function MobilityEstado({ mensaje, className = "" }: { mensaje: string; className?: string }) {
  return (
    <span role="status" className={`inline-flex items-center gap-2 ${className}`.trim()}>
      <MobilityMini />
      <span>{mensaje}</span>
    </span>
  );
}
