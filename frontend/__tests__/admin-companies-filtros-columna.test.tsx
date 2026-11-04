/** HU #13999 — Compañías: panel de consulta de siempre (arriba) + embudo por columna (servidor). */
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const fetchCompaniesIndex = vi.fn();

const permissionsState = vi.hoisted(() => ({
  isSuperAdmin: true,
  isAdminCompany: false,
  isOtAdmin: false,
  tenantId: "11111111-1111-1111-1111-111111111111" as string | null,
  permissions: [] as string[],
  userId: "u1" as string | null,
  roleId: null as string | null,
  roleCode: "SuperAdmin" as string | null,
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: () => permissionsState }));
vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: (...args: unknown[]) => fetchCompaniesIndex(...args),
  fetchCompanyChildren: vi.fn(),
  createCompany: vi.fn(),
  updateCompany: vi.fn(),
}));

import AdminCompaniesPage from "@/app/admin/companies/page";

const ITEM = {
  id: "c1",
  nit: "900111222-3",
  razonSocial: "Alfa S.A.S.",
  code: "COMP-0001",
  tenantType: "RENTING",
  isTransitOffice: false,
  estadoActivo: true,
  fechaCreacion: "2026-01-10T10:00:00Z",
  rowVersion: 1,
};

const lastParams = () =>
  fetchCompaniesIndex.mock.calls[fetchCompaniesIndex.mock.calls.length - 1][0] as Record<
    string,
    unknown
  >;

async function esperarListado() {
  await waitFor(() => expect(screen.getByRole("table", { name: "Compañías" })).toBeInTheDocument());
}

type User = ReturnType<typeof userEvent.setup>;

const abrirEmbudo = (user: User, columna: string) =>
  user.click(screen.getByRole("button", { name: new RegExp(`^Filtrar por ${columna}`) }));

