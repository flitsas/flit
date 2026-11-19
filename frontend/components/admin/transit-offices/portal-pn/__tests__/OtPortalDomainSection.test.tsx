// HU #14385 — dominios del portal del organismo en la pestaña Diseño. La API se simula: sin red ni dominio real.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
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
    getPortalDominios: vi.fn(),
    registerPortalDominio: vi.fn(),
    verifyPortalDominio: vi.fn(),
    setPortalDominioPrincipal: vi.fn(),
    deletePortalDominio: vi.fn(),
  };
});

import {
  deletePortalDominio,
  getPortalDominios,
  registerPortalDominio,
  setPortalDominioPrincipal,
  verifyPortalDominio,
} from "@/lib/api/admin-ot-portal-dominio";

const ID = "ot-1";
const scope = { transitOfficeId: ID };

function dominio(over: Partial<DominioPortal> = {}): DominioPortal {
  return {
    id: "d1",
    host: "tramites.transitodemo.gov.co",
    principal: false,
    estado: "pendiente",
    motivo: null,
    dns: [
      {
        tipo: "TXT",
        nombre: "_flit-verify.tramites.transitodemo.gov.co",
        valor: "flit-verify=abc",
      },
      { tipo: "A", nombre: "tramites.transitodemo.gov.co", valor: "203.0.113.10" },
    ],
    rowVersion: 1,
    ...over,
  };
}

const varios = () => [
  dominio({ id: "d1", host: "uno.transitodemo.gov.co", estado: "activo", principal: true }),
  dominio({ id: "d2", host: "dos.transitodemo.gov.co", estado: "activo" }),
  dominio({ id: "d3", host: "tres.transitodemo.gov.co", estado: "pendiente" }),
  dominio({
    id: "d4",
    host: "cuatro.transitodemo.gov.co",
    estado: "fallido",
    motivo: "No encontramos el registro TXT en cuatro.transitodemo.gov.co.",
  }),
];

const tarjeta = (host: string) => screen.getByRole("article", { name: `Dominio ${host}` });

beforeEach(() => {
  vi.resetAllMocks();
});

