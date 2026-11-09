"use client";

// HU #14390 (Feature #14387, Épica #14297) — «Precios» del portal en el hub del OT. Es el mismo módulo de Tarifas de
// Plataforma (TarifasTab, formulario e historial) con el cliente del OT inyectado: una sola organización, sin
// selector de organismo, valores generales visibles y no editables, y los cargos que el servidor no deja cambiar
// bloqueados. El permiso real lo decide el servidor (`editable`); el front solo refleja y apaga lo que no aplica.
//   - ot_admin: su organismo sale del token; servicio FLIT y envío no están activos.
//   - SuperAdmin: edita todos los cargos del organismo del hub (`transitOfficeId`).
import { useMemo } from "react";
import { usePermissions } from "@/hooks/usePermissions";
import { TarifasTab, type TarifasTabProps } from "@/components/admin/plataforma/persona-natural/TarifasTab";
import type {
  TarifasApi,
  TarifasModo,
} from "@/components/admin/plataforma/persona-natural/tarifas-api";
import {
  fetchHistorialTarifaOt,
  fetchTarifasOt,
  fetchTiposTarifaOt,
  registrarTarifasOt,
} from "@/lib/api/admin-ot-portal-tarifas";

export interface OtPreciosSectionProps
  extends Pick<TarifasTabProps, "onCambio" | "solicitudPrecio" | "onSolicitudCerrada"> {
  transitOfficeId: string;
}

const MODO_OT_ADMIN: TarifasModo = {
  organismoFijo: true,
  sinPaso: true,
  conceptosBloqueados: ["SERVICIO_FLIT", "ENVIO_DOCUMENTO"],
  titulo: "Precios",
  descripcion:
    "Cuánto paga la persona por cada trámite de tu organismo. Si un trámite no tiene precio propio, rige el valor general. Un cambio queda en el historial y se ve en el portal de inmediato.",
};

const MODO_SUPERADMIN: TarifasModo = {
  ...MODO_OT_ADMIN,
  conceptosBloqueados: [],
  etiquetaPropia: "De este organismo",
  descripcion:
    "Cuánto paga la persona por cada trámite de este organismo. Si un trámite no tiene precio propio, rige el valor general. Un cambio queda en el historial y se ve en el portal de inmediato.",
};

export function OtPreciosSection({ transitOfficeId, ...puente }: OtPreciosSectionProps) {
  const { isSuperAdmin } = usePermissions();

  const api = useMemo<TarifasApi>(() => {
    // El ot_admin no envía el organismo: lo toma el servidor de su token.
    const scope = isSuperAdmin ? { transitOfficeId } : undefined;
    return {
      listar: (filtro, signal) => fetchTarifasOt(filtro, scope, signal),
      historial: (id, signal) => fetchHistorialTarifaOt(id, scope, signal),
      registrar: (body) => registrarTarifasOt(body, scope),
      catalogos: async (signal) => ({
        tipos: await fetchTiposTarifaOt(scope, signal),
        organismos: [],
      }),
    };
  }, [isSuperAdmin, transitOfficeId]);

  return <TarifasTab api={api} modo={isSuperAdmin ? MODO_SUPERADMIN : MODO_OT_ADMIN} {...puente} />;
}
