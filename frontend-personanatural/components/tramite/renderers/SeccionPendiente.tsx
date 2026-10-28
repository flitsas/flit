"use client";

import { useEffect } from "react";
import { Card } from "@/components/tramite/piezas";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { grupoDePendiente, GRUPO_POR_SECCION, type GrupoPaso } from "@/lib/tramite/pasos";

/**
 * Marcador de una sección cuyo renderer aún no está construido (pago: HU F5; envío: F6). No dibuja
 * campos a medias ni inventa un diseño: avisa con un estado accesible y deja avanzar solo si el back ya da por completo lo que
 * esa sección pide (por ejemplo, un borrador reanudado), para no atrapar a nadie.
 */
export function SeccionPendiente({ seccion, onEstado }: PropsSeccion) {
  const { definicion, solicitud } = useWizard();
  const grupo: GrupoPaso = (GRUPO_POR_SECCION[seccion.sectionType] ?? seccion.sectionType) as GrupoPaso;
  const pendientes = solicitud.pendientes.filter((p) => grupoDePendiente(p, definicion) === grupo);
  const propia = seccion.paso === null;
  // Pago y Envío no tienen un pendiente que los cierre aquí: nunca se dan por completos desde el marcador.
  const completo = !propia && pendientes.length === 0;

  useEffect(() => {
    onEstado({ completo, bloqueo: completo ? undefined : "Esta sección aún no está disponible en el portal." });
  }, [completo, onEstado]);

  return (
    <Card title={seccion.paso?.titulo || undefined}>
      <p role="status" className="text-sm text-muted-foreground">
        Esta sección aún no está disponible en el portal. La habilitaremos en una próxima entrega.
      </p>
    </Card>
  );
}
