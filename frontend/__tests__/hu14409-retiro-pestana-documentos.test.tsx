// HU #14409 (Feature #14408) — la ficha de la compañía deja de ofrecer la pestaña Documentos y deja de
// llamar a las rutas /document-params, para cualquier rol y también al abrir una compañía hija.
//
// Uso de ejemplo:
//   pnpm exec vitest run __tests__/hu14409-retiro-pestana-documentos.test.tsx
//
// Se prueba la página real, no solo el contenedor de pestañas: quien decide qué panel se inyecta es
// ella. Los paneles vecinos van con doble porque aquí solo importa cuáles se ofrecen.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { TenantSettings } from "@/lib/api/types";

const { nav, permisos, companias, apiFetch } = vi.hoisted(() => ({
  nav: { tenantId: "hija-1", networkHead: null as string | null },
  permisos: {
    isSuperAdmin: true,
    isAdminCompany: false,
    isGroupParent: false,
    tenantId: "plataforma" as string | null,
  },
  companias: {
    fetchCompany: vi.fn(),
    fetchCompanyChildren: vi.fn(),
    fetchTenantSettings: vi.fn(),
    updateTenantSettings: vi.fn(),
  },
  apiFetch: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useParams: () => ({ tenantId: nav.tenantId }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useSearchParams: () => ({ get: (k: string) => (k === "networkHead" ? nav.networkHead : null) }),
}));
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: () => permisos }));
vi.mock("@/lib/api/admin-companies", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-companies")>()),
  ...companias,
}));
vi.mock("@/lib/api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/client")>()),
  apiFetch,
}));
vi.mock("@/lib/api/platform", () => ({ listTenantProducts: vi.fn().mockResolvedValue([]), setTenantProduct: vi.fn() }));

vi.mock("@/components/admin/companies/panels/WhitelistPanel", () => ({ WhitelistPanel: () => <div>panel-lista-blanca</div> }));
vi.mock("@/components/admin/companies/panels/OTConfigTablePanel", () => ({ OTConfigTablePanel: () => <div>panel-organismos</div> }));
vi.mock("@/components/admin/companies/panels/AuditLogPanel", () => ({ AuditLogPanel: () => <div>panel-historial</div> }));
vi.mock("@/components/admin/companies/panels/CompanyUsersPanel", () => ({ CompanyUsersPanel: () => <div>panel-usuarios</div> }));
vi.mock("@/components/admin/companies/legal-representatives/RepresentativesAndVaultTab", () => ({
  RepresentativesAndVaultTab: () => <div>panel-representantes</div>,
}));
vi.mock("@/components/admin/companies/mandate-signers/CompanyMandatariosPanel", () => ({
  CompanyMandatariosPanel: () => <div>panel-mandatarios</div>,
}));
vi.mock("@/components/admin/companies/external-integrations/ExternalIntegrationsPanel", () => ({
  ExternalIntegrationsPanel: () => <div>panel-integraciones</div>,
}));
vi.mock("@/components/admin/companies/CompanyChildrenSection", () => ({ CompanyChildrenSection: () => <div>panel-hijos</div> }));
vi.mock("@/components/admin/branding/BrandingConfigurator", () => ({ BrandingConfigurator: () => <div>panel-marca</div> }));
vi.mock("@/components/admin/domain/NetworkDomainsSection", () => ({ NetworkDomainsSection: () => <div>panel-dominios</div> }));

import AdminCompanyDetailPage from "@/app/admin/companies/[tenantId]/page";

const settings: TenantSettings = {
  tenantId: "hija-1",
  switchesMatricula: {
    allowInitialRegistration: true,
    allowMiscNewVehicles: false,
    onlyOwnVehicles: false,
    onlyOwnVehiclesByFamily: { matriculas: false, traspaso: false, otros: false },
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  },
  baulFirmasActivo: true,
  preasignacionPlacaActiva: false,
  validarSoatConRunt: false,
  enrutamientoSMTP: "FLIT_SMTP",
  notificationTarget: "COMPRADOR",
  metodosRecaudo: ["Pasarela FLIT"],
};

const compania = (id: string, extra: Record<string, unknown> = {}) => ({
  id,
  razonSocial: `Compania ${id}`,
  nit: "900000000-1",
  tenantType: "TENANT_B2B",
  estadoActivo: true,
  parentTenantId: null,
  ...extra,
});

