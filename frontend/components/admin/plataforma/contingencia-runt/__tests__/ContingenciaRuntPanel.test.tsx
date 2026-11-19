// HU #14649 (Feature #14646, Épica #14644) — Plataforma → Contingencia de proveedor RUNT.
// La API se mockea según docs/design/EPICA-14644-contrato.md; un test por criterio de aceptación.
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "@/components/admin/Toast";
import { ConsultaProvidersSection } from "@/components/admin/companies/ConsultaProvidersSection";
import { formFromSettings } from "@/components/admin/companies/settingsForm";
import { evaluateAdminAccess } from "@/lib/auth/guard";
import { canManageRuntContingency, type JwtPayload } from "@/lib/auth/jwt";
import { ApiError } from "@/lib/api/types";
import type { RuntContingencyItem } from "@/lib/api/admin-runt-contingency";
import { ContingenciaRuntPanel } from "../ContingenciaRuntPanel";

const getRuntContingency = vi.fn();
const putRuntContingency = vi.fn();

vi.mock("@/lib/api/admin-runt-contingency", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-runt-contingency")>(
    "@/lib/api/admin-runt-contingency",
  );
  return {
    ...actual,
    getRuntContingency: (...a: unknown[]) => getRuntContingency(...a),
    putRuntContingency: (...a: unknown[]) => putRuntContingency(...a),
  };
});

const items = (): RuntContingencyItem[] => [
  { kind: "vehicle_vin", provider: null, reason: null, updatedByName: null, updatedAt: null },
  {
    kind: "vehicle_plate",
    provider: "verifik",
    reason: "Kyverum caído desde las 9:10",
    updatedByName: "Ana Pérez",
    updatedAt: "2026-11-19T14:10:00Z",
  },
  {
    kind: "conductor",
    provider: null,
    reason: "Kyverum restablecido",
    updatedByName: "Ana Pérez",
    updatedAt: "2026-11-19T16:00:00Z",
  },
];

function renderPanel() {
  return render(
    <ToastProvider>
      <ContingenciaRuntPanel />
    </ToastProvider>,
  );
}

async function abrirEditar(user: ReturnType<typeof userEvent.setup>, fila: string) {
  await user.click(await screen.findByRole("button", { name: `Acciones para ${fila}` }));
  await user.click(await screen.findByRole("menuitem", { name: new RegExp(`Editar contingencia de ${fila}`) }));
  return await screen.findByRole("dialog");
}

beforeEach(() => {
  getRuntContingency.mockReset();
  putRuntContingency.mockReset();
  getRuntContingency.mockResolvedValue({ items: items() });
});

