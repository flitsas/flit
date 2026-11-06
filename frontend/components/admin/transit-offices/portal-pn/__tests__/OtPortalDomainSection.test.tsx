// HU #14385 — dominio del portal del organismo en la pestaña Diseño. La API se simula: sin red ni dominio real.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OtPortalDomainSection } from "../OtPortalDomainSection";
import { ApiError } from "@/lib/api/types";
import type { DominioPortal } from "@/lib/api/admin-ot-portal-dominio";

vi.mock("@/lib/api/admin-ot-portal-dominio", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-ot-portal-dominio")>(
    "@/lib/api/admin-ot-portal-dominio",
  );
  return {
    ...actual,
    getPortalDominio: vi.fn(),
    registerPortalDominio: vi.fn(),
    verifyPortalDominio: vi.fn(),
    deletePortalDominio: vi.fn(),
  };
});

import {
  deletePortalDominio,
  getPortalDominio,
  registerPortalDominio,
  verifyPortalDominio,
} from "@/lib/api/admin-ot-portal-dominio";

const ID = "ot-1";

function dominio(over: Partial<DominioPortal> = {}): DominioPortal {
  return {
    host: "tramites.transitodemo.gov.co",
    estado: "pendiente",
    motivo: null,
    dns: [
      { tipo: "TXT", nombre: "_flit-verify.tramites.transitodemo.gov.co", valor: "flit-verify=abc" },
      { tipo: "A", nombre: "tramites.transitodemo.gov.co", valor: "203.0.113.10" },
    ],
    rowVersion: 1,
    ...over,
  };
}

beforeEach(() => {
  vi.resetAllMocks();
});

describe("OtPortalDomainSection", () => {
  it("sin dominio muestra el estado vacío y el formulario para registrar", async () => {
    vi.mocked(getPortalDominio).mockResolvedValue(null);
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    expect(await screen.findByText("Este organismo aún no tiene un dominio registrado.")).toBeInTheDocument();
    expect(getPortalDominio).toHaveBeenCalledWith({ transitOfficeId: ID });
    expect(screen.getByRole("button", { name: "Registrar dominio" })).toBeInTheDocument();
  });

  it("registra un host y muestra el estado pendiente con las instrucciones de DNS", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominio).mockResolvedValueOnce(null).mockResolvedValue(dominio());
    vi.mocked(registerPortalDominio).mockResolvedValue(dominio());
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByText("Este organismo aún no tiene un dominio registrado.");

    await user.type(screen.getByLabelText("Dominio del portal"), "tramites.transitodemo.gov.co");
    await user.click(screen.getByRole("button", { name: "Registrar dominio" }));

    await waitFor(() =>
      expect(registerPortalDominio).toHaveBeenCalledWith("tramites.transitodemo.gov.co", { transitOfficeId: ID }),
    );
    expect(await screen.findByText("Pendiente de comprobación")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Registros DNS que debes crear" })).toBeInTheDocument();
    expect(screen.getByText("flit-verify=abc")).toBeInTheDocument();
  });

  it("valida el formato del host en el cliente sin llamar al servidor", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominio).mockResolvedValue(null);
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByText("Este organismo aún no tiene un dominio registrado.");

    await user.type(screen.getByLabelText("Dominio del portal"), "https://malo");
    await user.click(screen.getByRole("button", { name: "Registrar dominio" }));

    expect(await screen.findByText(/no tiene un formato válido/)).toBeInTheDocument();
    expect(registerPortalDominio).not.toHaveBeenCalled();
  });

  it("muestra el motivo exacto del servidor cuando el host está repetido o reservado", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominio).mockResolvedValue(null);
    vi.mocked(registerPortalDominio).mockRejectedValue(
      new ApiError(422, "Ese dominio ya lo usa otro organismo de la plataforma.", { code: "X" }),
    );
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByText("Este organismo aún no tiene un dominio registrado.");

    await user.type(screen.getByLabelText("Dominio del portal"), "repetido.gov.co");
    await user.click(screen.getByRole("button", { name: "Registrar dominio" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Ese dominio ya lo usa otro organismo de la plataforma.");
  });

  it.each([
    ["verificado", "Comprobado"],
    ["activo", "Activo"],
  ] as const)("muestra el estado %s", async (estado, texto) => {
    vi.mocked(getPortalDominio).mockResolvedValue(dominio({ estado }));
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    expect(await screen.findByText(texto)).toBeInTheDocument();
  });

  it("el estado fallido muestra el motivo exacto, sin traducirlo", async () => {
    vi.mocked(getPortalDominio).mockResolvedValue(
      dominio({ estado: "fallido", motivo: "No encontramos el registro TXT en tramites.transitodemo.gov.co." }),
    );
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    expect(await screen.findByText("Comprobación fallida")).toBeInTheDocument();
    expect(screen.getByText("No encontramos el registro TXT en tramites.transitodemo.gov.co.")).toBeInTheDocument();
  });

  it("Comprobar ahora usa el cliente del organismo y refleja el nuevo estado", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominio).mockResolvedValue(dominio());
    vi.mocked(verifyPortalDominio).mockResolvedValue(dominio({ estado: "verificado" }));
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await user.click(await screen.findByRole("button", { name: "Comprobar ahora" }));
    expect(verifyPortalDominio).toHaveBeenCalledWith({ transitOfficeId: ID });
    expect(await screen.findByText("Comprobado")).toBeInTheDocument();
  });

  it("falla la carga: muestra el error y deja reintentar", async () => {
    vi.mocked(getPortalDominio).mockRejectedValue(new ApiError(500, "x"));
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    expect(await screen.findByText("No se pudo cargar el estado del dominio.")).toBeInTheDocument();
  });

  it("retira el dominio tras confirmar y vuelve al estado vacío", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominio).mockResolvedValueOnce(dominio()).mockResolvedValue(null);
    vi.mocked(deletePortalDominio).mockResolvedValue(undefined);
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await user.click(await screen.findByRole("button", { name: /Retirar dominio/ }));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("este organismo");
    await user.click(screen.getByRole("button", { name: "Retirar" }));
    await waitFor(() => expect(deletePortalDominio).toHaveBeenCalledWith({ transitOfficeId: ID }));
    expect(await screen.findByText("Este organismo aún no tiene un dominio registrado.")).toBeInTheDocument();
  });
});
