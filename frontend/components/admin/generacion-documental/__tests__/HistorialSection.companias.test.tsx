// Feature #12201 — universo de compañías del historial para SuperAdmin.
//
// El backend ya honraba `tenantId` (CF-20) pero la interfaz no ofrecía dónde elegirlo, así que un
// SuperAdmin solo veía su propia compañía. Aquí se cubren las dos direcciones: que el control
// aparece y funciona para quien debe, y que NO existe para nadie más ni se activa por omisión.
//
// Importa porque en este repo el aislamiento real entre compañías es el `WHERE tenant_id` del
// repositorio y no la RLS: el listado global es el único camino que no lo lleva.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  fetchStandaloneDocuments: vi.fn(),
  requestStandaloneDocumentDownload: vi.fn(),
  fetchCompaniesIndex: vi.fn(),
  isSuperAdmin: vi.fn(),
  push: vi.fn(),
}));

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  fetchStandaloneDocuments: mocks.fetchStandaloneDocuments,
  requestStandaloneDocumentDownload: mocks.requestStandaloneDocumentDownload,
}));
vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: mocks.fetchCompaniesIndex,
}));
vi.mock("@/lib/auth/jwt", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/auth/jwt")>()),
  isSuperAdmin: mocks.isSuperAdmin,
}));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: mocks.push }) }));

import { HistorialSection } from "../HistorialSection";

const item = {
  id: "11111111-1111-1111-1111-111111111111",
  documentType: "certificado_rues" as const,
  scenario: null,
  status: "generated" as const,
  companyName: "Renting Demo S.A.S.",
  createdByUserId: "22222222-2222-2222-2222-222222222222",
  createdByUserName: "Ana Gestora",
  createdAt: "2026-09-01T14:30:00.000Z",
};

const COMPANIAS = {
  data: [
    { id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", razonSocial: "Renting Demo S.A.S." },
    { id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", razonSocial: "Transportes Acme S.A.S." },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 200,
};

/** Última llamada al listado: es la que refleja el filtro recién aplicado. */
function ultimaConsulta(): Record<string, unknown> {
  const calls = mocks.fetchStandaloneDocuments.mock.calls;
  return calls[calls.length - 1][0] as Record<string, unknown>;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchStandaloneDocuments.mockResolvedValue({
    items: [item],
    page: 1,
    pageSize: 20,
    total: 1,
  });
  mocks.fetchCompaniesIndex.mockResolvedValue(COMPANIAS);
});

describe("Historial — el selector de compañía es exclusivo del SuperAdmin", () => {
  it("un usuario normal no ve el control, y no se piden compañías", async () => {
    mocks.isSuperAdmin.mockReturnValue(false);
    render(<HistorialSection />);

    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());

    expect(screen.queryByTestId("historial-filtro-compania")).not.toBeInTheDocument();
    // No está oculto ni deshabilitado: no existe. Y no se gasta una consulta que no se usaría.
    expect(mocks.fetchCompaniesIndex).not.toHaveBeenCalled();
  });

  it("el SuperAdmin ve «Mi compañía», «Todas las compañías» y las compañías cargadas", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    render(<HistorialSection />);

    const selector = await screen.findByTestId("historial-filtro-compania");
    await waitFor(() => expect(mocks.fetchCompaniesIndex).toHaveBeenCalled());

    await waitFor(() =>
      expect(screen.getByRole("option", { name: "Transportes Acme S.A.S." })).toBeInTheDocument(),
    );
    expect(screen.getByRole("option", { name: "Mi compañía" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Todas las compañías" })).toBeInTheDocument();
    // Arranca en «Mi compañía»: cruzar datos entre compañías nunca es el estado inicial.
    expect((selector as HTMLSelectElement).value).toBe("");
  });
});

describe("Historial — qué viaja en la consulta según el universo elegido", () => {
  beforeEach(() => {
    mocks.isSuperAdmin.mockReturnValue(true);
  });

  it("por defecto no envía ni tenantId ni allTenants", async () => {
    render(<HistorialSection />);
    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());

    const consulta = ultimaConsulta();
    expect(consulta.tenantId).toBeUndefined();
    expect(consulta.allTenants).toBeUndefined();
  });

  it("«Todas las compañías» envía allTenants y ningún tenantId", async () => {
    render(<HistorialSection />);
    const selector = await screen.findByTestId("historial-filtro-compania");

    await userEvent.selectOptions(selector, "ALL");

    await waitFor(() => expect(ultimaConsulta().allTenants).toBe(true));
    expect(ultimaConsulta().tenantId).toBeUndefined();
  });

  it("elegir una compañía envía su tenantId y ningún allTenants", async () => {
    render(<HistorialSection />);
    const selector = await screen.findByTestId("historial-filtro-compania");
    await waitFor(() =>
      expect(screen.getByRole("option", { name: "Transportes Acme S.A.S." })).toBeInTheDocument(),
    );

    await userEvent.selectOptions(selector, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    await waitFor(() =>
      expect(ultimaConsulta().tenantId).toBe("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
    );
    expect(ultimaConsulta().allTenants).toBeUndefined();
  });
});

describe("Historial — si la carga de compañías falla, la pantalla sigue sirviendo", () => {
  it("conserva las dos opciones que no dependen de ese listado", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.fetchCompaniesIndex.mockRejectedValue(new Error("500"));

    render(<HistorialSection />);

    const selector = await screen.findByTestId("historial-filtro-compania");
    expect(screen.getByRole("option", { name: "Mi compañía" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Todas las compañías" })).toBeInTheDocument();

    // Control positivo: el historial se cargó igual, así que la ausencia de compañías no está
    // «demostrada» sobre una pantalla que no llegó a renderizar.
    await waitFor(() => expect(mocks.fetchStandaloneDocuments).toHaveBeenCalled());
    expect(selector).toBeEnabled();
  });
});
