// HU #12431 AC1 — pestaña "Correo" del configurador: muestra real (borrador/publicada) en vez del
// mock estático de #12414. Cubre: conmutador Borrador/Publicada, 404 en "Publicada" (sin marca
// publicada), draft-partial, error + reintento, iframe aislado (sandbox/srcDoc), teclado en el
// conmutador y `source="admin"` (ficha SuperAdmin, sin conmutador, vía tenantId).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrandingPreview } from "../BrandingPreview";
import { ApiError } from "@/lib/api/types";

const getCompanyBrandingEmailSample = vi.fn();
const getNotificationSample = vi.fn();

vi.mock("@/lib/api/branding-client", () => ({
  getCompanyBrandingEmailSample: (...a: unknown[]) => getCompanyBrandingEmailSample(...a),
}));

vi.mock("@/lib/api/admin-plataforma-notificaciones", () => ({
  getNotificationSample: (...a: unknown[]) => getNotificationSample(...a),
}));

const defaultProps = {
  open: true,
  onClose: vi.fn(),
  platformName: "Movilidad Andina",
  colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" },
  logoUrl: null,
};

function draftSample(overrides: Record<string, unknown> = {}) {
  return {
    templateId: "tramites.aprobado",
    subject: "Tu trámite fue aprobado",
    html: "<p>Hola desde el borrador</p>",
    theme: { kind: "brand", platformName: "Movilidad Andina", version: 1, senderName: "Movilidad Andina" },
    ...overrides,
  };
}

async function openCorreoTab() {
  const user = userEvent.setup();
  await user.click(screen.getByRole("tab", { name: /correo/i }));
  return user;
}

beforeEach(() => {
  getCompanyBrandingEmailSample.mockReset();
  getNotificationSample.mockReset();
});

