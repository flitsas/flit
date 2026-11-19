"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { VehicleLoader } from "@flit/ui/Loader";

/** Gestión sin ?empresa= válida: vuelve a la bandeja de saneamientos (spec-ui §6). */
export function VolverABandeja() {
  const router = useRouter();
  useEffect(() => router.replace("/backoffice/saneamientos"), [router]);
  return <VehicleLoader label="Volviendo a la bandeja…" />;
}
