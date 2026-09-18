// HU #12427 — panel de estado del dominio. Cubre: (1) 4 estados de UI (cargando/error/vacío/lleno),
// (2) cada estado (pending/verified/active/failed) con icono + texto (no solo color) y motivo
// humano, (3) instrucciones DNS copiables (pending/failed), (4) "Comprobar ahora" — éxito,
// TXT_MISMATCH y 429 (cooldown), (5) accesibilidad básica (roles, nombres accesibles).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DomainStatusPanel } from "../DomainStatusPanel";
import { ApiError } from "@/lib/api/types";
import type { TenantDomainResponse } from "@/lib/api/types";

vi.mock("@/lib/api/domain-client", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/domain-client")>("@/lib/api/domain-client");
  return {
    ...actual,
    getAdminDomain: vi.fn(),
    getCompanyDomain: vi.fn(),
    verifyAdminDomain: vi.fn(),
    verifyCompanyDomain: vi.fn(),
  };
});

import {
  getAdminDomain,
  getCompanyDomain,
  verifyAdminDomain,
  verifyCompanyDomain,
} from "@/lib/api/domain-client";

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
    nextCheckAt: "2026-09-10T12:05:00Z",
    graceUntil: null,
    rowVersion: 1,
    ...overrides,
  };
}

beforeEach(() => {
  vi.mocked(getAdminDomain).mockReset();
  vi.mocked(getCompanyDomain).mockReset();
  vi.mocked(verifyAdminDomain).mockReset();
  vi.mocked(verifyCompanyDomain).mockReset();
});

/**
 * Después de `userEvent.setup()` — userEvent instala su propio stub de portapapeles y pisaría
 * uno definido antes (mismo patrón que `__tests__/validaciones-module.test.tsx`).
 */
function stubClipboard(): { writeText: ReturnType<typeof vi.fn> } {
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
  return { writeText };
}

describe("DomainStatusPanel — 4 estados de UI", () => {
  it("cargando: muestra skeleton", () => {
    vi.mocked(getAdminDomain).mockReturnValue(new Promise(() => {}));
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("error: muestra estado de error con reintentar", async () => {
    vi.mocked(getAdminDomain).mockRejectedValue(new ApiError(500, "boom"));
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /reintentar/i })).toBeInTheDocument();
  });

  it("vacío: la red no tiene dominio (404 => null), sin botón de comprobar", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(null);
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /comprobar ahora/i })).not.toBeInTheDocument();
  });

  it("lleno: muestra el host y el estado", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain());
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByTestId("domain-host")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /comprobar ahora/i })).toBeInTheDocument();
  });
});

describe("DomainStatusPanel — estado con icono + texto, motivo y fecha (AC1/AC5)", () => {
  it("pending: texto 'Pendiente de comprobación'", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain({ status: "pending" }));
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByText(/pendiente de comprobación/i)).toBeInTheDocument();
  });

  it("verified: texto 'Comprobado'", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain({ status: "verified" }));
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByText(/^comprobado$/i)).toBeInTheDocument();
  });

  it("active: texto 'Activo'", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain({ status: "active" }));
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByText(/^activo$/i)).toBeInTheDocument();
  });

  it("failed con motivo TXT_NOT_FOUND: muestra el motivo humano", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain({ status: "failed", statusReason: "TXT_NOT_FOUND" }));
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByText(/comprobación fallida/i)).toBeInTheDocument();
    expect(screen.getByText(/no se encontró el registro txt/i)).toBeInTheDocument();
  });

  it("muestra la fecha del último cambio", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain());
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByText(/último cambio:/i)).toBeInTheDocument();
  });

  it("con graceUntil: muestra el aviso de gracia", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(
      domain({ status: "failed", statusReason: "TXT_NOT_FOUND", graceUntil: "2026-09-20T00:00:00Z" }),
    );
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByText(/sigue operando hasta/i)).toBeInTheDocument();
  });

  it("con certificado emitido: muestra fechas de emisión/expiración", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(
      domain({
        status: "active",
        certificate: { issuedAt: "2026-09-10T00:00:00Z", expiresAt: "2026-12-10T00:00:00Z" },
      }),
    );
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    expect(await screen.findByText(/certificado emitido el/i)).toBeInTheDocument();
    expect(screen.getByText(/vence el/i)).toBeInTheDocument();
  });
});