describe("BrandingPreview — pestaña Correo, source=company (AC1)", () => {
  it("por defecto pide la muestra en modo Borrador y la renderiza en un iframe aislado", async () => {
    getCompanyBrandingEmailSample.mockResolvedValue(draftSample());
    render(<BrandingPreview {...defaultProps} source="company" />);

    await openCorreoTab();

    await waitFor(() =>
      expect(getCompanyBrandingEmailSample).toHaveBeenCalledWith(
        expect.objectContaining({ source: "draft" }),
      ),
    );

    const iframe = await screen.findByTestId("branding-preview-email-iframe");
    expect(iframe).toHaveAttribute("srcdoc", "<p>Hola desde el borrador</p>");
    expect(iframe).toHaveAttribute("sandbox", "");
    expect(screen.getByText(/movilidad andina/i)).toBeInTheDocument();
  });

  it("alterna a Publicada y vuelve a pedir la muestra con source=published", async () => {
    getCompanyBrandingEmailSample.mockResolvedValue(draftSample());
    const user = userEvent.setup();
    render(<BrandingPreview {...defaultProps} source="company" />);

    await user.click(screen.getByRole("tab", { name: /correo/i }));
    await waitFor(() => expect(getCompanyBrandingEmailSample).toHaveBeenCalledTimes(1));

    getCompanyBrandingEmailSample.mockResolvedValue(
      draftSample({ theme: { kind: "brand", platformName: "Movilidad Andina", version: 2, senderName: "Movilidad Andina" } }),
    );
    await user.click(screen.getByRole("button", { name: /^publicada$/i }));

    await waitFor(() =>
      expect(getCompanyBrandingEmailSample).toHaveBeenLastCalledWith(
        expect.objectContaining({ source: "published" }),
      ),
    );
  });

  it("el conmutador Borrador/Publicada es operable por teclado", async () => {
    getCompanyBrandingEmailSample.mockResolvedValue(draftSample());
    const user = userEvent.setup();
    render(<BrandingPreview {...defaultProps} source="company" />);

    await user.click(screen.getByRole("tab", { name: /correo/i }));
    await waitFor(() => expect(getCompanyBrandingEmailSample).toHaveBeenCalledTimes(1));

    const publishedButton = screen.getByRole("button", { name: /^publicada$/i });
    publishedButton.focus();
    expect(publishedButton).toHaveFocus();

    await user.keyboard("{Enter}");
    await waitFor(() =>
      expect(getCompanyBrandingEmailSample).toHaveBeenLastCalledWith(
        expect.objectContaining({ source: "published" }),
      ),
    );
  });

  it("404 en Publicada muestra el estado vacío 'aún no hay marca publicada'", async () => {
    getCompanyBrandingEmailSample.mockResolvedValueOnce(draftSample());
    getCompanyBrandingEmailSample.mockRejectedValueOnce(new ApiError(404, "BRANDING_NOT_FOUND"));
    const user = userEvent.setup();
    render(<BrandingPreview {...defaultProps} source="company" />);

    await user.click(screen.getByRole("tab", { name: /correo/i }));
    await waitFor(() => expect(getCompanyBrandingEmailSample).toHaveBeenCalledTimes(1));

    await user.click(screen.getByRole("button", { name: /^publicada$/i }));

    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText(/aún no hay marca publicada/i)).toBeInTheDocument();
  });

  it("Publicada con kind='flit' (sin marca) también muestra el estado vacío", async () => {
    getCompanyBrandingEmailSample.mockResolvedValueOnce(draftSample());
    getCompanyBrandingEmailSample.mockResolvedValueOnce(
      draftSample({ theme: { kind: "flit", platformName: "FLIT 2.0" } }),
    );
    const user = userEvent.setup();
    render(<BrandingPreview {...defaultProps} source="company" />);

    await user.click(screen.getByRole("tab", { name: /correo/i }));
    await waitFor(() => expect(getCompanyBrandingEmailSample).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole("button", { name: /^publicada$/i }));

    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
  });

  it("draft-partial muestra el aviso de borrador incompleto", async () => {
    getCompanyBrandingEmailSample.mockResolvedValue(
      draftSample({ theme: { kind: "draft-partial", platformName: "FLIT 2.0" } }),
    );
    render(<BrandingPreview {...defaultProps} source="company" />);

    await openCorreoTab();

    expect(await screen.findByText(/borrador incompleto/i)).toBeInTheDocument();
  });

  it("error de red muestra el estado de error con reintento", async () => {
    getCompanyBrandingEmailSample.mockRejectedValueOnce(new ApiError(500, "boom"));
    const user = userEvent.setup();
    render(<BrandingPreview {...defaultProps} source="company" />);

    await user.click(screen.getByRole("tab", { name: /correo/i }));
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();

    getCompanyBrandingEmailSample.mockResolvedValue(draftSample());
    await user.click(screen.getByRole("button", { name: /reintentar/i }));

    expect(await screen.findByTestId("branding-preview-email-iframe")).toBeInTheDocument();
  });
});

describe("BrandingPreview — pestaña Correo, source=admin (ficha SuperAdmin)", () => {
  it("usa getNotificationSample con el tenantId, sin conmutador Borrador/Publicada", async () => {
    getNotificationSample.mockResolvedValue({
      templateId: "tramites.aprobado",
      subject: "Tu trámite fue aprobado",
      html: "<p>Hola desde admin</p>",
      theme: { kind: "brand", platformName: "Movilidad Andina", version: 4, senderName: "Movilidad Andina" },
    });
    render(<BrandingPreview {...defaultProps} source="admin" tenantId="tenant-1" />);

    await openCorreoTab();

    await waitFor(() =>
      expect(getNotificationSample).toHaveBeenCalledWith("tramites.aprobado", { tenantId: "tenant-1" }),
    );
    expect(screen.queryByRole("button", { name: /^borrador$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^publicada$/i })).not.toBeInTheDocument();
    expect(await screen.findByTestId("branding-preview-email-iframe")).toHaveAttribute(
      "srcdoc",
      "<p>Hola desde admin</p>",
    );
  });
});

describe("BrandingPreview — pestañas Acceso/Cabecera intactas (regresión #12414)", () => {
  it("la pestaña Acceso sigue mostrando el mock scoped con las variables --preview-*", () => {
    render(<BrandingPreview {...defaultProps} source="company" />);
    expect(screen.getByRole("tabpanel", { name: /pantalla de acceso/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /iniciar sesión/i })).toBeInTheDocument();
  });
});