describe("ContingenciaRuntPanel (HU #14649)", () => {
  it("estado cargando muestra «Cargando contingencia…»", () => {
    getRuntContingency.mockReturnValue(new Promise(() => undefined));
    renderPanel();
    expect(screen.getByText("Cargando contingencia…")).toBeInTheDocument();
  });

  it("AC1 · muestra las tres filas con estado, quién, cuándo (DD/MM/YYYY HH:mm) y motivo", async () => {
    renderPanel();
    const tabla = await screen.findByRole("table", { name: "Contingencia de proveedor RUNT" });
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas).toHaveLength(3);

    expect(within(filas[0]).getByText("Vehículo por VIN")).toBeInTheDocument();
    expect(within(filas[0]).getByText("Sin contingencia")).toBeInTheDocument();

    expect(within(filas[1]).getByText("Vehículo por placa")).toBeInTheDocument();
    expect(within(filas[1]).getByText("Verifik")).toBeInTheDocument();
    expect(within(filas[1]).getByText("Ana Pérez")).toBeInTheDocument();
    expect(within(filas[1]).getByText("19/11/2026 09:10")).toBeInTheDocument();
    expect(within(filas[1]).getByText("Kyverum caído desde las 9:10")).toBeInTheDocument();

    expect(within(filas[2]).getByText("Conductor")).toBeInTheDocument();
    expect(within(filas[2]).getByText("Kyverum restablecido")).toBeInTheDocument();
  });

  it("AC2 · elegir Verifik para placa, escribir el motivo, guardar y confirmar actualiza la fila", async () => {
    const user = userEvent.setup();
    putRuntContingency.mockResolvedValue({
      kind: "vehicle_plate",
      provider: "kyverum_runt",
      reason: "Verifik lento",
      updatedByName: "Ana Pérez",
      updatedAt: "2026-11-19T17:00:00Z",
    });
    getRuntContingency.mockResolvedValue({
      items: items().map((i) => (i.kind === "vehicle_plate" ? { ...i, provider: null } : i)),
    });
    renderPanel();
    const dialogo = await abrirEditar(user, "Vehículo por placa");

    await user.selectOptions(within(dialogo).getByLabelText("Proveedor"), "verifik");
    await user.type(within(dialogo).getByLabelText(/Motivo/), "Kyverum caído desde las 9:10");
    await user.click(within(dialogo).getByRole("button", { name: "Guardar" }));

    // Confirmación corta antes de enviar.
    expect(putRuntContingency).not.toHaveBeenCalled();
    expect(await screen.findByTestId("contingencia-runt-confirmar")).toHaveTextContent("Verifik");
    putRuntContingency.mockResolvedValue({
      kind: "vehicle_plate",
      provider: "verifik",
      reason: "Kyverum caído desde las 9:10",
      updatedByName: "Ana Pérez",
      updatedAt: "2026-11-19T14:10:00Z",
    });
    await user.click(screen.getByRole("button", { name: "Confirmar" }));

    await waitFor(() =>
      expect(putRuntContingency).toHaveBeenCalledWith("vehicle_plate", {
        provider: "verifik",
        reason: "Kyverum caído desde las 9:10",
      }),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    const fila = screen.getByText("Vehículo por placa").closest("tr") as HTMLElement;
    expect(within(fila).getByText("Verifik")).toBeInTheDocument();
    expect(within(fila).getByText("Kyverum caído desde las 9:10")).toBeInTheDocument();
  });

  it("AC3 · sin motivo Guardar queda deshabilitado y el campo indica que es obligatorio", async () => {
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrirEditar(user, "Conductor");

    expect(within(dialogo).getByRole("button", { name: "Guardar" })).toBeDisabled();
    const motivo = within(dialogo).getByLabelText(/Motivo/);
    expect(motivo).toBeRequired();
    expect(within(dialogo).getByText("El motivo es obligatorio.")).toBeInTheDocument();

    await user.type(motivo, "   ");
    expect(within(dialogo).getByRole("button", { name: "Guardar" })).toBeDisabled();
    expect(motivo).toHaveAttribute("maxlength", "500");
  });

  it("AC3 · el selector del conductor solo ofrece los proveedores de conductor", async () => {
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrirEditar(user, "Conductor");
    const select = within(dialogo).getByLabelText<HTMLSelectElement>("Proveedor");
    expect(Array.from(select.options).map((o) => o.value)).toEqual([
      "",
      "kyverum_runt_conductor",
      "verifik_conductor",
    ]);
  });

  it("AC4 · apagar: «Sin contingencia» con motivo envía provider null y la fila queda sin contingencia", async () => {
    const user = userEvent.setup();
    putRuntContingency.mockResolvedValue({
      kind: "vehicle_plate",
      provider: null,
      reason: "Kyverum restablecido",
      updatedByName: "Ana Pérez",
      updatedAt: "2026-11-19T18:00:00Z",
    });
    renderPanel();
    const dialogo = await abrirEditar(user, "Vehículo por placa");

    await user.selectOptions(within(dialogo).getByLabelText("Proveedor"), "");
    await user.type(within(dialogo).getByLabelText(/Motivo/), "Kyverum restablecido");
    await user.click(within(dialogo).getByRole("button", { name: "Guardar" }));
    await user.click(await screen.findByRole("button", { name: "Confirmar" }));

    await waitFor(() =>
      expect(putRuntContingency).toHaveBeenCalledWith("vehicle_plate", {
        provider: null,
        reason: "Kyverum restablecido",
      }),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    const fila = screen.getByText("Vehículo por placa").closest("tr") as HTMLElement;
    expect(within(fila).getByText("Sin contingencia")).toBeInTheDocument();
  });

  it("AC5 · si la carga falla muestra el motivo exacto (503) y permite reintentar", async () => {
    const user = userEvent.setup();
    getRuntContingency.mockRejectedValueOnce(
      new ApiError(503, "El servicio de consultas no responde. Intenta de nuevo en unos minutos."),
    );
    renderPanel();

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("El servicio de consultas no responde. Intenta de nuevo en unos minutos.");

    await user.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Vehículo por VIN")).toBeInTheDocument();
    expect(getRuntContingency).toHaveBeenCalledTimes(2);
  });

  it("AC5 · si guardar falla muestra el motivo exacto y deja reintentar", async () => {
    const user = userEvent.setup();
    putRuntContingency.mockRejectedValueOnce(
      new ApiError(503, "El servicio de consultas no responde. Intenta de nuevo en unos minutos."),
    );
    renderPanel();
    const dialogo = await abrirEditar(user, "Vehículo por VIN");

    await user.selectOptions(within(dialogo).getByLabelText("Proveedor"), "verifik");
    await user.type(within(dialogo).getByLabelText(/Motivo/), "Kyverum caído");
    await user.click(within(dialogo).getByRole("button", { name: "Guardar" }));
    await user.click(await screen.findByRole("button", { name: "Confirmar" }));

    const alerta = await within(await screen.findByRole("dialog")).findByRole("alert");
    expect(alerta).toHaveTextContent("El servicio de consultas no responde. Intenta de nuevo en unos minutos.");
    // Sigue abierto, con los datos escritos, para reintentar.
    expect(screen.getByLabelText(/Motivo/)).toHaveValue("Kyverum caído");
    expect(screen.getByRole("button", { name: "Guardar" })).toBeEnabled();
  });
});

describe("ContingenciaRuntPanel · acceso (HU #14649 AC6)", () => {
  const token = (payload: Record<string, unknown>) =>
    `${Buffer.from(JSON.stringify({ alg: "none" })).toString("base64url")}.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.`;

  it("AC6 · solo SuperAdmin o el permiso runt_contingency.manage ven la sección", () => {
    expect(canManageRuntContingency({ role: "SuperAdmin" } as JwtPayload)).toBe(true);
    expect(canManageRuntContingency({ role: "AdminCompany", permissions: [] } as unknown as JwtPayload)).toBe(false);
    expect(canManageRuntContingency(null)).toBe(false);
  });

  it("AC6 · un AdminCompany no entra a la ruta por URL", () => {
    const decision = evaluateAdminAccess(
      token({ sub: "u1", role: "AdminCompany", permissions: ["runt_confirmation.settings.manage"] }),
      "/admin/plataforma/contingencia-runt",
    );
    expect(decision.allowed).toBe(false);
  });

  it("AC6 · el Super Admin sí entra a la ruta", () => {
    expect(
      evaluateAdminAccess(token({ sub: "u1", role: "SuperAdmin" }), "/admin/plataforma/contingencia-runt").allowed,
    ).toBe(true);
  });
});

describe("Pantalla de la empresa (HU #14649 AC7)", () => {
  it("AC7 · «Proveedores de consulta RUNT» no muestra ningún aviso de contingencia", () => {
    render(
      <ConsultaProvidersSection
        form={formFromSettings({
          tenantId: "t1",
          switchesMatricula: { allowInitialRegistration: true, allowMiscNewVehicles: true, onlyOwnVehicles: false },
          baulFirmasActivo: false,
          preasignacionPlacaActiva: false,
          enrutamientoSMTP: "FLIT_SMTP",
          notificationTarget: "RADICADOR",
          metodosRecaudo: [],
        })}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByLabelText("Vehículo por placa")).toBeInTheDocument();
    // Ni consulta la contingencia ni pinta aviso alguno: solo la configuración propia de la empresa.
    expect(getRuntContingency).not.toHaveBeenCalled();
    expect(document.body.textContent ?? "").not.toMatch(/Sin contingencia|motivo|Plataforma|Super Admin/i);
  });
});
