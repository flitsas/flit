import { Car } from "lucide-react";

// Cargador del diseño de referencia (transfer-step-trail): el carro orbita; con «reducir movimiento» queda quieto.
export function MobilityLoader() {
  return (
    <div role="status" aria-live="polite" className="flex flex-col items-center gap-5 py-8">
      <div className="mobility-track">
        <div className="mobility-orbit">
          <Car aria-hidden className="h-6 w-6 text-accent" />
        </div>
      </div>
      <p className="text-sm">Cargando...</p>
    </div>
  );
}
