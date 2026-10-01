// HU #13181 (Feature F7 #13119, épica #13090) — lista de compañías asociadas del formulario del
// mandatario según el perfil. Un bloque por criterio de aceptación.

import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError, ApiValidationError } from "@/lib/api/types";

const fetchOt = vi.fn();
const fetchHijas = vi.fn();

vi.mock("@/lib/api/admin-mandate-signers", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-mandate-signers")>()),
  fetchOtAssociableCompanies: (...a: unknown[]) => fetchOt(...a),
  fetchCompanyAssociableCompanies: (...a: unknown[]) => fetchHijas(...a),
}));
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

import * as api from "@/lib/api/admin-mandate-signers";
import { CompanyMandatarioForm } from "../CompanyMandatarioForm";

const OFICINAS = [{ transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" }];
const ACME = { id: "t-acme", name: "ACME SAS", nit: "900111111" };
const BETA = { id: "t-beta", name: "BETA SAS", nit: "900222222" };

const pagina = (items: { id: string; name: string; nit: string }[], total = items.length) => ({
  items,
  total,
  page: 1,
  pageSize: 10,
  aplicaSoloASuCompania: false,
});
const SIN_RED = { items: [], total: 0, page: 1, pageSize: 100, aplicaSoloASuCompania: true };

const onSubmit = vi.fn();

function renderHub() {
  return render(
    <CompanyMandatarioForm
      variant="hub"
      offices={OFICINAS}
      editing={null}
      initialOfficeIds={["ot-1"]}
      restrictToOfficeIds={["ot-1"]}
      ownerCompanyIds={["t-propia"]}
      onCancel={vi.fn()}
      onSubmit={onSubmit}
    />,
  );
}

function renderCompania() {
  return render(
    <CompanyMandatarioForm
      tenantId="t-1"
      offices={OFICINAS}
      editing={null}
      initialOfficeIds={["ot-1"]}
      onCancel={vi.fn()}
      onSubmit={onSubmit}
    />,
  );
}

async function diligenciar(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Nombre completo"), "Ana Restrepo");
  await user.type(screen.getByLabelText("Número de documento"), "1020304050");
  await user.type(screen.getByLabelText(/^Correo/), "ana@ejemplo.com");
  await user.click(screen.getByRole("radio", { name: "Validación de identidad" }));
}

beforeEach(() => {
  vi.clearAllMocks();
  fetchOt.mockResolvedValue(pagina([ACME, BETA]));
  fetchHijas.mockResolvedValue(SIN_RED);
  onSubmit.mockResolvedValue({ id: "ms-1", integrityHash: "h" });
});

describe("HU #13181 AC1 — OT y Super Admin buscan y seleccionan", () => {
  it("busca por nombre o NIT, marca varias (sin duplicados) y las conserva entre búsquedas", async () => {
    const user = userEvent.setup();
    renderHub();
    await user.click(await screen.findByRole("checkbox", { name: "ACME SAS (NIT 900111111)" }));
    fetchOt.mockResolvedValue(pagina([BETA]));
    await user.type(screen.getByLabelText("Buscar compañía por nombre o NIT"), "beta");
    await waitFor(() =>
      expect(fetchOt).toHaveBeenCalledWith(
        "ot-1",
        expect.objectContaining({ search: "beta", page: 1 }),
        expect.anything(),
      ),
    );
    await user.click(await screen.findByRole("checkbox", { name: "BETA SAS (NIT 900222222)" }));
    // Las dos quedan marcadas aunque la lista solo muestre la última búsqueda, sin repetirse.
    const seleccion = screen.getByRole("list", { name: "Compañías seleccionadas" });
    expect(within(seleccion).getAllByRole("listitem")).toHaveLength(2);
    expect(within(seleccion).getByText(/ACME SAS · NIT 900111111/)).toBeInTheDocument();
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].officeCompanies).toEqual([
      { transitOfficeId: "ot-1", associatedCompanyTenantIds: ["t-acme", "t-beta"] },
    ]);
  });

  it("no envía búsquedas de un solo carácter", async () => {
    const user = userEvent.setup();
    renderHub();
    await screen.findByRole("checkbox", { name: "ACME SAS (NIT 900111111)" });
    fetchOt.mockClear();
    await user.type(screen.getByLabelText("Buscar compañía por nombre o NIT"), "a");
    expect(await screen.findByText(/al menos 2 caracteres/i)).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 450));
    expect(fetchOt).not.toHaveBeenCalled();
  });

  it("no ofrece la compañía propia del mandatario", async () => {
    fetchOt.mockResolvedValue(pagina([ACME, { id: "t-propia", name: "PROPIA SAS", nit: "800" }]));
    renderHub();
    await screen.findByRole("checkbox", { name: "ACME SAS (NIT 900111111)" });
    expect(screen.queryByRole("checkbox", { name: /PROPIA SAS/ })).not.toBeInTheDocument();
  });
});

