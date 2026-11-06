import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NetworkDomainsSection } from "@/components/admin/domain/NetworkDomainsSection";
import { activateNetwork, getAdminDomain, listNetworkDomains } from "@/lib/api/domain-client";
import type { NetworkDomainsResponse, TenantDomainResponse } from "@/lib/api/types";

vi.mock("@/lib/api/domain-client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/domain-client")>();
  return {
    ...actual,
    activateNetwork: vi.fn(),
    getAdminDomain: vi.fn(),
    listNetworkDomains: vi.fn(),
  };
});

const TENANT = "8c121200-5bb7-4033-9746-5b03b04c0075";

function domain(purpose: string, host: string, overrides: Partial<TenantDomainResponse> = {}): TenantDomainResponse {
  return {
    purpose,
    host,
    status: "pending",
    statusReason: null,
    statusChangedAt: "2026-11-05T15:00:00Z",
    verification: { txtName: `_flit-verify.${host}`, txtValue: "tok-0000000000000001", aName: host, aValue: "203.0.113.10" },
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

function network(overrides: Partial<NetworkDomainsResponse["activation"]>, domains: TenantDomainResponse[] = []): NetworkDomainsResponse {
  return {
    domains,
    products: ["tramites"],
    activation: { ready: false, active: false, pending: ["HUB", "tramites"], ...overrides },
  };
}

describe("NetworkDomainsSection", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getAdminDomain).mockResolvedValue(null);
  });

  it("ofrece un dominio principal y uno por producto habilitado", async () => {
    vi.mocked(listNetworkDomains).mockResolvedValue(network({}));

    render(<NetworkDomainsSection tenantId={TENANT} />);

    expect(await screen.findByRole("heading", { name: "Dominio de Trámites" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Dominio principal" })).toBeInTheDocument();
    await waitFor(() => expect(getAdminDomain).toHaveBeenCalledWith(TENANT, "tramites"));
    expect(getAdminDomain).toHaveBeenCalledWith(TENANT, undefined);
  });

  it("no deja activar mientras falte algo, y dice qué falta", async () => {
    vi.mocked(listNetworkDomains).mockResolvedValue(network({ pending: ["tramites"] }));

    render(<NetworkDomainsSection tenantId={TENANT} />);

    expect(await screen.findByText("Falta completar: Trámites.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Activar red" })).toBeDisabled();
  });

  it("activa solo tras confirmar la advertencia del corte de acceso", async () => {
    const hub = domain("HUB", "consola.cliente.com", { status: "verified", certificate: { issuedAt: "2026-11-05T15:00:00Z", expiresAt: null } });
    vi.mocked(listNetworkDomains).mockResolvedValue(network({ ready: true, pending: [] }, [hub]));
    vi.mocked(activateNetwork).mockResolvedValue(network({ active: true, pending: [] }, [{ ...hub, status: "active" }]));
    const user = userEvent.setup();

    render(<NetworkDomainsSection tenantId={TENANT} />);

    await user.click(await screen.findByRole("button", { name: "Activar red" }));
    expect(activateNetwork).not.toHaveBeenCalled();

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/deberán entrar por consola\.cliente\.com/)).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Activar" }));

    await waitFor(() => expect(activateNetwork).toHaveBeenCalledWith(TENANT));
    expect(await screen.findByText("La red está activa. Sus usuarios entran por consola.cliente.com.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Activar red" })).not.toBeInTheDocument();
  });
});
