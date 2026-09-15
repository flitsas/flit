// HU #12414 — configurador de marca. Cubre: (1) los 4 estados de UI, (2) AC1 (gating lo hace el
// caller, el componente no discrimina — se prueba con source=company y source=admin), (3) AC3
// bloquea publicar con contraste insuficiente o campos faltantes, (4) AC5 el diálogo de
// publicación confirma antes de aplicar, (5) AC6 `retire` solo aparece con source="admin".
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrandingConfigurator } from "../BrandingConfigurator";
import { ApiError } from "@/lib/api/types";
import type { TenantBrandingResponse } from "@/lib/api/branding";

vi.mock("@/lib/api/branding", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/branding")>("@/lib/api/branding");
  return {
    ...actual,
    getBranding: vi.fn(),
    upsertBrandingDraft: vi.fn(),
    publishBranding: vi.fn(),
    retireBranding: vi.fn(),
    uploadBrandLogo: vi.fn(),
  };
});

import {
  getBranding,
  publishBranding,
  upsertBrandingDraft,
  retireBranding,
} from "@/lib/api/branding";

function branding(overrides: Partial<TenantBrandingResponse> = {}): TenantBrandingResponse {
  return {
    tenantId: "tenant-1",
    draft: {
      platformName: "Movilidad Andina",
      colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" },
      logoId: "logo-1",
    },
    published: null,
    publishedVersion: 0,
    publishedAt: null,
    publishedBy: null,
    hasUnpublishedChanges: true,
    logoUrl: "/api/v1/public/branding/logos/logo-1",
    completeness: { isComplete: true, missing: [] },
    rowVersion: 1,
    ...overrides,
  };
}

beforeEach(() => {
  vi.mocked(getBranding).mockReset();
  vi.mocked(upsertBrandingDraft).mockReset();
  vi.mocked(publishBranding).mockReset();
  vi.mocked(retireBranding).mockReset();
});

describe("BrandingConfigurator — estados de UI", () => {
  it("muestra skeleton de carga", () => {
    vi.mocked(getBranding).mockReturnValue(new Promise(() => {}));
    render(<BrandingConfigurator source="company" />);
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("muestra error con reintentar si la carga falla", async () => {
    vi.mocked(getBranding).mockRejectedValue(new ApiError(500, "boom"));
    render(<BrandingConfigurator source="company" />);
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /reintentar/i })).toBeInTheDocument();
  });

  it("estado 'vacío' (404 BRANDING_NOT_FOUND): muestra el aviso y el formulario en blanco para crear", async () => {
    vi.mocked(getBranding).mockRejectedValue(new ApiError(404, "BRANDING_NOT_FOUND"));
    render(<BrandingConfigurator source="company" />);

    expect(await screen.findByText(/aún no tiene identidad de marca configurada/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/nombre de la plataforma/i)).toHaveValue("");
  });

  it("estado 'lleno': precarga nombre, colores y logotipo del borrador", async () => {
    vi.mocked(getBranding).mockResolvedValue(branding());
    render(<BrandingConfigurator source="company" />);

    expect(await screen.findByLabelText(/nombre de la plataforma/i)).toHaveValue("Movilidad Andina");
    expect(screen.getByDisplayValue("#0B3D91")).toBeInTheDocument();
  });
});

