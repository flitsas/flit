import type { CompanyListItem } from "@/lib/api/types";

/** Modos de la sección de OT en configuración de compañía (HUs #12351, #12408). */
export type OtConfigPanelMode =
  | "editable"
  | "readonly-concession-head"
  | "readonly-concession-inherited"
  | "readonly-marca-blanca"
  | "superadmin-concession";

export function resolveOtConfigPanelMode(input: {
  company: CompanyListItem | null;
  parentTenantType: string | null;
  isSuperAdmin: boolean;
  isAdminCompany: boolean;
}): OtConfigPanelMode {
  const { company, parentTenantType, isSuperAdmin, isAdminCompany } = input;
  if (!company) {
    return "editable";
  }

  if (isSuperAdmin) {
    if (company.tenantType === "CONCESION") return "superadmin-concession";
    if (company.tenantType === "MARCA_BLANCA") return "readonly-marca-blanca";
    return "editable";
  }

  if (!isAdminCompany) {
    return "editable";
  }

  if (company.tenantType === "CONCESION") {
    return "readonly-concession-head";
  }

  if (company.tenantType === "MARCA_BLANCA" || parentTenantType === "MARCA_BLANCA") {
    return "readonly-marca-blanca";
  }

  if (company.parentTenantId && parentTenantType === "CONCESION") {
    return "readonly-concession-inherited";
  }

  return "editable";
}

export function otConfigPanelReadOnly(mode: OtConfigPanelMode): boolean {
  return mode !== "editable" && mode !== "superadmin-concession";
}

export function otConfigPanelLegend(mode: OtConfigPanelMode): string | null {
  switch (mode) {
    case "readonly-concession-head":
      return "Los organismos de tránsito de esta Concesión los administra la plataforma. Esta vista es de solo lectura.";
    case "readonly-concession-inherited":
      return "La gobierna su Concesión. Esta lista es de solo lectura.";
    case "readonly-marca-blanca":
      return "Operas en todos los organismos habilitados por la plataforma salvo los bloqueados. Los bloqueos los administra la plataforma.";
    default:
      return null;
  }
}

export function showMarcaBlancaBlocksSection(
  company: CompanyListItem | null,
  parentTenantType: string | null,
): boolean {
  if (!company) return false;
  return (
    company.tenantType === "MARCA_BLANCA" ||
    parentTenantType === "MARCA_BLANCA" ||
    Boolean(company.parentTenantId && parentTenantType === "MARCA_BLANCA")
  );
}

export function showSuperAdminTransitBlocksPanel(
  company: CompanyListItem | null,
  isSuperAdmin: boolean,
): boolean {
  return Boolean(isSuperAdmin && company?.tenantType === "MARCA_BLANCA" && !company.parentTenantId);
}
