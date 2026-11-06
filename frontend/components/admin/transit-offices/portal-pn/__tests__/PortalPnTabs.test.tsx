// HU #14377 (Feature #14374) — Portal Persona Natural del organismo: entrada, vista previa, contraste, acceso y estados.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";
import type { PortalMarca } from "@/lib/api/admin-ot-portal-marca";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }), useParams: () => ({ id: "ot-1" }) }));

vi.mock("@/lib/api/admin-ot-portal-marca", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-ot-portal-marca")>("@/lib/api/admin-ot-portal-marca");
  return {
    ...actual,
    getPortalMarca: vi.fn(),
    savePortalMarcaDraft: vi.fn(),
    publishPortalMarca: vi.fn(),
    retirePortalMarca: vi.fn(),
    uploadPortalMarcaLogo: vi.fn(),
    deletePortalMarcaLogo: vi.fn(),
    uploadPortalMarcaBanner: vi.fn(),
    deletePortalMarcaBanner: vi.fn(),
    fetchPortalMarcaImage: vi.fn(async (path: string) => `blob:${path}`),
  };
});

import {
  deletePortalMarcaLogo,
  fetchPortalMarcaImage,
  getPortalMarca,
  publishPortalMarca,
  retirePortalMarca,
  savePortalMarcaDraft,
  uploadPortalMarcaBanner,
  uploadPortalMarcaLogo,
} from "@/lib/api/admin-ot-portal-marca";
import { PortalPnTabs } from "../PortalPnTabs";
import { PortalPnAccess, PORTAL_PN_NO_ACCESS_MESSAGE } from "../PortalPnAccess";

function marca(overrides: Partial<PortalMarca> = {}): PortalMarca {
  return {
    transitOfficeId: "ot-1",
    draft: {
      platformName: "Tránsito de Cali",
      colors: { primary: "#0B3D91", secondary: "#0F766E", onPrimary: "#FFFFFF" },
      logoId: "logo-1",
      bannerId: "ban-1",
    },
    published: null,
    publishedVersion: 0,
    publishedAt: null,
    publishedBy: null,
    hasUnpublishedChanges: true,
    logoUrl: "/api/v1/admin/ot/portal-marca/imagenes/logo-1",
    bannerUrl: "/api/v1/admin/ot/portal-marca/imagenes/ban-1",
    completeness: { isComplete: true, missing: [] },
    rowVersion: 3,
    ...overrides,
  };
}

const SCOPE = { transitOfficeId: "ot-1" };

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchPortalMarcaImage).mockImplementation(async (path: string) => `blob:${path}`);
});

async function abrirDiseno(data: PortalMarca = marca()) {
  vi.mocked(getPortalMarca).mockResolvedValue(data);
  const user = userEvent.setup({ applyAccept: false });
  render(<PortalPnTabs transitOfficeId="ot-1" />);
  await screen.findByLabelText("Nombre en el portal");
  return user;
}