describe("HU #13181 AC2 — Admin de Compañía con red", () => {
  it("ve solo sus hijas y no hay buscador global", async () => {
    fetchHijas.mockResolvedValue(pagina([ACME, BETA]));
    const user = userEvent.setup();
    renderCompania();
    expect(await screen.findByRole("checkbox", { name: "ACME SAS (NIT 900111111)" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "BETA SAS (NIT 900222222)" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Buscar compañía por nombre o NIT")).not.toBeInTheDocument();
    expect(fetchOt).not.toHaveBeenCalled();
    expect(fetchHijas).toHaveBeenCalledWith("t-1", expect.anything(), expect.anything(), undefined);
    await user.click(screen.getByRole("checkbox", { name: "BETA SAS (NIT 900222222)" }));
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].officeCompanies).toEqual([
      { transitOfficeId: "ot-1", associatedCompanyTenantIds: ["t-beta"] },
    ]);
  });
});

describe("HU #13181 AC3 — compañía sin red", () => {
  it("no muestra lista y dice que aplica solo a su compañía", async () => {
    const user = userEvent.setup();
    renderCompania();
    expect(await screen.findByText("Este mandatario aplica solo a su compañía")).toBeInTheDocument();
    expect(screen.queryByLabelText("Buscar compañía por nombre o NIT")).not.toBeInTheDocument();
    expect(screen.queryByText(/Opcional\. Sin selección/)).not.toBeInTheDocument();
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].officeCompanies).toBeUndefined();
  });
});

describe("HU #13181 AC4 — errores del backend", () => {
  it("403 fuera de alcance: muestra el motivo y conserva lo digitado", async () => {
    fetchHijas.mockResolvedValue(pagina([ACME]));
    onSubmit.mockRejectedValue(
      new ApiError(403, "La compañía asociada no está dentro de tu alcance.", {
        code: "compania_asociada_fuera_de_alcance",
      }),
    );
    const user = userEvent.setup();
    renderCompania();
    await user.click(await screen.findByRole("checkbox", { name: "ACME SAS (NIT 900111111)" }));
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "La compañía asociada no está dentro de tu alcance.",
    );
    expect(screen.getByLabelText("Nombre completo")).toHaveValue("Ana Restrepo");
    expect(screen.getByRole("checkbox", { name: "ACME SAS (NIT 900111111)" })).toBeChecked();
  });

  it("422: muestra el motivo por compañía y deja el formulario abierto con lo digitado", async () => {
    onSubmit.mockRejectedValue(
      new ApiValidationError(
        [
          {
            field: "associatedCompanyTenantIds",
            message: "La compañía asociada está inactiva o bloqueada.",
            value: "t-beta",
          },
        ],
        422,
      ),
    );
    const user = userEvent.setup();
    renderHub();
    await user.click(await screen.findByRole("checkbox", { name: "BETA SAS (NIT 900222222)" }));
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    const errores = await screen.findByTestId("mandatario-asociadas-errores");
    expect(errores).toHaveTextContent("BETA SAS: La compañía asociada está inactiva o bloqueada.");
    expect(screen.getByRole("dialog", { name: /registrar mandatario/i })).toBeInTheDocument();
    expect(screen.getByLabelText("Nombre completo")).toHaveValue("Ana Restrepo");
    expect(screen.getByRole("checkbox", { name: "BETA SAS (NIT 900222222)" })).toBeChecked();
  });
});

describe("HU #13181 AC5 — se retira lo antiguo", () => {
  it("no hay texto de todas las empresas ni cliente de empresas representadas", async () => {
    renderHub();
    await screen.findByRole("checkbox", { name: "ACME SAS (NIT 900111111)" });
    expect(screen.queryByText(/para TODAS las empresas/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/firma solo para/i)).not.toBeInTheDocument();
    expect("fetchRepresentedCompanies" in api).toBe(false);
  });
});

describe("HU #13181 AC6 — carga y vacío", () => {
  it("muestra carga mientras consulta", async () => {
    fetchOt.mockReturnValue(new Promise(() => undefined));
    renderHub();
    expect(await screen.findByText("Cargando compañías…")).toBeInTheDocument();
  });

  it("sin coincidencias muestra «Sin resultados» y el guardado sigue permitido sin selección", async () => {
    const user = userEvent.setup();
    renderHub();
    await screen.findByRole("checkbox", { name: "ACME SAS (NIT 900111111)" });
    fetchOt.mockResolvedValue(pagina([]));
    await user.type(screen.getByLabelText("Buscar compañía por nombre o NIT"), "zzz");
    expect(await screen.findByText("Sin resultados")).toBeInTheDocument();
    await diligenciar(user);
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].officeCompanies).toEqual([
      { transitOfficeId: "ot-1", associatedCompanyTenantIds: [] },
    ]);
  });

  it("un fallo de consulta muestra error con Reintentar", async () => {
    fetchOt.mockRejectedValueOnce(new ApiError(500, "boom"));
    const user = userEvent.setup();
    renderHub();
    expect(await screen.findByText("No se pudieron cargar las compañías.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("checkbox", { name: "ACME SAS (NIT 900111111)" })).toBeInTheDocument();
  });
});
