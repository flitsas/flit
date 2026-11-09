"use client";

// HU #14399 (Feature #14396, Épica #14297) — Pestaña «Reportes» de Plataforma > Persona Natural: el mismo `PortalReportes`
// del hub del OT, con el cliente de Plataforma y el filtro de organismo (solo SuperAdmin).
import { PortalReportes } from "@/components/admin/transit-offices/portal-pn/PortalReportes";
import { fetchOrganismosPn, fetchTiposPn } from "@/lib/api/admin-pn-config";
import { crearReportePlataformaApi } from "@/lib/api/admin-portal-reportes";
import { nombrePropio } from "./formato";

const api = crearReportePlataformaApi(
  async (signal) => (await fetchTiposPn(signal)).map((t) => ({ id: t.id, nombre: t.nombre })),
  async (signal) =>
    (await fetchOrganismosPn(signal)).map((o) => ({ id: o.id, nombre: nombrePropio(o.nombre) })),
);

export function ReportesTab() {
  return <PortalReportes api={api} incluirFiltroOrganismo />;
}