describe("AC1 — entrada: subpestañas del portal", () => {
  it("muestra Cobro y Diseño (y no Reportes, que llega con otra HU)", async () => {
    vi.mocked(getPortalMarca).mockResolvedValue(marca());
    render(<PortalPnTabs transitOfficeId="ot-1" />);

    const tabs = within(screen.getByRole("tablist", { name: "Secciones del portal Persona Natural" })).getAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual(["Cobro", "Diseño"]);
    await screen.findByLabelText("Nombre en el portal");
  });

  it("el slot de Cobro se muestra cuando se conecta; sin él, entra en Diseño", async () => {
    vi.mocked(getPortalMarca).mockResolvedValue(marca());
    render(<PortalPnTabs transitOfficeId="ot-1" cobro={<p>Precios del organismo</p>} />);
    expect(screen.getByRole("tab", { name: "Cobro" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Precios del organismo")).toBeVisible();
    await screen.findByLabelText("Nombre en el portal");
  });

  it("la marca se pide con el organismo de la ruta y la sección de dominio queda como espacio opcional", async () => {
    vi.mocked(getPortalMarca).mockResolvedValue(marca());
    render(<PortalPnTabs transitOfficeId="ot-1" domain={<section aria-label="Dominio">Dominio aquí</section>} />);
    await screen.findByLabelText("Nombre en el portal");
    expect(getPortalMarca).toHaveBeenCalledWith(SCOPE, expect.anything());
    expect(screen.getByRole("region", { name: "Dominio" })).toBeInTheDocument();
  });
});

describe("AC2 — vista previa antes de publicar", () => {
  it("refleja el nombre, el logo y el banner del borrador sin publicar nada", async () => {
    const user = await abrirDiseno();

    const preview = screen.getByTestId("portal-pn-preview");
    await waitFor(() => expect(within(preview).getByAltText("Logo del organismo")).toHaveAttribute("src", "blob:/api/v1/admin/ot/portal-marca/imagenes/logo-1"));
    expect(within(preview).getByAltText("Banner del organismo")).toHaveAttribute("src", "blob:/api/v1/admin/ot/portal-marca/imagenes/ban-1");
    expect(fetchPortalMarcaImage).toHaveBeenCalledWith("/api/v1/admin/ot/portal-marca/imagenes/logo-1", SCOPE, expect.anything());

    const nombre = screen.getByLabelText("Nombre en el portal");
    await user.clear(nombre);
    await user.type(nombre, "Movilidad Andina");
    expect(screen.getByTestId("portal-pn-preview-name")).toHaveTextContent("Movilidad Andina");
    expect(publishPortalMarca).not.toHaveBeenCalled();
    expect(savePortalMarcaDraft).not.toHaveBeenCalled();
  });

  it("al elegir otra combinación de colores la vista previa cambia sus variables", async () => {
    const user = await abrirDiseno();
    const preview = screen.getByTestId("portal-pn-preview");
    expect(preview.style.getPropertyValue("--pv-primary")).toBe("#0B3D91");

    await user.click(screen.getByRole("button", { name: "Combinación Verde" }));
    expect(preview.style.getPropertyValue("--pv-primary")).toBe("#064E3B");
    expect(preview.style.getPropertyValue("--pv-action")).toBe("#047857");
  });

  it("sin logo ni banner la vista previa lo dice y el portal usará los de FLIT", async () => {
    await abrirDiseno(marca({ logoUrl: null, bannerUrl: null, draft: { ...marca().draft, logoId: null, bannerId: null } }));
    expect(screen.getByText("FLIT")).toBeInTheDocument();
    expect(screen.getByText("Sin banner: el portal no muestra banner.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Quitar logo" })).not.toBeInTheDocument();
  });

  it("cargar un logo lo deja en el borrador sin tocar el nombre que se está editando", async () => {
    const user = await abrirDiseno();
    vi.mocked(uploadPortalMarcaLogo).mockResolvedValue(marca({ rowVersion: 4, logoUrl: "/api/v1/admin/ot/portal-marca/imagenes/logo-2" }));
    const nombre = screen.getByLabelText("Nombre en el portal");
    await user.clear(nombre);
    await user.type(nombre, "Otro nombre");

    const file = new File([new Uint8Array(10)], "logo.png", { type: "image/png" });
    await user.upload(screen.getByLabelText("Cargar logo del portal"), file);

    await waitFor(() => expect(uploadPortalMarcaLogo).toHaveBeenCalledWith(file, SCOPE));
    expect(await screen.findByText("Imagen cargada en el borrador.")).toBeInTheDocument();
    expect(nombre).toHaveValue("Otro nombre");
    await waitFor(() => expect(fetchPortalMarcaImage).toHaveBeenCalledWith("/api/v1/admin/ot/portal-marca/imagenes/logo-2", SCOPE, expect.anything()));
  });

  it("un archivo que no es PNG, JPEG o WebP, o que pesa de más, se rechaza con el motivo exacto sin subirlo", async () => {
    const user = await abrirDiseno();
    await user.upload(screen.getByLabelText("Cargar logo del portal"), new File(["x"], "logo.gif", { type: "image/gif" }));
    expect(await screen.findByText("El logo debe ser PNG, JPEG o WebP.")).toBeInTheDocument();

    const grande = new File([new Uint8Array(2 * 1024 * 1024 + 1)], "banner.png", { type: "image/png" });
    await user.upload(screen.getByLabelText("Cargar banner del portal"), grande);
    expect(await screen.findByText("El banner pesa más de 2 MB. Elige una imagen más liviana.")).toBeInTheDocument();
    expect(uploadPortalMarcaLogo).not.toHaveBeenCalled();
    expect(uploadPortalMarcaBanner).not.toHaveBeenCalled();
  });

  it("si el servidor rechaza el archivo muestra su motivo exacto", async () => {
    const user = await abrirDiseno();
    vi.mocked(uploadPortalMarcaLogo).mockRejectedValue(new ApiError(422, "El contenido del archivo no coincide con su extensión.", { code: "PORTAL_MARCA_ARCHIVO_INVALIDO" }));
    await user.upload(screen.getByLabelText("Cargar logo del portal"), new File(["x"], "logo.png", { type: "image/png" }));
    expect(await screen.findByText("El contenido del archivo no coincide con su extensión.")).toBeInTheDocument();
  });

  it("quitar el logo llama al servidor y lo deja fuera del borrador", async () => {
    const user = await abrirDiseno();
    vi.mocked(deletePortalMarcaLogo).mockResolvedValue(marca({ rowVersion: 4, logoUrl: null, draft: { ...marca().draft, logoId: null } }));
    await user.click(screen.getByRole("button", { name: "Quitar logo" }));
    await waitFor(() => expect(deletePortalMarcaLogo).toHaveBeenCalledWith(SCOPE));
    expect(await screen.findByText(/Al publicar, el portal usa el logo FLIT/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Quitar logo" })).not.toBeInTheDocument();
  });
});

describe("AC3 — contraste insuficiente", () => {
  const POCO_CONTRASTE = marca({ draft: { ...marca().draft, colors: { primary: "#FFE066", secondary: "#FFE9A0", onPrimary: "#FFFFFF" } } });

  it("avisa, ofrece corregir y no deja publicar ni guardar hasta resolverlo", async () => {
    const user = await abrirDiseno(POCO_CONTRASTE);

    expect(screen.getByText(/no se leerían bien/)).toBeInTheDocument();
    const publicar = screen.getByRole("button", { name: /^Publicar$/ });
    expect(publicar).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Guardar borrador" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/contraste menor al mínimo/);
    expect(savePortalMarcaDraft).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Ajustar automáticamente" }));
    expect(screen.getByText(/Se lee bien/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Publicar$/ })).toBeEnabled();
  });

  it("sin nombre no se puede publicar y se dice qué falta", async () => {
    const user = await abrirDiseno();
    await user.clear(screen.getByLabelText("Nombre en el portal"));
    expect(screen.getByRole("button", { name: /^Publicar$/ })).toBeDisabled();
    expect(screen.getByText("Para publicar falta: nombre.")).toBeInTheDocument();
  });
});

describe("publicar y retirar", () => {
  it("publicar guarda el borrador, confirma en un modal y publica con la versión nueva", async () => {
    const user = await abrirDiseno();
    vi.mocked(savePortalMarcaDraft).mockResolvedValue(marca({ rowVersion: 4 }));
    vi.mocked(publishPortalMarca).mockResolvedValue(
      marca({ rowVersion: 5, published: marca().draft, publishedVersion: 1, publishedAt: "2026-11-05T15:30:00Z", hasUnpublishedChanges: false }),
    );

    await user.click(screen.getByRole("button", { name: /^Publicar$/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/de inmediato, desde la siguiente carga/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Publicar" }));

    await waitFor(() => expect(publishPortalMarca).toHaveBeenCalledWith(4, SCOPE));
    expect(savePortalMarcaDraft).toHaveBeenCalledWith(
      { platformName: "Tránsito de Cali", colors: { primary: "#0B3D91", secondary: "#0F766E", onPrimary: "#FFFFFF" }, rowVersion: 3 },
      SCOPE,
    );
    expect(await screen.findByText("Marca publicada. El portal la muestra desde la siguiente carga.")).toBeInTheDocument();
    expect(screen.getByText(/Publicada \(versión 1\)/)).toBeInTheDocument();
  });

  it("si el servidor rechaza el contraste muestra su motivo exacto", async () => {
    const user = await abrirDiseno();
    vi.mocked(savePortalMarcaDraft).mockResolvedValue(marca({ rowVersion: 4 }));
    vi.mocked(publishPortalMarca).mockRejectedValue(new ApiError(422, "El contraste del texto sobre el color principal es 3,1:1 y debe ser al menos 4,5:1.", { code: "PORTAL_MARCA_CONTRASTE" }));

    await user.click(screen.getByRole("button", { name: /^Publicar$/ }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Publicar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("El contraste del texto sobre el color principal es 3,1:1 y debe ser al menos 4,5:1.");
  });

  it("un conflicto de versión ofrece recargar la marca", async () => {
    const user = await abrirDiseno();
    vi.mocked(savePortalMarcaDraft).mockRejectedValue(new ApiError(409, "Otra persona cambió la marca. Recárgala para seguir.", { code: "PORTAL_MARCA_CONFLICTO" }));

    await user.click(screen.getByRole("button", { name: "Guardar borrador" }));
    expect(await screen.findByText("Otra persona cambió la marca. Recárgala para seguir.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Recargar la marca" }));
    await waitFor(() => expect(getPortalMarca).toHaveBeenCalledTimes(2));
  });

  it("retirar pide confirmación, usa la versión vigente y conserva el borrador", async () => {
    const publicada = marca({ published: marca().draft, publishedVersion: 2, publishedAt: "2026-11-05T15:30:00Z", hasUnpublishedChanges: false });
    const user = await abrirDiseno(publicada);
    vi.mocked(retirePortalMarca).mockResolvedValue(marca({ rowVersion: 4, published: null, publishedVersion: 2 }));

    await user.click(screen.getByRole("button", { name: "Retirar marca publicada" }));
    const dialog = await screen.findByRole("dialog");
    expect(retirePortalMarca).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole("button", { name: "Retirar" }));

    await waitFor(() => expect(retirePortalMarca).toHaveBeenCalledWith(3, SCOPE));
    expect(await screen.findByText("Marca retirada. El portal vuelve a la marca FLIT; tu borrador se conserva.")).toBeInTheDocument();
    expect(screen.getByLabelText("Nombre en el portal")).toHaveValue("Tránsito de Cali");
    expect(screen.queryByRole("button", { name: "Retirar marca publicada" })).not.toBeInTheDocument();
  });

  it("guardar borrador confirma en verde sin publicar", async () => {
    const user = await abrirDiseno();
    vi.mocked(savePortalMarcaDraft).mockResolvedValue(marca({ rowVersion: 4 }));
    await user.click(screen.getByRole("button", { name: "Guardar borrador" }));
    expect(await screen.findByText("Borrador guardado. Todavía no se ve en el portal.")).toBeInTheDocument();
    expect(publishPortalMarca).not.toHaveBeenCalled();
  });
});

describe("AC5 — estados de la pantalla", () => {
  it("cargando muestra «Armando el administrador…»", async () => {
    let resolver: (m: PortalMarca) => void = () => undefined;
    vi.mocked(getPortalMarca).mockReturnValue(new Promise<PortalMarca>((r) => (resolver = r)));
    render(<PortalPnTabs transitOfficeId="ot-1" />);
    expect(await screen.findByText("Armando el administrador…")).toBeInTheDocument();
    await act(async () => resolver(marca()));
    expect(await screen.findByLabelText("Nombre en el portal")).toBeInTheDocument();
    expect(screen.queryByText("Armando el administrador…")).not.toBeInTheDocument();
  });

  it("si falla, muestra el motivo exacto del servidor y deja reintentar", async () => {
    vi.mocked(getPortalMarca).mockRejectedValueOnce(new ApiError(403, "Tu usuario no pertenece a este organismo."));
    vi.mocked(getPortalMarca).mockResolvedValueOnce(marca());
    const user = userEvent.setup({ applyAccept: false });
    render(<PortalPnTabs transitOfficeId="ot-1" />);

    expect(await screen.findByText("Tu usuario no pertenece a este organismo.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Reintentar/i }));
    expect(await screen.findByLabelText("Nombre en el portal")).toBeInTheDocument();
  });

  it("un organismo sin marca todavía arranca con el nombre vacío y la primera combinación", async () => {
    await abrirDiseno(
      marca({ draft: { platformName: null, colors: null, logoId: null, bannerId: null }, logoUrl: null, bannerUrl: null, hasUnpublishedChanges: false, rowVersion: 0 }),
    );
    expect(screen.getByLabelText("Nombre en el portal")).toHaveValue("");
    expect(screen.getByText("Para publicar falta: nombre.")).toBeInTheDocument();
    expect(screen.getByText("Sin marca publicada: el portal usa la marca FLIT.")).toBeInTheDocument();
  });
});

describe("AC4 — sin acceso", () => {
  function tokenCon(claims: Record<string, unknown>): string {
    const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, "");
    return `${b64({ alg: "none" })}.${b64(claims)}.`;
  }

  beforeEach(() => {
    window.localStorage.clear();
    document.cookie = "flit_token=; max-age=0; path=/";
  });

  it("un usuario que no es SuperAdmin ni ot_admin ve el motivo exacto y no entra", async () => {
    window.localStorage.setItem("flit:jwt", tokenCon({ sub: "u1", role: "gestor_ot" }));
    render(
      <PortalPnAccess>
        <p>Contenido del portal</p>
      </PortalPnAccess>,
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(PORTAL_PN_NO_ACCESS_MESSAGE);
    expect(screen.queryByText("Contenido del portal")).not.toBeInTheDocument();
  });

  it.each([["ot_admin"], ["SuperAdmin"]])("%s entra", async (role) => {
    window.localStorage.setItem("flit:jwt", tokenCon({ sub: "u1", role }));
    render(
      <PortalPnAccess>
        <p>Contenido del portal</p>
      </PortalPnAccess>,
    );
    expect(await screen.findByText("Contenido del portal")).toBeInTheDocument();
  });
});
