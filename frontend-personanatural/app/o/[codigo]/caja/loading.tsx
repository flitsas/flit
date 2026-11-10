import { MobilityLoader } from "@/components/MobilityLoader";

export default function CargandoCaja() {
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
      <MobilityLoader mensaje="Abriendo la caja…" />
    </div>
  );
}
