"use client";

import { useEffect, useState } from "react";
import { notFound, useRouter } from "next/navigation";
import {
  confirmacionRuntTabPath,
  defaultConfirmacionRuntTab,
} from "@/components/admin/plataforma/confirmacion-runt/confirmacion-runt-nav";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload } from "@/lib/auth/jwt";

/**
 * Raíz de Plataforma → Confirmación RUNT (HU #12313): no tiene contenido propio, aterriza en la
 * primera pestaña que el usuario puede ver (Configuración → Historial). Sin ninguna → 404 del
 * segmento. `notFound()` se lanza en render (no dentro del efecto) para que lo atrape el
 * boundary del segmento.
 */
export default function AdminConfirmacionRuntIndexPage() {
  const router = useRouter();
  const [missing, setMissing] = useState(false);

  useEffect(() => {
    const tab = defaultConfirmacionRuntTab(decodeJwtPayload(getToken()));
    if (tab === null) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setMissing(true);
      return;
    }
    router.replace(confirmacionRuntTabPath(tab));
  }, [router]);

  if (missing) notFound();
  return null;
}