describe("BrandingConfigurator — AC3 contraste y completitud bloquean publicar", () => {
  it("deshabilita Publicar cuando falta el logotipo", async () => {
    vi.mocked(getBranding).mockResolvedValue(
      branding({ draft: { platformName: "X", colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" }, logoId: null }, logoUrl: null }),
    );
    render(<BrandingConfigurator source="company" />);

    await screen.findByLabelText(/nombre de la plataforma/i);
    expect(screen.getByRole("button", { name: /^publicar$/i })).toBeDisabled();
  });

  it("deshabilita Publicar cuando el contraste texto/principal es insuficiente", async () => {
    vi.mocked(getBranding).mockResolvedValue(
      branding({ draft: { platformName: "X", colors: { primary: "#557EFF", secondary: "#00DBD5", onPrimary: "#4F74C9" }, logoId: "logo-1" } }),
    );
    render(<BrandingConfigurator source="company" />);

    await screen.findByLabelText(/nombre de la plataforma/i);
    expect(screen.getByRole("button", { name: /^publicar$/i })).toBeDisabled();
    expect(screen.getByText(/no cumple el mínimo/i)).toBeInTheDocument();
  });

  it("habilita Publicar cuando el borrador está completo y el contraste cumple", async () => {
    vi.mocked(getBranding).mockResolvedValue(branding());
    render(<BrandingConfigurator source="company" />);

    await screen.findByLabelText(/nombre de la plataforma/i);
    expect(screen.getByRole("button", { name: /^publicar$/i })).toBeEnabled();
  });
});

describe("BrandingConfigurator — AC5 publicar pide confirmación", () => {
  it("abre el diálogo de confirmación y publica solo tras confirmar", async () => {
    const user = userEvent.setup();
    vi.mocked(getBranding).mockResolvedValue(branding());
    vi.mocked(upsertBrandingDraft).mockResolvedValue(branding({ rowVersion: 2 }));
    vi.mocked(publishBranding).mockResolvedValue(
      branding({ publishedAt: "2026-09-20T15:04:05Z", publishedBy: { userId: "user-1" }, hasUnpublishedChanges: false, published: branding().draft }),
    );

    render(<BrandingConfigurator source="company" />);
    await screen.findByLabelText(/nombre de la plataforma/i);

    await user.click(screen.getByRole("button", { name: /^publicar$/i }));
    expect(await screen.findByText(/sesiones nuevas/i)).toBeInTheDocument();
    expect(publishBranding).not.toHaveBeenCalled();

    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^publicar$/i }));

    await waitFor(() => expect(publishBranding).toHaveBeenCalled());
    expect(await screen.findByText(/publicada el/i)).toBeInTheDocument();
  });
});

describe("BrandingConfigurator — AC6 retirar solo existe con source=admin", () => {
  it("source=company NUNCA muestra 'Retirar marca publicada'", async () => {
    vi.mocked(getBranding).mockResolvedValue(branding({ published: branding().draft, publishedAt: "2026-09-01T00:00:00Z" }));
    render(<BrandingConfigurator source="company" />);
    await screen.findByLabelText(/nombre de la plataforma/i);
    expect(screen.queryByRole("button", { name: /retirar marca publicada/i })).not.toBeInTheDocument();
  });

  it("source=admin con marca publicada SÍ muestra 'Retirar marca publicada'", async () => {
    vi.mocked(getBranding).mockResolvedValue(branding({ published: branding().draft, publishedAt: "2026-09-01T00:00:00Z" }));
    render(<BrandingConfigurator source="admin" tenantId="tenant-1" />);
    await screen.findByLabelText(/nombre de la plataforma/i);
    expect(screen.getByRole("button", { name: /retirar marca publicada/i })).toBeInTheDocument();
  });

  it("confirma antes de retirar", async () => {
    const user = userEvent.setup();
    vi.mocked(getBranding).mockResolvedValue(branding({ published: branding().draft, publishedAt: "2026-09-01T00:00:00Z" }));
    vi.mocked(retireBranding).mockResolvedValue(branding({ published: null, publishedAt: null }));

    render(<BrandingConfigurator source="admin" tenantId="tenant-1" />);
    await screen.findByLabelText(/nombre de la plataforma/i);

    await user.click(screen.getByRole("button", { name: /retirar marca publicada/i }));
    expect(retireBranding).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^retirar$/i }));
    await waitFor(() => expect(retireBranding).toHaveBeenCalledWith("tenant-1"));
  });
});