/** Toda URL pedida en la prueba, por el cliente de la API o por `fetch` directo. */
function rutasPedidas(fetchSpy: ReturnType<typeof vi.fn>): string[] {
  return [
    ...apiFetch.mock.calls.map(([ruta]) => String(ruta)),
    ...fetchSpy.mock.calls.map(([ruta]) => String(ruta)),
  ];
}

const nombresDePestanas = () => screen.getAllByRole("tab").map((t) => t.textContent);

async function recorrerPestanas(user: ReturnType<typeof userEvent.setup>) {
  const total = screen.getAllByRole("tab").length;
  for (let i = 0; i < total; i += 1) {
    await user.click(screen.getAllByRole("tab")[i]!);
    // Cede el turno para que el panel recién montado alcance a disparar su carga.
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
}

let fetchSpy: ReturnType<typeof vi.fn>;

beforeEach(() => {
  vi.clearAllMocks();
  nav.tenantId = "hija-1";
  nav.networkHead = null;
  Object.assign(permisos, { isSuperAdmin: true, isAdminCompany: false, isGroupParent: false, tenantId: "plataforma" });
  companias.fetchTenantSettings.mockResolvedValue(settings);
  companias.fetchCompanyChildren.mockResolvedValue([]);
  companias.fetchCompany.mockImplementation(async (id: string) => compania(id));
  apiFetch.mockResolvedValue([]);
  fetchSpy = vi.fn().mockResolvedValue(new Response("[]", { status: 200 }));
  vi.stubGlobal("fetch", fetchSpy);
});

describe("HU #14409 AC1 — Super Admin en la ficha de una compañía", () => {
  it("no ofrece Documentos y conserva las demás pestañas, en su orden", async () => {
    nav.tenantId = "compania-1";
    render(<AdminCompanyDetailPage />);
    await screen.findByRole("tab", { name: "Productos" });

    expect(screen.queryByRole("tab", { name: "Documentos" })).not.toBeInTheDocument();
    expect(nombresDePestanas()).toEqual([
      "Productos",
      "Trámites",
      "Configuración Empresa",
      "Representantes legales",
      "Mandatarios",
      "Usuarios",
      "Integraciones externas",
      "Historial de Cambios",
    ]);
  });
});

describe("HU #14409 AC2 — Super Admin en la ficha de una compañía hija", () => {
  beforeEach(() => {
    companias.fetchCompany.mockImplementation(async (id: string) =>
      id === "hija-1"
        ? compania(id, { parentTenantId: "cabeza-1" })
        : compania(id, { tenantType: "MARCA_BLANCA" }),
    );
  });

  it("no ofrece Documentos", async () => {
    render(<AdminCompanyDetailPage />);
    await screen.findByRole("tab", { name: "Productos" });

    expect(screen.queryByRole("tab", { name: "Documentos" })).not.toBeInTheDocument();
  });

  it("recorrer todas las pestañas no llama a ninguna ruta /document-params", async () => {
    const user = userEvent.setup();
    render(<AdminCompanyDetailPage />);
    await screen.findByRole("tab", { name: "Productos" });

    await recorrerPestanas(user);

    expect(rutasPedidas(fetchSpy).filter((r) => r.includes("/document-params"))).toEqual([]);
  });
});

describe("HU #14409 AC4 — Administrador de compañía (no regresión de HU #12710)", () => {
  beforeEach(() => {
    Object.assign(permisos, { isSuperAdmin: false, isAdminCompany: true, tenantId: "hija-1" });
  });

  it("solo ve Representantes legales y Mandatarios y abre en Representantes legales", async () => {
    render(<AdminCompanyDetailPage />);
    await screen.findByRole("tab", { name: "Representantes legales" });

    expect(nombresDePestanas()).toEqual(["Representantes legales", "Mandatarios"]);
    expect(screen.getByRole("tab", { name: "Representantes legales" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("panel-representantes")).toBeInTheDocument();
  });

  it("la cabeza administrando una hija conserva Usuarios, sin Documentos ni llamadas a /document-params", async () => {
    const user = userEvent.setup();
    Object.assign(permisos, { isGroupParent: true, tenantId: "cabeza-1" });
    nav.networkHead = "cabeza-1";
    render(<AdminCompanyDetailPage />);
    await screen.findByRole("tab", { name: "Representantes legales" });

    expect(nombresDePestanas()).toEqual(["Representantes legales", "Mandatarios", "Usuarios"]);
    await recorrerPestanas(user);
    expect(rutasPedidas(fetchSpy).filter((r) => r.includes("/document-params"))).toEqual([]);
  });
});
