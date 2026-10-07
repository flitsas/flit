// Mejora de diseño — Representantes legales: búsqueda, resumen, «Filas por página» y acciones con nombre.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { LegalRepresentativesTab } from "../LegalRepresentativesTab";
import type { LegalRepresentativeItem } from "@/lib/api/admin-legal-representatives";

vi.mock("@/lib/api/admin-legal-representatives", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-legal-representatives")>();
  return {
    ...actual,
    fetchLegalRepresentatives: vi.fn(),
    fetchLegalRepresentative: vi.fn(),
    fetchAssignableProcedureTypes: vi.fn(),
  };
});

import {
  fetchAssignableProcedureTypes,
  fetchLegalRepresentatives,
} from "@/lib/api/admin-legal-representatives";

function rep(i: number, over: Partial<LegalRepresentativeItem> = {}): LegalRepresentativeItem {
  return {
    id: `rep-${i}`,
    representedCompanyId: "co-1",
    companyDocumentNumber: "900123456-7",
    companyName: "XYZ",
    documentType: "CC",
    documentNumber: `10000000${String(i).padStart(2, "0")}`,
    firstLastName: `Apellido${i}`,
    secondLastName: null,
    name: `Nombre${i}`,
    email: null,
    address: null,
    city: null,
    phone: null,
    signatureVaultId: null,
    identityValidationRef: null,
    hasSignatureOrIdentity: i % 2 === 0,
    identityStatus: "none",
    identityValidUntil: null,
    firmaBaulVigente: false,
    firmaBaulVigenteHasta: null,
    procedureTypeIds: [],
    companies: [],
    isActive: true,
    createdAt: "2026-06-01T00:00:00Z",
    updatedAt: null,
    ...over,
  };
}

function renderTab() {
  return render(
    <ToastProvider>
      <LegalRepresentativesTab tenantId="t-1" />
    </ToastProvider>,
  );
}

describe("LegalRepresentativesTab — diseño", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue([]);
    const data = Array.from({ length: 12 }, (_, i) => rep(i + 1));
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue({
      data,
      totalCount: 12,
      page: 1,
      pageSize: 100,
    });
  });

  it("resume el total y cuántos están sin firma ni identidad", async () => {
    renderTab();
    await screen.findByText("Nombre1 Apellido1");
    expect(screen.getByTestId("representantes-resumen")).toHaveTextContent(
      "12 representantes · 6 sin firma ni identidad",
    );
  });

  it("pagina en cliente con «Filas por página» (10 por defecto)", async () => {
    renderTab();
    await screen.findByText("Nombre1 Apellido1");
    expect(screen.getByText("Nombre10 Apellido10")).toBeInTheDocument();
    expect(screen.queryByText("Nombre11 Apellido11")).not.toBeInTheDocument();
    expect(screen.getByText(/filas por página/i)).toBeInTheDocument();
  });

  it("la búsqueda filtra por nombre o documento en todo el directorio", async () => {
    const user = userEvent.setup();
    renderTab();
    await screen.findByText("Nombre1 Apellido1");
    const caja = screen.getByRole("textbox", { name: "Buscar representantes" });

    await user.type(caja, "nombre12");
    expect(screen.getByText("Nombre12 Apellido12")).toBeInTheDocument();
    expect(screen.queryByText("Nombre1 Apellido1")).not.toBeInTheDocument();

    await user.clear(caja);
    await user.type(caja, "1000000003");
    expect(screen.getByText("Nombre3 Apellido3")).toBeInTheDocument();
    expect(screen.queryByText("Nombre4 Apellido4")).not.toBeInTheDocument();

    await user.clear(caja);
    await user.type(caja, "zzz");
    expect(screen.getByText(/ningún representante coincide/i)).toBeInTheDocument();
  });

  it("cada fila ofrece acciones con nombre accesible y tooltip", async () => {
    renderTab();
    await screen.findByText("Nombre1 Apellido1");
    const fila = screen.getByText("Nombre1 Apellido1").closest("tr") as HTMLElement;
    for (const nombre of [
      "Editar persona y firma de Nombre1 Apellido1",
      "Asociar empresas de Nombre1 Apellido1",
      "Eliminar Nombre1 Apellido1",
    ]) {
      expect(within(fila).getByRole("button", { name: nombre })).toHaveAttribute("title", nombre);
    }
  });

  it("el estado de firma lleva texto además de color", async () => {
    renderTab();
    await screen.findByText("Nombre1 Apellido1");
    const fila = screen.getByText("Nombre1 Apellido1").closest("tr") as HTMLElement;
    expect(within(fila).getByText("Sin firma ni identidad")).toBeInTheDocument();
    const fila2 = screen.getByText("Nombre2 Apellido2").closest("tr") as HTMLElement;
    expect(within(fila2).getByText("Con firma o identidad")).toBeInTheDocument();
  });
});
