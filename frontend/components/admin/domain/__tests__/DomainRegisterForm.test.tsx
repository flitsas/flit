// HU #12427 — formulario de registro/cambio/retiro de dominio (exclusivo SuperAdmin). Cubre:
// (1) validación de formato en cliente, (2) PUT de registro/cambio, (3) DELETE de retiro con
// confirmación, (4) errores 409 mapeados a texto legible.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DomainRegisterForm } from "../DomainRegisterForm";
import { ApiError } from "@/lib/api/types";
import type { TenantDomainResponse } from "@/lib/api/types";

vi.mock("@/lib/api/domain-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/domain-client")>("@/lib/api/domain-client");
  return {
    ...actual,
    registerAdminDomain: vi.fn(),
    removeAdminDomain: vi.fn(),
  };
});

import { registerAdminDomain, removeAdminDomain } from "@/lib/api/domain-client";

function domain(overrides: Partial<TenantDomainResponse> = {}): TenantDomainResponse {
  return {
    host: "app.movilidadandina.com",
    status: "pending",
    statusReason: null,
    statusChangedAt: "2026-09-10T12:00:00Z",
    verification: {
      txtName: "_flit-verify.app.movilidadandina.com",
      txtValue: "flit-verify=abc123",
      cnameName: "app.movilidadandina.com",
      cnameTarget: "edge.flitsas.online",
    },
    verifiedAt: null,
    activatedAt: null,
    certificate: { issuedAt: null, expiresAt: null },
    lastCheckedAt: null,
    nextCheckAt: null,
    graceUntil: null,
    rowVersion: 1,
    ...overrides,
  };
}

beforeEach(() => {
  vi.mocked(registerAdminDomain).mockReset();
  vi.mocked(removeAdminDomain).mockReset();
});

describe("DomainRegisterForm — validación de formato en cliente", () => {
  it("rechaza un host vacío sin llamar al backend", async () => {
    const user = userEvent.setup();
    render(<DomainRegisterForm tenantId="tenant-1" domain={null} onRegistered={vi.fn()} onRemoved={vi.fn()} />);

    await user.click(screen.getByRole("button", { name: /registrar dominio/i }));

    expect(await screen.findByText(/el dominio es obligatorio/i)).toBeInTheDocument();
    expect(registerAdminDomain).not.toHaveBeenCalled();
  });

  it("rechaza un host con esquema/puerto sin llamar al backend", async () => {
    const user = userEvent.setup();
    render(<DomainRegisterForm tenantId="tenant-1" domain={null} onRegistered={vi.fn()} onRemoved={vi.fn()} />);

    await user.type(screen.getByLabelText(/dominio de la red/i), "https://app.x.com");
    await user.click(screen.getByRole("button", { name: /registrar dominio/i }));

    expect(await screen.findByText(/formato válido/i)).toBeInTheDocument();
    expect(registerAdminDomain).not.toHaveBeenCalled();
  });
});

describe("DomainRegisterForm — registrar / cambiar (PUT)", () => {
  it("primer registro: llama a registerAdminDomain sin rowVersion previo", async () => {
    const user = userEvent.setup();
    const onRegistered = vi.fn();
    vi.mocked(registerAdminDomain).mockResolvedValue(domain());

    render(<DomainRegisterForm tenantId="tenant-1" domain={null} onRegistered={onRegistered} onRemoved={vi.fn()} />);
    await user.type(screen.getByLabelText(/dominio de la red/i), "app.movilidadandina.com");
    await user.click(screen.getByRole("button", { name: /registrar dominio/i }));

    await waitFor(() =>
      expect(registerAdminDomain).toHaveBeenCalledWith("tenant-1", {
        host: "app.movilidadandina.com",
        rowVersion: null,
      }),
    );
    expect(onRegistered).toHaveBeenCalledWith(domain());
  });

  it("con dominio vigente: el botón dice 'Cambiar dominio' y envía el rowVersion actual", async () => {
    const user = userEvent.setup();
    vi.mocked(registerAdminDomain).mockResolvedValue(domain({ host: "otro.dominio.com", rowVersion: 2 }));

    render(<DomainRegisterForm tenantId="tenant-1" domain={domain({ rowVersion: 5 })} onRegistered={vi.fn()} onRemoved={vi.fn()} />);
    const input = screen.getByLabelText(/dominio de la red/i);
    await user.clear(input);
    await user.type(input, "otro.dominio.com");
    await user.click(screen.getByRole("button", { name: /cambiar dominio/i }));

    await waitFor(() =>
      expect(registerAdminDomain).toHaveBeenCalledWith("tenant-1", { host: "otro.dominio.com", rowVersion: 5 }),
    );
    expect(screen.getByText(/reinicia el ciclo de comprobación/i)).toBeInTheDocument();
  });

  it("409 CONCURRENCY_CONFLICT se muestra con texto legible", async () => {
    const user = userEvent.setup();
    vi.mocked(registerAdminDomain).mockRejectedValue(
      new ApiError(409, "Alguien más modificó este dominio. Vuelve a cargarlo e inténtalo de nuevo.", {
        error: "CONCURRENCY_CONFLICT",
      }),
    );

    render(<DomainRegisterForm tenantId="tenant-1" domain={domain()} onRegistered={vi.fn()} onRemoved={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: /cambiar dominio/i }));

    expect(await screen.findByText(/alguien más modificó/i)).toBeInTheDocument();
  });

  it("409 DOMAIN_HOST_ALREADY_REGISTERED se muestra con texto legible", async () => {
    const user = userEvent.setup();
    vi.mocked(registerAdminDomain).mockRejectedValue(
      new ApiError(409, "Ese dominio ya está registrado por otra red.", { error: "DOMAIN_HOST_ALREADY_REGISTERED" }),
    );

    render(<DomainRegisterForm tenantId="tenant-1" domain={null} onRegistered={vi.fn()} onRemoved={vi.fn()} />);
    await user.type(screen.getByLabelText(/dominio de la red/i), "app.otrared.com");
    await user.click(screen.getByRole("button", { name: /registrar dominio/i }));

    expect(await screen.findByText(/otra red/i)).toBeInTheDocument();
  });
});

describe("DomainRegisterForm — retirar (DELETE) con confirmación", () => {
  it("sin dominio vigente no muestra el botón de retirar", () => {
    render(<DomainRegisterForm tenantId="tenant-1" domain={null} onRegistered={vi.fn()} onRemoved={vi.fn()} />);
    expect(screen.queryByRole("button", { name: /retirar dominio/i })).not.toBeInTheDocument();
  });

  it("pide confirmación antes de retirar y llama a removeAdminDomain al confirmar", async () => {
    const user = userEvent.setup();
    const onRemoved = vi.fn();
    vi.mocked(removeAdminDomain).mockResolvedValue(undefined);

    render(<DomainRegisterForm tenantId="tenant-1" domain={domain()} onRegistered={vi.fn()} onRemoved={onRemoved} />);
    await user.click(screen.getByRole("button", { name: /^retirar dominio$/i }));

    expect(removeAdminDomain).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^retirar$/i }));

    await waitFor(() => expect(removeAdminDomain).toHaveBeenCalledWith("tenant-1"));
    expect(onRemoved).toHaveBeenCalled();
  });
});