describe("OtPortalDomainSection", () => {
  it("sin dominios muestra el estado vacío y el formulario para agregar", async () => {
    vi.mocked(getPortalDominios).mockResolvedValue([]);
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    expect(
      await screen.findByText("Este organismo aún no tiene un dominio registrado."),
    ).toBeInTheDocument();
    expect(getPortalDominios).toHaveBeenCalledWith(scope);
    expect(screen.getByRole("button", { name: "Registrar dominio" })).toBeInTheDocument();
  });

  it("lista varios dominios con su estado, motivo exacto y principal en texto", async () => {
    vi.mocked(getPortalDominios).mockResolvedValue(varios());
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByRole("article", { name: "Dominio uno.transitodemo.gov.co" });

    expect(
      await within(tarjeta("uno.transitodemo.gov.co")).findByText("Activo"),
    ).toBeInTheDocument();
    expect(
      within(tarjeta("uno.transitodemo.gov.co")).getByText("Dominio principal"),
    ).toBeInTheDocument();
    expect(
      within(tarjeta("dos.transitodemo.gov.co")).queryByText("Dominio principal"),
    ).not.toBeInTheDocument();
    expect(
      await within(tarjeta("tres.transitodemo.gov.co")).findByText("Pendiente de comprobación"),
    ).toBeInTheDocument();
    expect(
      await within(tarjeta("cuatro.transitodemo.gov.co")).findByText(
        "No encontramos el registro TXT en cuatro.transitodemo.gov.co.",
      ),
    ).toBeInTheDocument();
    expect(
      await within(tarjeta("tres.transitodemo.gov.co")).findByText("flit-verify=abc"),
    ).toBeInTheDocument();
  });

  it("«Hacer principal» solo aparece en un dominio activo que no es el principal", async () => {
    vi.mocked(getPortalDominios).mockResolvedValue(varios());
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByRole("article", { name: "Dominio uno.transitodemo.gov.co" });
    expect(screen.getAllByRole("button", { name: /Hacer principal/ })).toHaveLength(1);
    expect(
      within(tarjeta("dos.transitodemo.gov.co")).getByRole("button", { name: /Hacer principal/ }),
    ).toBeInTheDocument();
  });

  it("hacer principal llama al servicio y recarga la lista", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominios)
      .mockResolvedValueOnce(varios())
      .mockResolvedValue(varios().map((d) => ({ ...d, principal: d.id === "d2" })));
    vi.mocked(setPortalDominioPrincipal).mockResolvedValue(
      dominio({ id: "d2", principal: true, estado: "activo" }),
    );
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByRole("article", { name: "Dominio dos.transitodemo.gov.co" });

    await user.click(
      within(tarjeta("dos.transitodemo.gov.co")).getByRole("button", { name: /Hacer principal/ }),
    );

    expect(setPortalDominioPrincipal).toHaveBeenCalledWith("d2", scope);
    await waitFor(() =>
      expect(
        within(tarjeta("dos.transitodemo.gov.co")).getByText("Dominio principal"),
      ).toBeInTheDocument(),
    );
    expect(
      within(tarjeta("uno.transitodemo.gov.co")).queryByText("Dominio principal"),
    ).not.toBeInTheDocument();
  });

  it("agrega un dominio y aparece en la lista pendiente con sus registros DNS", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominios).mockResolvedValueOnce([]).mockResolvedValue([dominio()]);
    vi.mocked(registerPortalDominio).mockResolvedValue(dominio());
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByText("Este organismo aún no tiene un dominio registrado.");

    await user.type(screen.getByLabelText("Agregar un dominio"), "tramites.transitodemo.gov.co");
    await user.click(screen.getByRole("button", { name: "Registrar dominio" }));

    await waitFor(() =>
      expect(registerPortalDominio).toHaveBeenCalledWith("tramites.transitodemo.gov.co", scope),
    );
    const t = await screen.findByRole("article", { name: "Dominio tramites.transitodemo.gov.co" });
    expect(await within(t).findByText("Pendiente de comprobación")).toBeInTheDocument();
    expect(screen.getByLabelText("Agregar un dominio")).toHaveValue("");
  });

  it("valida el formato del host en el cliente sin llamar al servidor", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominios).mockResolvedValue([]);
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByText("Este organismo aún no tiene un dominio registrado.");
    await user.type(screen.getByLabelText("Agregar un dominio"), "https://malo");
    await user.click(screen.getByRole("button", { name: "Registrar dominio" }));
    expect(await screen.findByText(/no tiene un formato válido/)).toBeInTheDocument();
    expect(registerPortalDominio).not.toHaveBeenCalled();
  });

  it("al llegar al tope muestra el motivo exacto del servidor", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominios).mockResolvedValue(varios());
    vi.mocked(registerPortalDominio).mockRejectedValue(
      new ApiError(422, "El organismo ya tiene el máximo de 10 dominios.", { code: "X" }),
    );
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByRole("article", { name: "Dominio uno.transitodemo.gov.co" });

    await user.type(screen.getByLabelText("Agregar un dominio"), "once.transitodemo.gov.co");
    await user.click(screen.getByRole("button", { name: "Registrar dominio" }));

    expect(
      await screen.findByText("El organismo ya tiene el máximo de 10 dominios."),
    ).toBeInTheDocument();
  });

  it("Comprobar ahora usa el id del dominio y refleja el nuevo estado", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominios).mockResolvedValue([dominio({ id: "d3" })]);
    vi.mocked(verifyPortalDominio).mockResolvedValue(dominio({ id: "d3", estado: "verificado" }));
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await user.click(await screen.findByRole("button", { name: "Comprobar ahora" }));
    expect(verifyPortalDominio).toHaveBeenCalledWith("d3", scope);
    expect(await screen.findByText("Comprobado")).toBeInTheDocument();
  });

  it("falla la carga: muestra el error", async () => {
    vi.mocked(getPortalDominios).mockRejectedValue(new ApiError(500, "x"));
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    expect(await screen.findByText("No se pudieron cargar los dominios.")).toBeInTheDocument();
  });

  it("retirar confirma con el texto de los otros dominios y el enlace del punto, y llama con el id", async () => {
    const user = userEvent.setup();
    vi.mocked(getPortalDominios)
      .mockResolvedValueOnce(varios())
      .mockResolvedValue(varios().slice(0, 2));
    vi.mocked(deletePortalDominio).mockResolvedValue(undefined);
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByRole("article", { name: "Dominio tres.transitodemo.gov.co" });

    await user.click(
      within(tarjeta("tres.transitodemo.gov.co")).getByRole("button", { name: /Retirar/ }),
    );
    const confirmacion = screen.getByRole("alertdialog");
    expect(confirmacion).toHaveTextContent(
      "El portal sigue en sus otros dominios o en el enlace del punto.",
    );
    await user.click(within(confirmacion).getByRole("button", { name: "Retirar" }));

    await waitFor(() => expect(deletePortalDominio).toHaveBeenCalledWith("d3", scope));
    await waitFor(() =>
      expect(
        screen.queryByRole("article", { name: "Dominio tres.transitodemo.gov.co" }),
      ).not.toBeInTheDocument(),
    );
  });

  it("no ofrece la regla de un solo dominio y siempre deja agregar", async () => {
    vi.mocked(getPortalDominios).mockResolvedValue(varios());
    render(<OtPortalDomainSection transitOfficeId={ID} />);
    await screen.findByRole("article", { name: "Dominio uno.transitodemo.gov.co" });
    expect(screen.queryByText(/retira primero este/)).not.toBeInTheDocument();
    expect(screen.getByLabelText("Agregar un dominio")).toBeInTheDocument();
  });
});
