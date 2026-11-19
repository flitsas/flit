"use client";

import { useEffect } from "react";
import { Card } from "@/components/tramite/piezas";
import type { PropsSeccion } from "@/components/tramite/ContextoWizard";

/**
 * Renderer informativo de `plate_request` (placa solicitada tras la entrega). No captura nada: el trámite se radica como borrador y la
 * placa la asigna el organismo de tránsito después. Siempre está completo, así que no frena el avance.
 */
export function PlacaPosterior({ seccion, onEstado }: PropsSeccion) {
  useEffect(() => {
    onEstado({ completo: true });
  }, [onEstado]);

  return (
    <Card title={seccion.paso?.titulo || "Placa del vehículo"} id={`placa-posterior-${seccion.clave.replace(/\W+/g, "-")}`}>
      <p role="status" className="text-sm">
        La placa la asigna el organismo de tránsito después de la entrega del trámite. No necesitas hacer nada en este paso.
      </p>
    </Card>
  );
}
