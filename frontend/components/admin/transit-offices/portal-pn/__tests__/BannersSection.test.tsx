// Banners del portal del organismo: administración dentro de «Diseño» (borrador, orden, límite de 6, errores).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";
import type { PortalMarca, PortalMarcaBanner } from "@/lib/api/admin-ot-portal-marca";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }), useParams: () => ({ id: "ot-1" }) }));

vi.mock("@/lib/api/admin-ot-portal-marca", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-ot-portal-marca")>("@/lib/api/admin-ot-portal-marca");
  return {
    ...actual,
    getPortalMarca: vi.fn(),
    uploadPortalMarcaBannerItem: vi.fn(),
    updatePortalMarcaBannerItem: vi.fn(),
    reorderPortalMarcaBanners: vi.fn(),
    deletePortalMarcaBannerItem: vi.fn(),
    fetchPortalMarcaImage: vi.fn(async (path: string) => `blob:${path}`),
  };
});

import {
  deletePortalMarcaBannerItem,
  getPortalMarca,
  reorderPortalMarcaBanners,
  updatePortalMarcaBannerItem,
  uploadPortalMarcaBannerItem,
} from "@/lib/api/admin-ot-portal-marca";
import { PortalPnTabs } from "../PortalPnTabs";

const SCOPE = { transitOfficeId: "ot-1" };

function ban(n: number, extra: Partial<PortalMarcaBanner> = {}): PortalMarcaBanner {
  return { imageId: `ban-${n}`, imageUrl: `/api/v1/admin/ot/portal-marca/imagenes/ban-${n}`, alt: `Banner ${n}`, linkUrl: null, ...extra };
}

function marca(banners: PortalMarcaBanner[], overrides: Partial<PortalMarca> = {}): PortalMarca {
  return {
    transitOfficeId: "ot-1",
    draft: { platformName: "Tránsito de Cali", colors: { primary: "#0B3D91", secondary: "#0F766E", onPrimary: "#FFFFFF" }, logoId: null, bannerId: null },
    published: null,
    publishedVersion: 0,
    publishedAt: null,
    publishedBy: null,
    hasUnpublishedChanges: false,
    logoUrl: null,
    bannerUrl: null,
    banners,
    carousel: [],
    completeness: { isComplete: true, missing: [] },
    rowVersion: 3,
    ...overrides,
  };
}

async function abrir(data: PortalMarca) {
  vi.mocked(getPortalMarca).mockResolvedValue(data);
  const user = userEvent.setup({ applyAccept: false });
  render(<PortalPnTabs transitOfficeId="ot-1" initialTab="diseno" />);
  await screen.findByRole("heading", { name: "Banners" });
  return user;
}

async function accion(user: ReturnType<typeof userEvent.setup>, fila: number, nombre: string) {
  const triggers = screen.getAllByRole("button", { name: /^Acciones para el banner/ });
  await user.click(triggers[fila]);
  await user.click(await screen.findByRole("menuitem", { name: nombre }));
}

async function abrirAgregar(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: /Agregar banner/ }));
  return screen.findByRole("dialog");
}

const PNG = () => new File([new Uint8Array(10)], "banner.png", { type: "image/png" });