async function filtrarTexto(user: User, columna: string, valor: string) {
  await abrirEmbudo(user, columna);
  await user.type(screen.getByLabelText(new RegExp(`${columna} contiene`, "i")), valor);
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

describe("AdminCompaniesPage — panel de arriba + filtros por columna (HU #13999)", () => {
  beforeEach(() => {
    fetchCompaniesIndex.mockReset();
    permissionsState.isSuperAdmin = true;
    permissionsState.isAdminCompany = false;
    fetchCompaniesIndex.mockResolvedValue({ data: [ITEM], totalCount: 1, page: 1, pageSize: 10 });
  });

  it("AC1: Código, NIT y Razón social (embudo) se envían como codigo/nit/razonSocial sin recargar", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    await filtrarTexto(user, "Código", "COMP-0001");
    await waitFor(() => expect(lastParams()).toMatchObject({ codigo: "COMP-0001" }));
    await filtrarTexto(user, "NIT", "9001");
    await filtrarTexto(user, "Razón social", "Alfa");
    await waitFor(() =>
      expect(lastParams()).toMatchObject({ codigo: "COMP-0001", nit: "9001", razonSocial: "Alfa" }),
    );
  });

  it("AC1: Tipo (varios) y Estado se envían como tenantType y estadoActivo booleano", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    await abrirEmbudo(user, "Tipo");
    await user.click(screen.getByRole("checkbox", { name: "Radicadora" }));
    await user.click(screen.getByRole("checkbox", { name: "Concesión" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    await waitFor(() => expect(lastParams().tenantType).toBe("RENTING,CONCESION"));

    await abrirEmbudo(user, "Estado");
    await user.click(screen.getByRole("checkbox", { name: "Inactiva" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    await waitFor(() => expect(lastParams().estadoActivo).toBe(false));
  });

  it("AC1: Activa + Inactiva a la vez en la columna no envía estadoActivo", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    await abrirEmbudo(user, "Estado");
    await user.click(screen.getByRole("checkbox", { name: "Activa" }));
    await user.click(screen.getByRole("checkbox", { name: "Inactiva" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));

    await waitFor(() => expect(fetchCompaniesIndex.mock.calls.length).toBeGreaterThan(1));
    expect(lastParams()).not.toHaveProperty("estadoActivo");
  });

  it("AC1: Fecha de creación (Periodo del embudo) se envía como fechaDesde/fechaHasta", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    await abrirEmbudo(user, "Fecha creación");
    await user.click(screen.getByRole("radio", { name: "Rango propio" }));
    await user.type(screen.getByLabelText("Desde"), "2026-01-01");
    await user.type(screen.getByLabelText("Hasta"), "2026-01-31");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));

    await waitFor(() =>
      expect(lastParams()).toMatchObject({ fechaDesde: "2026-01-01", fechaHasta: "2026-01-31" }),
    );
  });

  it("AC1: el panel de arriba de siempre está y la barra nueva (+ Filtro / Periodo) ya no", async () => {
    render(<AdminCompaniesPage />);
    await esperarListado();
    expect(screen.getByRole("form", { name: "Filtros de compañías" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^buscar$/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /\+ filtro/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^periodo/i })).not.toBeInTheDocument();
  });

  it("AC1: el panel de arriba filtra con «Buscar» y se combina (AND) con el embudo; la columna gana si repite parámetro", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    await user.type(screen.getByLabelText("NIT", { selector: "#filter-nit" }), "9001");
    await user.selectOptions(screen.getByLabelText("Estado", { selector: "#filter-estado" }), "true");
    await user.click(screen.getByRole("button", { name: /^buscar$/i }));
    await waitFor(() => expect(lastParams()).toMatchObject({ nit: "9001", estadoActivo: true }));

    await filtrarTexto(user, "Razón social", "Alfa");
    await waitFor(() =>
      expect(lastParams()).toMatchObject({ nit: "9001", estadoActivo: true, razonSocial: "Alfa" }),
    );
    await filtrarTexto(user, "NIT", "777");
    await waitFor(() => expect(lastParams()).toMatchObject({ nit: "777", razonSocial: "Alfa" }));
  });

  it("AC1: quitar el filtro de una columna lo retira de la API y deja el panel intacto", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    await user.type(screen.getByLabelText("NIT", { selector: "#filter-nit" }), "9001");
    await user.click(screen.getByRole("button", { name: /^buscar$/i }));
    await filtrarTexto(user, "Código", "COMP-0001");
    await waitFor(() => expect(lastParams()).toMatchObject({ nit: "9001", codigo: "COMP-0001" }));

    await abrirEmbudo(user, "Código");
    await user.click(screen.getByRole("button", { name: "Quitar filtro" }));
    await waitFor(() => expect(lastParams()).not.toHaveProperty("codigo"));
    expect(lastParams()).toMatchObject({ nit: "9001" });
  });

  it("AC3: sin resultados muestra «Limpiar filtros» y al limpiar vuelve el listado completo en página 1", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    fetchCompaniesIndex.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 10 });
    await filtrarTexto(user, "NIT", "000");
    const limpiar = await screen.findByRole("button", { name: "Limpiar filtros" });
    // La tabla sigue montada con sus embudos y los filtros de arriba (la fila vacía ocupa todas las columnas).
    expect(screen.getByRole("table", { name: "Compañías" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /filtrar por nit/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /filtrar por razón social/i })).toBeInTheDocument();
    expect(screen.getByLabelText("Razón Social")).toBeInTheDocument();

    fetchCompaniesIndex.mockResolvedValue({ data: [ITEM], totalCount: 1, page: 1, pageSize: 10 });
    await user.click(limpiar);

    await esperarListado();
    expect(lastParams()).not.toHaveProperty("nit");
    expect(lastParams()).toMatchObject({ page: 1 });
  });

  it("AC3: con 0 filas se puede quitar un solo filtro desde su embudo y vuelven las filas", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    fetchCompaniesIndex.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 10 });
    await filtrarTexto(user, "NIT", "000");
    await screen.findByRole("button", { name: "Limpiar filtros" });

    fetchCompaniesIndex.mockResolvedValue({ data: [ITEM], totalCount: 1, page: 1, pageSize: 10 });
    await abrirEmbudo(user, "NIT");
    await user.click(screen.getByRole("button", { name: "Quitar filtro" }));
    await esperarListado();
    expect(lastParams()).not.toHaveProperty("nit");
  });

  it("AC3: «Limpiar filtros» quita solo los de columna; el «Limpiar» del panel, los suyos", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    await user.type(screen.getByLabelText("Razón Social"), "Beta");
    await user.click(screen.getByRole("button", { name: /^buscar$/i }));
    fetchCompaniesIndex.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 10 });
    await filtrarTexto(user, "NIT", "000");
    const limpiarColumnas = await screen.findByRole("button", { name: "Limpiar filtros" });

    fetchCompaniesIndex.mockResolvedValue({ data: [ITEM], totalCount: 1, page: 1, pageSize: 10 });
    await user.click(limpiarColumnas);
    await esperarListado();
    expect(lastParams().nit).toBeUndefined();
    expect(lastParams()).toMatchObject({ razonSocial: "Beta" });

    await user.click(screen.getByRole("button", { name: /^limpiar$/i }));
    await waitFor(() => expect(lastParams().razonSocial).toBeUndefined());
  });

  it("AC4: los filtros se combinan con «Incluir organismos de tránsito» y no lo reinician", async () => {
    const user = userEvent.setup();
    render(<AdminCompaniesPage />);
    await esperarListado();

    await user.click(screen.getByLabelText(/Incluir organismos de tránsito/i));
    await waitFor(() => expect(lastParams().excludeTransitOffices).toBe(false));
    await esperarListado();

    await filtrarTexto(user, "NIT", "9001");
    await waitFor(() =>
      expect(lastParams()).toMatchObject({ nit: "9001", excludeTransitOffices: false }),
    );
    expect(screen.getByLabelText(/Incluir organismos de tránsito/i)).toBeChecked();

    // Y al revés: cambiar el toggle conserva el filtro.
    await user.click(screen.getByLabelText(/Incluir organismos de tránsito/i));
    await waitFor(() =>
      expect(lastParams()).toMatchObject({ nit: "9001", excludeTransitOffices: true }),
    );
  });
});
