"use client";

// HU #14455 / #14462 (Feature #14453) — pestaña «Caja» de Portal Persona Natural. Qué subpestañas ve cada quien:
//  - Admin OT:        Cobrar · Cobros del día (con Anular y selector de día) · Usuarios de caja.
//  - SuperAdmin:      Usuarios de caja · Cobros de caja (administración, con `transitOfficeId`); no cobra.
//  - Persona de caja: Cobrar · Cobros del día (sin Anular ni selector de día). Nunca llama a APIs de administración.
// La autorización real la hace la API; aquí solo se decide qué se ve y qué se monta.
import { useMemo, useState } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";
import { CobrarTramite } from "@/components/caja/CobrarTramite";
import { CobrosDelDia } from "@/components/caja/CobrosDelDia";
import { getToken } from "@/lib/api/client";
import { otCajaApi, type OtCajaApi } from "@/lib/api/ot-caja";
import { decodeJwtPayload, isSoloCaja, isSuperAdmin } from "@/lib/auth/jwt";
import { OtCajaCobrosSection } from "./OtCajaCobrosSection";
import { OtCajaUsuariosSection } from "./OtCajaUsuariosSection";

export type CajaSubId = "cobrar" | "cobros" | "usuarios" | "cobros-admin";
export type CajaRol = "superadmin" | "admin" | "cajero";

/** Subpestaña de la URL (`?sub=`) → id interno. «cobros» del SuperAdmin es la lista de administración. */
export function subDesdeQuery(valor: string | null | undefined, rol: CajaRol): CajaSubId | undefined {
  if (valor === "cobrar" && rol !== "superadmin") return "cobrar";
  if (valor === "cobros") return rol === "superadmin" ? "cobros-admin" : "cobros";
  if (valor === "usuarios" && rol !== "cajero") return "usuarios";
  return undefined;
}

/** Valor de `?sub=` para una subpestaña. */
export function subHaciaQuery(id: CajaSubId): string {
  return id === "cobros-admin" ? "cobros" : id;
}

export function rolDeCaja(): CajaRol {
  const claims = decodeJwtPayload(getToken());
  if (isSuperAdmin(claims)) return "superadmin";
  return isSoloCaja(claims) ? "cajero" : "admin";
}

export interface OtCajaTabProps {
  transitOfficeId: string;
  /** Solo para pruebas: por defecto sale del token. */
  rol?: CajaRol;
  /** Solo para pruebas: cliente de `/api/v1/ot/caja` ya resuelto. */
  api?: OtCajaApi;
  /** Subpestaña inicial (`?sub=`). */
  initialSub?: CajaSubId;
  /** Avisa el cambio de subpestaña (la página lo refleja en la URL). */
  onSubChange?: (id: CajaSubId) => void;
}

export function OtCajaTab({ transitOfficeId, rol: rolProp, api = otCajaApi, initialSub, onSubChange }: OtCajaTabProps) {
  const rol = useMemo<CajaRol>(() => rolProp ?? rolDeCaja(), [rolProp]);
  const cobra = rol !== "superadmin";
  const [version, setVersion] = useState(0);

  const tabs: SectionTab<CajaSubId>[] = [];
  const [active, setActive] = useState<CajaSubId>(() => {
    if (initialSub) return initialSub;
    return cobra ? "cobrar" : "usuarios";
  });

  if (cobra) {
    tabs.push({
      id: "cobrar",
      label: "Cobrar",
      content: <CobrarTramite api={api} onCobrado={() => setVersion((v) => v + 1)} />,
    });
    tabs.push({
      id: "cobros",
      label: "Cobros del día",
      // Solo se monta con la pestaña abierta: cada vez que se entra, la lista se pide de nuevo.
      content: active === "cobros" ? <CobrosDelDia api={api} puedeAnular={rol === "admin"} version={version} /> : null,
    });
  }
  if (rol !== "cajero") {
    tabs.push({ id: "usuarios", label: "Usuarios de caja", content: <OtCajaUsuariosSection transitOfficeId={transitOfficeId} /> });
  }
  if (rol === "superadmin") {
    tabs.push({ id: "cobros-admin", label: "Cobros de caja", content: <OtCajaCobrosSection transitOfficeId={transitOfficeId} /> });
  }

  const cambiar = (id: CajaSubId) => {
    setActive(id);
    onSubChange?.(id);
  };
  const vigente = tabs.some((t) => t.id === active) ? active : tabs[0].id;

  return <SectionTabs tabs={tabs} active={vigente} onChange={cambiar} ariaLabel="Secciones de la caja del portal" />;
}