async function llenarYEnviar(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
  await user.upload(within(dialog).getByLabelText("Imagen"), PNG());
  await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
  await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("banners — estados", () => {
  it("cargando: muestra «Armando el administrador…» y luego el gestor", async () => {
    let resolver: (m: PortalMarca) => void = () => undefined;
    vi.mocked(getPortalMarca).mockReturnValue(new Promise<PortalMarca>((r) => (resolver = r)));
    render(<PortalPnTabs transitOfficeId="ot-1" initialTab="diseno" />);
    expect(await screen.findByText("Armando el administrador…")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Banners" })).not.toBeInTheDocument();
    await act(async () => resolver(marca([])));
    expect(await screen.findByRole("heading", { name: "Banners" })).toBeInTheDocument();
  });

  it("error: muestra el motivo del servidor y no el gestor", async () => {
    vi.mocked(getPortalMarca).mockRejectedValue(new ApiError(403, "Tu usuario no pertenece a este organismo."));
    render(<PortalPnTabs transitOfficeId="ot-1" initialTab="diseno" />);
    expect(await screen.findByText("Tu usuario no pertenece a este organismo.")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Banners" })).not.toBeInTheDocument();
  });

  it("vacío: explica que el portal usa el banner de FLIT y muestra el tamaño recomendado", async () => {
    await abrir(marca([]));
    expect(screen.getByText("Sin banners: el portal usa el banner de FLIT.")).toBeInTheDocument();
    const seccion = screen.getByRole("region", { name: "Banners" });
    expect(within(seccion).getByText(/Tamaño recomendado: 1600 × 366 px/)).toBeInTheDocument();
    expect(within(seccion).getByText(/máximo 2 MB cada uno/)).toBeInTheDocument();
    expect(within(seccion).getByText(/80 % central/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Agregar banner/ })).toBeEnabled();
    expect(screen.queryByLabelText("Cargar banner del portal")).not.toBeInTheDocument();
  });

  it("lleno: lista en orden con miniatura, texto alternativo y enlace; la vista previa muestra el primero y la cantidad", async () => {
    await abrir(marca([ban(1, { linkUrl: "https://ejemplo.co/a" }), ban(2)]));
    const filas = within(screen.getByRole("list", { name: "Banners del portal" })).getAllByRole("listitem");
    expect(filas).toHaveLength(2);
    expect(within(filas[0]).getByText("https://ejemplo.co/a")).toBeInTheDocument();
    expect(within(filas[1]).getByText("Sin enlace")).toBeInTheDocument();
    await waitFor(() => expect(within(filas[0]).getByAltText("Banner 1")).toHaveAttribute("src", "blob:/api/v1/admin/ot/portal-marca/imagenes/ban-1"));
    const preview = screen.getByTestId("portal-pn-preview-banner");
    await waitFor(() => expect(within(preview).getByAltText("Primer banner del portal")).toHaveAttribute("src", "blob:/api/v1/admin/ot/portal-marca/imagenes/ban-1"));
    expect(within(preview).getByText("Banners: 2")).toBeInTheDocument();
  });

  it("solo lectura: se ven los banners pero no hay acciones", async () => {
    await abrir(marca([ban(1)], { canEdit: false }));
    expect(screen.getByText("Banner 1")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Agregar banner/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Acciones para el banner/ })).not.toBeInTheDocument();
  });

  it("al llegar a 6 el botón se deshabilita y dice por qué", async () => {
    await abrir(marca([1, 2, 3, 4, 5, 6].map((n) => ban(n))));
    expect(screen.getByRole("button", { name: /Agregar banner/ })).toBeDisabled();
    expect(screen.getByText(/Llegaste al máximo de 6 banners/)).toBeInTheDocument();
  });
});

