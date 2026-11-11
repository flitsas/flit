// HU #14402 (Feature #14400) — Pestaña «Diseño web»: logo y colores de la web, sin banner, con acceso a Contenido. La API
// de la web es un doble; la sección de diseño y las reglas de contraste son las reales.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";
import type { PortalMarca } from "@/lib/api/admin-ot-portal-marca";
import { DisenoWebTab } from "../DisenoWebTab";

vi.mock("@/lib/api/admin-pn-marca-web", () => ({
  getMarcaWeb: vi.fn(),
  saveMarcaWebDraft: vi.fn(),
  publishMarcaWeb: vi.fn(),
  retireMarcaWeb: vi.fn(),
  uploadMarcaWebLogo: vi.fn(),
  deleteMarcaWebLogo: vi.fn(),
  fetchMarcaWebImage: vi.fn(async (path: string) => `blob:${path}`),
}));

import {
  fetchMarcaWebImage,
  getMarcaWeb,
  publishMarcaWeb,
  retireMarcaWeb,
  saveMarcaWebDraft,
} from "@/lib/api/admin-pn-marca-web";

function marca(over: Partial<PortalMarca> = {}): PortalMarca {
  return {
    transitOfficeId: "",
    draft: {
      platformName: null,
      colors: { primary: "#0B3D91", secondary: "#0F766E", onPrimary: "#FFFFFF" },
      logoId: "logo-w",
      bannerId: null,
    },
    published: null,
    publishedVersion: 0,
    publishedAt: null,
    publishedBy: null,
    hasUnpublishedChanges: true,
    logoUrl: "/api/v1/admin/plataforma/persona-natural/marca-web/imagenes/logo-w",
    bannerUrl: null,
    completeness: { isComplete: true, missing: [] },
    rowVersion: 2,
    ...over,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchMarcaWebImage).mockImplementation(async (path: string) => `blob:${path}`);
});

async function abrir(data: PortalMarca = marca(), onIrABanners = vi.fn()) {
  vi.mocked(getMarcaWeb).mockResolvedValue(data);
  const user = userEvent.setup({ applyAccept: false });
  render(<DisenoWebTab onIrABanners={onIrABanners} />);
  await screen.findByRole("heading", { name: "Diseño de la web de persona" });
  return { user, onIrABanners };
}

describe("AC2 — sin banner, con acceso a Contenido", () => {
  it("configura logo y colores, no ofrece banner y el acceso lleva a Contenido", async () => {
    const { user, onIrABanners } = await abrir();

    expect(screen.getByLabelText("Cargar logo de la web")).toBeInTheDocument();
    expect(screen.queryByLabelText("Cargar banner del portal")).not.toBeInTheDocument();
    expect(screen.queryByText(/Sin banner propio/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Combinación Verde" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Ir a los banners de Contenido/ }));
    expect(onIrABanners).toHaveBeenCalledTimes(1);
  });

  it("pide la marca de la web (sin organismo) y la vista previa usa el logo del borrador", async () => {
    await abrir();
    expect(getMarcaWeb).toHaveBeenCalledWith(expect.anything());
    const preview = screen.getByTestId("portal-pn-preview");
    await waitFor(() =>
      expect(within(preview).getByAltText("Logo de la web")).toHaveAttribute(
        "src",
        expect.stringContaining("blob:"),
      ),
    );
    expect(preview).toHaveAttribute("aria-label", "Vista previa de la web de persona");
  });
});

describe("proceso: borrador, publicar y retirar", () => {
  it("guarda el borrador y publica sin exigir nombre", async () => {
    vi.mocked(saveMarcaWebDraft).mockResolvedValue(marca({ rowVersion: 3 }));
    vi.mocked(publishMarcaWeb).mockResolvedValue(
      marca({
        rowVersion: 4,
        published: marca().draft,
        publishedVersion: 1,
        publishedAt: "2026-11-06T10:00:00Z",
        hasUnpublishedChanges: false,
      }),
    );
    const { user } = await abrir();

    await user.click(screen.getByRole("button", { name: /Guardar borrador/ }));
    expect(
      await screen.findByText("Borrador guardado. Todavía no se ve en la web."),
    ).toBeInTheDocument();
    expect(saveMarcaWebDraft).toHaveBeenCalledWith({
      platformName: null,
      colors: expect.objectContaining({ primary: "#0B3D91" }),
      rowVersion: 2,
    });

    await user.click(screen.getByRole("button", { name: /^Publicar$/ }));
    const dialogo = await screen.findByRole("dialog");
    await user.click(within(dialogo).getByRole("button", { name: /^Publicar$/ }));
    expect(
      await screen.findByText("Diseño publicado. La web lo muestra desde la siguiente carga."),
    ).toBeInTheDocument();
    expect(publishMarcaWeb).toHaveBeenCalledWith(3);
  });

  it("retira lo publicado", async () => {
    vi.mocked(retireMarcaWeb).mockResolvedValue(marca({ rowVersion: 5 }));
    const { user } = await abrir(
      marca({
        published: marca().draft,
        publishedVersion: 1,
        publishedAt: "2026-11-06T10:00:00Z",
        hasUnpublishedChanges: false,
      }),
    );

    await user.click(screen.getByRole("button", { name: /Retirar marca publicada/ }));
    const dialogo = await screen.findByRole("dialog");
    await user.click(within(dialogo).getByRole("button", { name: "Retirar" }));
    expect(
      await screen.findByText(
        "Diseño retirado. La web vuelve a la marca FLIT; tu borrador se conserva.",
      ),
    ).toBeInTheDocument();
    expect(retireMarcaWeb).toHaveBeenCalledWith(2);
  });

  it("sin marca publicada lo dice con las palabras de la web", async () => {
    await abrir();
    expect(screen.getByText("Sin diseño publicado: la web usa la marca FLIT.")).toBeInTheDocument();
  });
});

describe("estados", () => {
  it("error de carga: muestra el motivo y permite reintentar", async () => {
    vi.mocked(getMarcaWeb).mockRejectedValueOnce(new ApiError(500, "El servidor no respondió."));
    render(<DisenoWebTab onIrABanners={vi.fn()} />);
    expect(await screen.findByText("El servidor no respondió.")).toBeInTheDocument();

    vi.mocked(getMarcaWeb).mockResolvedValue(marca());
    await userEvent.click(screen.getByRole("button", { name: /Reintentar/ }));
    expect(await screen.findByLabelText("Cargar logo de la web")).toBeInTheDocument();
  });

  it("una falla al publicar muestra el motivo exacto del servidor", async () => {
    vi.mocked(saveMarcaWebDraft).mockResolvedValue(marca({ rowVersion: 3 }));
    vi.mocked(publishMarcaWeb).mockRejectedValue(
      new ApiError(422, "Falta el logo de la web.", { code: "PORTAL_MARCA_INCOMPLETA" }),
    );
    const { user } = await abrir();

    await user.click(screen.getByRole("button", { name: /^Publicar$/ }));
    await user.click(
      within(await screen.findByRole("dialog")).getByRole("button", { name: /^Publicar$/ }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent("Falta el logo de la web.");
  });
});
