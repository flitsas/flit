"use client";

import { useEffect, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";

/** Código de la 403 cuando la sesión no trae la capacidad de backoffice (la página muestra su texto por defecto). */
export const SIN_PERMISO_BACKOFFICE = "SIN_PERMISO_BACKOFFICE";

/**
 * Puerta de las rutas /backoffice/* (HU #13802, F06-C7): solo con la capacidad `backoffice` de GET /sesion (permiso
 * diagnostico.backoffice.ver y compañía FLIT, HU #13724). Sin ella (p. ej. el admin de una compañía) lleva a la página 403 y
 * no muestra la pantalla. Es ayuda de interfaz: cada endpoint del backoffice vuelve a exigir su política.
 */
export function AccesoBackoffice({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { sesion } = useSesion();
  const permitido = sesion?.capacidades.backoffice === true;
  const negado = sesion != null && !permitido;

  useEffect(() => {
    if (negado) router.replace(`/403?code=${SIN_PERMISO_BACKOFFICE}`);
  }, [negado, router]);

  if (!permitido) return <VehicleLoader label="Abriendo el backoffice…" />;
  return <>{children}</>;
}