describe("banners — agregar", () => {
  it("sube el archivo con texto alternativo y enlace, y actualiza la lista y el aviso", async () => {
    const user = await abrir(marca([]));
    const file = PNG();
    vi.mocked(uploadPortalMarcaBannerItem).mockResolvedValue(marca([ban(1, { alt: "Promo", linkUrl: "https://ejemplo.co" })], { rowVersion: 4, hasUnpublishedChanges: true }));

    const dialog = await abrirAgregar(user);
    await user.upload(within(dialog).getByLabelText("Imagen"), file);
    await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
    await user.type(within(dialog).getByLabelText("Enlace (opcional)"), "https://ejemplo.co");
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));

    await waitFor(() => expect(uploadPortalMarcaBannerItem).toHaveBeenCalledWith(file, "Promo", "https://ejemplo.co", SCOPE));
    expect(await screen.findByText("Banner agregado al borrador.")).toBeInTheDocument();
    expect(screen.getByText("Hay cambios en el borrador sin publicar.")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.queryByText("Sin banners: el portal usa el banner de FLIT.")).not.toBeInTheDocument();
  });

  it("rechaza formato y peso (2 MB) en el cliente, y exige texto alternativo, sin subir nada", async () => {
    const user = await abrir(marca([]));
    const dialog = await abrirAgregar(user);
    const input = within(dialog).getByLabelText("Imagen");

    await user.upload(input, new File(["x"], "a.gif", { type: "image/gif" }));
    expect(await within(dialog).findByText("El banner debe ser PNG, JPEG o WebP.")).toBeInTheDocument();

    await user.upload(input, new File([new Uint8Array(2 * 1024 * 1024 + 1)], "b.png", { type: "image/png" }));
    expect(await within(dialog).findByText("El banner pesa más de 2 MB. Elige una imagen más liviana.")).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(uploadPortalMarcaBannerItem).not.toHaveBeenCalled();

    await user.upload(input, PNG());
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(await within(dialog).findByText("Escribe el texto alternativo.")).toBeInTheDocument();
    expect(uploadPortalMarcaBannerItem).not.toHaveBeenCalled();
  });

  it("exige elegir una imagen", async () => {
    const user = await abrir(marca([]));
    const dialog = await abrirAgregar(user);
    await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(await within(dialog).findByText("Elige una imagen.")).toBeInTheDocument();
    expect(uploadPortalMarcaBannerItem).not.toHaveBeenCalled();
  });

  it("valida que el enlace sea http o https", async () => {
    const user = await abrir(marca([]));
    const dialog = await abrirAgregar(user);
    await user.upload(within(dialog).getByLabelText("Imagen"), PNG());
    await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
    await user.type(within(dialog).getByLabelText("Enlace (opcional)"), "javascript:alert(1)");
    expect(within(dialog).getByText("El enlace debe empezar por http:// o https://.")).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(uploadPortalMarcaBannerItem).not.toHaveBeenCalled();
  });

  it.each([
    [400, "PORTAL_MARCA_ARCHIVO_INVALIDO", "El contenido del archivo no coincide con su extensión."],
    [400, "PORTAL_MARCA_DATOS_INVALIDOS", "El texto alternativo es obligatorio."],
    [403, "PORTAL_MARCA_PROHIBIDO", "No tienes permiso para cambiar los banners."],
    [409, "BANNERS_LLENO", "Ya hay 6 banners. Quita uno para agregar otro."],
  ])("error %i %s: muestra el motivo exacto dentro del modal y deja corregir", async (status, code, message) => {
    const user = await abrir(marca([]));
    vi.mocked(uploadPortalMarcaBannerItem).mockRejectedValue(new ApiError(status, message, { code }));
    const dialog = await abrirAgregar(user);
    await llenarYEnviar(user, dialog);
    expect(await within(dialog).findByText(message)).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

describe("banners — editar, ordenar y quitar", () => {
  it("editar cambia texto alternativo y enlace (sin pedir la imagen otra vez)", async () => {
    const user = await abrir(marca([ban(1), ban(2)]));
    vi.mocked(updatePortalMarcaBannerItem).mockResolvedValue(marca([ban(1), ban(2, { alt: "Nuevo", linkUrl: "https://x.co" })], { rowVersion: 4 }));

    await accion(user, 1, "Editar");
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).queryByLabelText("Imagen")).not.toBeInTheDocument();
    const alt = within(dialog).getByLabelText("Texto alternativo");
    await user.clear(alt);
    await user.type(alt, "Nuevo");
    await user.type(within(dialog).getByLabelText("Enlace (opcional)"), "https://x.co");
    await user.click(within(dialog).getByRole("button", { name: "Guardar" }));

    await waitFor(() => expect(updatePortalMarcaBannerItem).toHaveBeenCalledWith("ban-2", { alt: "Nuevo", linkUrl: "https://x.co" }, SCOPE));
    expect(await screen.findByText("Banner actualizado en el borrador.")).toBeInTheDocument();
    expect(screen.getByText("https://x.co")).toBeInTheDocument();
  });

  it("editar: un 404 muestra su motivo y deja el modal abierto", async () => {
    const user = await abrir(marca([ban(1)]));
    vi.mocked(updatePortalMarcaBannerItem).mockRejectedValue(new ApiError(404, "El banner ya no existe.", { code: "PORTAL_MARCA_NO_ENCONTRADA" }));
    await accion(user, 0, "Editar");
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Guardar" }));
    expect(await within(dialog).findByText("El banner ya no existe.")).toBeInTheDocument();
  });

  it("bajar reordena enviando la permutación; el primero no puede subir", async () => {
    const user = await abrir(marca([ban(1), ban(2), ban(3)]));
    vi.mocked(reorderPortalMarcaBanners).mockResolvedValue(marca([ban(2), ban(1), ban(3)], { rowVersion: 4 }));

    await user.click(screen.getAllByRole("button", { name: /^Acciones para el banner/ })[0]);
    expect(await screen.findByRole("menuitem", { name: "Subir" })).toBeDisabled();
    await user.click(screen.getByRole("menuitem", { name: "Bajar" }));

    await waitFor(() => expect(reorderPortalMarcaBanners).toHaveBeenCalledWith(["ban-2", "ban-1", "ban-3"], SCOPE));
    expect(await screen.findByText("Orden de los banners guardado en el borrador.")).toBeInTheDocument();
    const filas = within(screen.getByRole("list", { name: "Banners del portal" })).getAllByRole("listitem");
    expect(within(filas[0]).getByText("Banner 2")).toBeInTheDocument();
  });

  it("subir mueve una posición hacia arriba", async () => {
    const user = await abrir(marca([ban(1), ban(2), ban(3)]));
    vi.mocked(reorderPortalMarcaBanners).mockResolvedValue(marca([ban(1), ban(3), ban(2)]));
    await accion(user, 2, "Subir");
    await waitFor(() => expect(reorderPortalMarcaBanners).toHaveBeenCalledWith(["ban-1", "ban-3", "ban-2"], SCOPE));
  });

  it("si falla el orden, el motivo se muestra en la pantalla", async () => {
    const user = await abrir(marca([ban(1), ban(2)]));
    vi.mocked(reorderPortalMarcaBanners).mockRejectedValue(new ApiError(400, "El orden no coincide con los banners actuales.", { code: "PORTAL_MARCA_DATOS_INVALIDOS" }));
    await accion(user, 0, "Bajar");
    expect(await screen.findByText("El orden no coincide con los banners actuales.")).toBeInTheDocument();
  });

  it("quitar pide confirmación y no llama a la API hasta confirmar", async () => {
    const user = await abrir(marca([ban(1), ban(2)]));
    vi.mocked(deletePortalMarcaBannerItem).mockResolvedValue(marca([ban(2)], { rowVersion: 4 }));

    await accion(user, 0, "Quitar");
    const dialog = await screen.findByRole("dialog");
    expect(deletePortalMarcaBannerItem).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole("button", { name: "Quitar" }));

    await waitFor(() => expect(deletePortalMarcaBannerItem).toHaveBeenCalledWith("ban-1", SCOPE));
    expect(await screen.findByText("Banner quitado del borrador.")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("cancelar la confirmación no quita nada", async () => {
    const user = await abrir(marca([ban(1)]));
    await accion(user, 0, "Quitar");
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Cancelar" }));
    expect(deletePortalMarcaBannerItem).not.toHaveBeenCalled();
  });

  it("si falla al quitar (403), el motivo aparece en la confirmación", async () => {
    const user = await abrir(marca([ban(1)]));
    vi.mocked(deletePortalMarcaBannerItem).mockRejectedValue(new ApiError(403, "No tienes permiso para cambiar los banners.", { code: "PORTAL_MARCA_PROHIBIDO" }));
    await accion(user, 0, "Quitar");
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Quitar" }));
    expect(await screen.findByText("No tienes permiso para cambiar los banners.")).toBeInTheDocument();
  });
});
