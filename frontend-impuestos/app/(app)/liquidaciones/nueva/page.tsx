"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { TARJETA_CLS } from "@/components/comun/estilos";
import { useSesion } from "@/components/ImpuestosShell";
import { FormularioPlaca } from "@/components/liquidaciones/FormularioPlaca";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { bloqueoCreacion } from "@/lib/liquidaciones/estados";
import { PERMISOS, tienePermiso } from "@/lib/sesion";

/** /liquidaciones/nueva (plan §6.1, spec-visual §3.3): liquidar una placa; solo la placa (D-ENTRADA, F2a-CF1). */
export default function PaginaNuevaLiquidacion() {
  const router = useRouter();
  const params = useSearchParams();
  const { sesion } = useSesion();
  const { empresa } = useEmpresaSeleccionada();
  const volver = hrefConEmpresa("/liquidaciones", empresa);
  return (
    <>
      <CabeceraModulo titulo="Liquidar por placa" icono="pesos" miga={[{ etiqueta: "Liquidaciones", href: volver }, { etiqueta: "Liquidar por placa" }]} />
      <section aria-label="Liquidar por placa" className={TARJETA_CLS}>
        {tienePermiso(sesion, PERMISOS.liquidacionesCrear) ? (
          <FormularioPlaca
            empresa={empresa}
            bloqueo={bloqueoCreacion(sesion)}
            placaInicial={params?.get("placa") ?? ""}
            autoFocus
            onCancelar={() => router.push(volver)}
          />
        ) : (
          <p className="text-sm text-muted-foreground">No tienes permiso para crear liquidaciones.</p>
        )}
      </section>
    </>
  );
}
