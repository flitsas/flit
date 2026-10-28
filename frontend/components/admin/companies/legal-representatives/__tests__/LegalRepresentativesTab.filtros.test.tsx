// HU #14001 (Feature #13996) — filtros por columna en Representantes legales (estándar HU #13997).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { LegalRepresentativesTab } from "../LegalRepresentativesTab";
import type {
  AssignableProcedureType,
  LegalRepresentativeItem,
} from "@/lib/api/admin-legal-representatives";

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

const P_TRASPASO = "p-traspaso";
const P_MATRICULA = "p-matricula";
const TYPES: AssignableProcedureType[] = [
  { id: P_TRASPASO, code: "TRASPASO", name: "Traspaso" },
  { id: P_MATRICULA, code: "MATRICULA", name: "Matrícula inicial" },
];

function rep(over: Partial<LegalRepresentativeItem>): LegalRepresentativeItem {
  return {
    id: "x",
    representedCompanyId: "co-1",
    companyDocumentNumber: "900123456-7",
    companyName: "XYZ",
    documentType: "CC",
    documentNumber: "1000",
    firstLastName: "Gómez",
    secondLastName: null,
    name: "Ana",
    hasSignatureOrIdentity: false,
    identityStatus: "none",
    procedureTypeIds: [],
    companies: [],
    isActive: true,
    createdAt: "2026-06-01T00:00:00Z",
    ...over,
  };
}

const ANA = rep({
  id: "1",
  name: "Ana",
  firstLastName: "Gómez",
  documentNumber: "1098765432",
  procedureTypeIds: [P_TRASPASO],
  hasSignatureOrIdentity: true,
});
const LUIS = rep({
  id: "2",
  name: "Luis",
  firstLastName: "Pérez",
  documentNumber: "52111222",
  procedureTypeIds: [P_MATRICULA],
  hasSignatureOrIdentity: false,
});
const MARTA = rep({
  id: "3",
  name: "Marta",
  firstLastName: "Rojas",
  documentNumber: "79333444",
  procedureTypeIds: [P_TRASPASO, P_MATRICULA],
  hasSignatureOrIdentity: false,
});

type User = ReturnType<typeof userEvent.setup>;

async function abrirFiltro(user: User, columna: string) {
  const base = `Filtrar por ${columna}`;
  await user.click(
    screen.getByRole("button", { name: (n) => n === base || n.startsWith(`${base} (`) }),
  );
}

async function renderTab() {
  render(
    <ToastProvider>
      <LegalRepresentativesTab tenantId="t-1" />
    </ToastProvider>,
  );
  await screen.findByText("Ana Gómez");
}

const visibles = () => screen.queryAllByRole("row").length - 1;

describe("LegalRepresentativesTab · filtros por columna (HU #14001)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue(TYPES);
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue({
      data: [ANA, LUIS, MARTA],
      totalCount: 3,
      page: 1,
      pageSize: 100,
    });
  });

  it("AC1: conserva el buscador de arriba y suma un embudo por columna (sin la barra de la épica)", async () => {
    await renderTab();
    expect(screen.getByPlaceholderText(/buscar por nombre o documento/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^\+ Filtro/ })).not.toBeInTheDocument();
    for (const c of ["Representante", "Documento", "Trámites", "Firma / Identidad"]) {
      expect(screen.getByRole("button", { name: `Filtrar por ${c}` })).toBeInTheDocument();
    }
    expect(screen.queryByRole("button", { name: /Filtrar por Acciones/ })).not.toBeInTheDocument();
    expect(visibles()).toBe(3);
  });

  it("AC1: el buscador de arriba se combina con el embudo (AND)", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Trámites");
    await user.click(screen.getByRole("checkbox", { name: "Traspaso" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(visibles()).toBe(2);
    await user.type(screen.getByRole("textbox", { name: "Buscar representantes" }), "1.098-765");
    expect(screen.getByText("Ana Gómez")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC1: filtra por Representante (contiene, sin tildes ni mayúsculas)", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Representante");
    await user.type(screen.getByLabelText(/representante contiene/i), "perez");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByText("Luis Pérez")).toBeInTheDocument();
    expect(screen.queryByText("Ana Gómez")).not.toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC1: filtra por Documento ignorando puntos", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Documento");
    await user.type(screen.getByLabelText(/documento contiene/i), "1.098.765");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByText("Ana Gómez")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC1: filtra por Trámites con las opciones del catálogo asignable", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Trámites");
    await user.click(screen.getByRole("checkbox", { name: "Matrícula inicial" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.queryByText("Ana Gómez")).not.toBeInTheDocument();
    expect(screen.getByText("Luis Pérez")).toBeInTheDocument();
    expect(screen.getByText("Marta Rojas")).toBeInTheDocument();
  });

  it("AC1: filtra por Firma / Identidad con los mismos estados que muestra la celda", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Firma / Identidad");
    await user.click(screen.getByRole("checkbox", { name: "Con firma o identidad" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByText("Ana Gómez")).toBeInTheDocument();
    expect(visibles()).toBe(1);
    expect(
      screen.getByRole("status", { name: /Estado: Con firma o identidad/i }),
    ).toBeInTheDocument();
  });

  it("AC1: combina varios filtros con AND", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Trámites");
    await user.click(screen.getByRole("checkbox", { name: "Traspaso" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(visibles()).toBe(2);
    await abrirFiltro(user, "Firma / Identidad");
    await user.click(screen.getByRole("checkbox", { name: "Sin firma ni identidad" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByText("Marta Rojas")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC3: sin resultados muestra el estado vacío y «Limpiar filtros» devuelve la lista completa", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Representante");
    await user.type(screen.getByLabelText(/representante contiene/i), "zzz");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByText(/ningún representante coincide/i)).toBeInTheDocument();
    // La tabla sigue montada con sus embudos (el activo en azul) y el buscador de arriba.
    expect(screen.getByRole("button", { name: /^Filtrar por Documento/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Filtrar por Representante \(/ })).toHaveAttribute(
      "data-activo",
      "true",
    );
    expect(screen.getByRole("textbox", { name: "Buscar representantes" })).toBeInTheDocument();
    // Se quita solo ese filtro desde su embudo.
    await abrirFiltro(user, "Representante");
    await user.click(screen.getByRole("button", { name: "Quitar filtro" }));
    expect(visibles()).toBe(3);
    await abrirFiltro(user, "Representante");
    await user.type(screen.getByLabelText(/representante contiene/i), "zzz");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    await user.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(visibles()).toBe(3);
  });

  it("AC4: el aviso «sin firma ni identidad» cuenta sobre TODOS los representantes", async () => {
    const user = userEvent.setup();
    await renderTab();
    expect(screen.getByTestId("representantes-resumen")).toHaveTextContent(
      "3 representantes · 2 sin firma ni identidad",
    );
    await abrirFiltro(user, "Representante");
    await user.type(screen.getByLabelText(/representante contiene/i), "ana");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(visibles()).toBe(1);
    expect(screen.getByTestId("representantes-resumen")).toHaveTextContent(
      "3 representantes · 2 sin firma ni identidad",
    );
  });
});
