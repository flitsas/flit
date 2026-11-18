// HU #14427 (Feature #14420) — Perfil + Empresa/OT destino antes de importar el Excel.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  security: vi.fn(),
  ot: vi.fn(),
  fetchAllCompanies: vi.fn(),
  fetchAllOt: vi.fn(),
}));

vi.mock("@/lib/api/bulk-invitations-client", async (orig) => ({
  ...(await orig<typeof import("@/lib/api/bulk-invitations-client")>()),
  createSecurityBulkInvitationsClient: mocks.security,
  createOtBulkInvitationsClient: mocks.ot,
}));
vi.mock("@/lib/api/admin-companies", () => ({ fetchAllCompanies: mocks.fetchAllCompanies }));
vi.mock("@/lib/api/admin-transit-office-tenants", () => ({ fetchAllTransitOfficeTenants: mocks.fetchAllOt }));
vi.mock("@/components/consultas/export", () => ({ download: vi.fn() }));

import { UsuariosBulkInvite } from "../UsuariosBulkInvite";

const fakeClient = (tag: string) => ({
  tag,
  downloadTemplate: vi.fn().mockResolvedValue(undefined),
  submit: vi.fn().mockResolvedValue({ dryRun: true, total: 1, valid: 1, created: 0, rejected: 0, notProcessed: 0, rows: [] }),
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.security.mockImplementation((scope?: { targetTenantId?: string }) =>
    fakeClient(`security:${scope?.targetTenantId ?? "own"}`),
  );
  mocks.ot.mockImplementation((scope?: { transitOfficeId?: string }) => fakeClient(`ot:${scope?.transitOfficeId}`));
  mocks.fetchAllCompanies.mockResolvedValue([{ id: "company-1", razonSocial: "Compañía Demo" }]);
  mocks.fetchAllOt.mockResolvedValue([
    { id: "ot-tenant-1", legalName: "Secretaría Demo", transitOfficeCode: "OT-DEMO", transitOfficeId: "ot-office-1" },
  ]);
});

const renderSuper = () => render(<UsuariosBulkInvite isSuperAdmin onClose={vi.fn()} />);

async function chooseDestination(user: ReturnType<typeof userEvent.setup>, label: RegExp, option: RegExp) {
  await user.click(await screen.findByLabelText(label));
  await user.click(await screen.findByRole("option", { name: option }));
}

describe("SuperAdmin — Perfil y destino", () => {
  it("ofrece Gestor y Organismo de Tránsito pero NO el perfil FLIT (AC3)", () => {
    renderSuper();

    expect(screen.getByRole("radio", { name: /Gestor/i })).toBeChecked();
    expect(screen.getByRole("radio", { name: /Organismo de Tránsito/i })).toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: /^FLIT/i })).not.toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(2);
  });

  it("sin destino elegido no se habilita descargar la plantilla ni cargar el archivo (AC2)", async () => {
    renderSuper();

    expect(screen.getByRole("button", { name: /descargar plantilla/i })).toBeDisabled();
    expect(screen.getByLabelText("Archivo Excel (.xlsx)")).toBeDisabled();
    expect(mocks.security).not.toHaveBeenCalledWith(expect.objectContaining({ targetTenantId: expect.anything() }));
  });

  it("Gestor + compañía → cliente Security con targetTenantId (AC1)", async () => {
    const user = userEvent.setup();
    renderSuper();

    await chooseDestination(user, /compañía destino/i, /Compañía Demo/);
    await user.click(screen.getByRole("button", { name: /descargar plantilla/i }));

    expect(mocks.security).toHaveBeenLastCalledWith({ targetTenantId: "company-1" });
    expect(mocks.ot).not.toHaveBeenCalled();
    const client = mocks.security.mock.results.at(-1)!.value;
    expect(client.downloadTemplate).toHaveBeenCalledTimes(1);
  });

  it("OT + organismo → cliente OT con el transitOfficeId del organismo (AC1)", async () => {
    const user = userEvent.setup();
    renderSuper();

    await user.click(screen.getByRole("radio", { name: /Organismo de Tránsito/i }));
    await chooseDestination(user, /organismo de tránsito destino/i, /Secretaría Demo/);
    await user.click(screen.getByRole("button", { name: /descargar plantilla/i }));

    expect(mocks.ot).toHaveBeenLastCalledWith({ transitOfficeId: "ot-office-1" });
    expect(mocks.ot.mock.results.at(-1)!.value.downloadTemplate).toHaveBeenCalledTimes(1);
  });

  it("cambiar de Perfil limpia el destino y descarta el archivo ya cargado (AC3)", async () => {
    const user = userEvent.setup();
    renderSuper();
    await chooseDestination(user, /compañía destino/i, /Compañía Demo/);
    await user.upload(screen.getByLabelText("Archivo Excel (.xlsx)"), new File(["x"], "gestores.xlsx"));
    expect(screen.getByText("gestores.xlsx")).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: /Organismo de Tránsito/i }));

    expect(screen.queryByText("gestores.xlsx")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Archivo Excel (.xlsx)")).toBeDisabled(); // destino OT aún sin elegir
    expect(screen.getByRole("button", { name: /validar archivo/i })).toBeDisabled();
  });

  it("cambiar de destino dentro del mismo Perfil descarta el archivo (AC3)", async () => {
    const user = userEvent.setup();
    mocks.fetchAllCompanies.mockResolvedValue([
      { id: "company-1", razonSocial: "Compañía Demo" },
      { id: "company-2", razonSocial: "Otra SAS" },
    ]);
    renderSuper();
    await chooseDestination(user, /compañía destino/i, /Compañía Demo/);
    await user.upload(screen.getByLabelText("Archivo Excel (.xlsx)"), new File(["x"], "demo.xlsx"));

    await chooseDestination(user, /compañía destino/i, /Otra SAS/);

    expect(screen.queryByText("demo.xlsx")).not.toBeInTheDocument();
    expect(mocks.security).toHaveBeenLastCalledWith({ targetTenantId: "company-2" });
  });

  it("estado de carga de destinos y error con reintento", async () => {
    const user = userEvent.setup();
    mocks.fetchAllCompanies.mockRejectedValueOnce(new Error("boom"));
    renderSuper();

    expect(await screen.findByText(/no se pudieron cargar los destinos/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /reintentar/i }));

    await waitFor(() => expect(screen.queryByText(/no se pudieron cargar los destinos/i)).not.toBeInTheDocument());
    expect(await screen.findByLabelText(/compañía destino/i)).toBeInTheDocument();
  });
});

describe("AdminCompany — destino fijo", () => {
  it("no muestra selectores: usa el cliente Security sin targetTenantId y habilita la plantilla", async () => {
    const user = userEvent.setup();
    render(<UsuariosBulkInvite isSuperAdmin={false} onClose={vi.fn()} />);

    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.getByTestId("bulk-invite-context")).toHaveTextContent(/Gestor · tu compañía/);
    expect(mocks.fetchAllCompanies).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /descargar plantilla/i }));

    expect(mocks.security).toHaveBeenLastCalledWith();
    expect(mocks.security.mock.results.at(-1)!.value.downloadTemplate).toHaveBeenCalledTimes(1);
  });
});
