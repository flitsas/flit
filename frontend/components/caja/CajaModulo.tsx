"use client";

// Módulo «Caja» de FLIT 2.0: pestañas «Cobrar» y «Cobros del día». La persona de caja (`ot_caja`) cobra y ve lo cobrado;
// el Admin OT además elige el día y anula con motivo. La autorización real la hace la API; aquí solo se decide qué se ve.
import { useMemo, useState } from "react";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { SectionTabs } from "@/components/atom/SectionTabs";
import { getToken } from "@/lib/api/client";
import { otCajaApi, type OtCajaApi } from "@/lib/api/ot-caja";
import { decodeJwtPayload, isOtAdmin } from "@/lib/auth/jwt";
import { CobrarTramite } from "./CobrarTramite";
import { CobrosDelDia } from "./CobrosDelDia";

type PestanaCaja = "cobrar" | "cobros";

export interface CajaModuloProps {
  /** Solo para pruebas: cliente ya resuelto. */
  api?: OtCajaApi;
  /** Solo para pruebas: por defecto sale del token (`ot_admin`). */
  puedeAnular?: boolean;
}

export function CajaModulo({ api = otCajaApi, puedeAnular }: CajaModuloProps) {
  const [pestana, setPestana] = useState<PestanaCaja>("cobrar");
  const [version, setVersion] = useState(0);
  const anula = useMemo(() => puedeAnular ?? isOtAdmin(decodeJwtPayload(getToken())), [puedeAnular]);

  return (
    <div className="flex flex-col gap-4">
      <CabeceraModulo
        icono="pago"
        titulo="Caja"
        descripcion="Cobra los trámites de tu organismo y consulta lo cobrado en el día."
      />
      <SectionTabs<PestanaCaja>
        ariaLabel="Secciones de la caja"
        active={pestana}
        onChange={setPestana}
        tabs={[
          { id: "cobrar", label: "Cobrar", content: <CobrarTramite api={api} onCobrado={() => setVersion((v) => v + 1)} /> },
          {
            id: "cobros",
            label: "Cobros del día",
            // Solo se monta con la pestaña abierta: cada vez que se entra, la lista se pide de nuevo.
            content: pestana === "cobros" ? <CobrosDelDia api={api} puedeAnular={anula} version={version} /> : null,
          },
        ]}
      />
    </div>
  );
}