describe("DomainStatusPanel — instrucciones DNS copiables (AC2)", () => {
  it("pending: muestra registros TXT y CNAME exactos con botón Copiar", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain());
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);

    await screen.findByTestId("domain-host");
    expect(screen.getByText("_flit-verify.app.movilidadandina.com")).toBeInTheDocument();
    expect(screen.getByText("flit-verify=abc123")).toBeInTheDocument();
    expect(screen.getByText("edge.flitsas.online")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /copiar valor del registro/i })).toHaveLength(2);
  });

  it("active: NO muestra la tabla de instrucciones DNS", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain({ status: "active" }));
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    await screen.findByTestId("domain-host");
    expect(screen.queryByText(/registros dns que debes crear/i)).not.toBeInTheDocument();
  });

  it("copiar escribe al portapapeles y confirma de forma accesible", async () => {
    const user = userEvent.setup();
    const clipboard = stubClipboard();
    vi.mocked(getAdminDomain).mockResolvedValue(domain());
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    await screen.findByTestId("domain-host");

    const [txtCopy] = screen.getAllByRole("button", { name: /copiar valor del registro txt/i });
    await user.click(txtCopy);

    expect(clipboard.writeText).toHaveBeenCalledWith("flit-verify=abc123");
    expect(await screen.findByText(/copiado al portapapeles/i)).toBeInTheDocument();
  });
});

describe("DomainStatusPanel — Comprobar ahora (AC2)", () => {
  it("éxito: pasa a verified sin recargar la página y muestra confirmación", async () => {
    const user = userEvent.setup();
    vi.mocked(getAdminDomain).mockResolvedValue(domain());
    vi.mocked(verifyAdminDomain).mockResolvedValue(domain({ status: "verified" }));

    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    await screen.findByTestId("domain-host");

    await user.click(screen.getByRole("button", { name: /comprobar ahora/i }));

    await waitFor(() => expect(verifyAdminDomain).toHaveBeenCalledWith("tenant-1"));
    expect(await screen.findByText(/^comprobado$/i)).toBeInTheDocument();
    expect(screen.getByText(/comprobación exitosa/i)).toBeInTheDocument();
  });

  it("fallo TXT_MISMATCH: muestra el motivo sin romper el panel", async () => {
    const user = userEvent.setup();
    vi.mocked(getAdminDomain).mockResolvedValue(domain());
    vi.mocked(verifyAdminDomain).mockResolvedValue(
      domain({ status: "failed", statusReason: "TXT_MISMATCH" }),
    );

    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    await screen.findByTestId("domain-host");
    await user.click(screen.getByRole("button", { name: /comprobar ahora/i }));

    expect(await screen.findByText(/no coincide con el valor esperado/i)).toBeInTheDocument();
  });

  it("429 cooldown: muestra el mensaje de espera y deshabilita el botón", async () => {
    const user = userEvent.setup();
    vi.mocked(getAdminDomain).mockResolvedValue(domain());
    vi.mocked(verifyAdminDomain).mockRejectedValue(
      new ApiError(429, "Ya se comprobó recientemente. Vuelve a intentarlo en 30 s.", {
        error: "DOMAIN_VERIFICATION_COOLDOWN",
        retryAfterSeconds: 30,
      }),
    );

    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    await screen.findByTestId("domain-host");
    const button = screen.getByRole("button", { name: /comprobar ahora/i });
    await user.click(button);

    expect(await screen.findByRole("alert")).toHaveTextContent(/30 s/);
    await waitFor(() => expect(button).toBeDisabled());
    expect(button).toHaveTextContent(/espera 30 s/i);
  });

  it("mode=company llama a verifyCompanyDomain (sin tenantId)", async () => {
    const user = userEvent.setup();
    vi.mocked(getCompanyDomain).mockResolvedValue(domain());
    vi.mocked(verifyCompanyDomain).mockResolvedValue(domain({ status: "verified" }));

    render(<DomainStatusPanel mode="company" />);
    await screen.findByTestId("domain-host");
    await user.click(screen.getByRole("button", { name: /comprobar ahora/i }));

    await waitFor(() => expect(verifyCompanyDomain).toHaveBeenCalled());
    expect(verifyAdminDomain).not.toHaveBeenCalled();
  });
});

describe("DomainStatusPanel — accesibilidad básica (AC5)", () => {
  it("el estado se anuncia con role='status', no depende solo del color", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain({ status: "active" }));
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    await screen.findByTestId("domain-host");
    const status = screen.getAllByRole("status");
    const hasTextStatus = status.some((el) => within(el).queryByText(/activo/i));
    expect(hasTextStatus).toBe(true);
  });

  it("los botones de copiar tienen nombre accesible explícito", async () => {
    vi.mocked(getAdminDomain).mockResolvedValue(domain());
    render(<DomainStatusPanel mode="admin" tenantId="tenant-1" />);
    await screen.findByTestId("domain-host");
    screen.getAllByRole("button", { name: /copiar valor del registro/i }).forEach((btn) => {
      expect(btn.getAttribute("aria-label")).toBeTruthy();
    });
  });
});
