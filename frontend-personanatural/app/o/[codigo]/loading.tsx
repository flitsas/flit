import { MobilityLoader } from "@/components/MobilityLoader";

/** Misma pantalla de espera que el trámite del portal (`PaginaTramite`). */
export default function CargandoCanal() {
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
      <MobilityLoader />
    </div>
  );
}
