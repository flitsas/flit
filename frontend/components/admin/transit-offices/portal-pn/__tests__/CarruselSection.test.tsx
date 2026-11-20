// Carrusel de imágenes del portal del organismo: administración dentro de «Diseño» (borrador, orden, límite, errores).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";
import type { PortalMarca, PortalMarcaCarruselItem } from "@/lib/api/admin-ot-portal-marca";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }), useParams: () => ({ id: "ot-1" }) }));

vi.mock("@/lib/api/admin-ot-portal-marca", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-ot-portal-marca")>("@/lib/api/admin-ot-portal-marca");
  return {
    ...actual,
    getPortalMarca: vi.fn(),
    uploadPortalMarcaCarrusel: vi.fn(),
    updatePortalMarcaCarrusel: vi.fn(),
    reorderPortalMarcaCarrusel: vi.fn(),
    deletePortalMarcaCarrusel: vi.fn(),
    fetchPortalMarcaImage: vi.fn(async (path: string) => `blob:${path}`),
  };
});

import {
  deletePortalMarcaCarrusel,
  getPortalMarca,
  reorderPortalMarcaCarrusel,
  updatePortalMarcaCarrusel,
  uploadPortalMarcaCarrusel,
} from "@/lib/api/admin-ot-portal-marca";
import { PortalPnTabs } from "../PortalPnTabs";

const SCOPE = { transitOfficeId: "ot-1" };

function item(n: number, extra: Partial<PortalMarcaCarruselItem> = {}): PortalMarcaCarruselItem {
  return { imageId: `img-${n}`, imageUrl: `/api/v1/admin/ot/portal-marca/imagenes/img-${n}`, alt: `Imagen ${n}`, linkUrl: null, ...extra };
}

function marca(carousel: PortalMarcaCarruselItem[], overrides: Partial<PortalMarca> = {}): PortalMarca {
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
    carousel,
    completeness: { isComplete: true, missing: [] },
    rowVersion: 3,
    ...overrides,
  };
}

async function abrir(data: PortalMarca) {
  vi.mocked(getPortalMarca).mockResolvedValue(data);
  const user = userEvent.setup({ applyAccept: false });
  render(<PortalPnTabs transitOfficeId="ot-1" initialTab="diseno" />);
  await screen.findByRole("heading", { name: "Carrusel de imágenes" });
  return user;
}

async function accion(user: ReturnType<typeof userEvent.setup>, fila: number, nombre: string) {
  const triggers = screen.getAllByRole("button", { name: /^Acciones para la imagen/ });
  await user.click(triggers[fila]);
  await user.click(await screen.findByRole("menuitem", { name: nombre }));
}

const PNG = () => new File([new Uint8Array(10)], "foto.png", { type: "image/png" });

beforeEach(() => {
  vi.clearAllMocks();
});

describe("carrusel — estados", () => {
  it("vacío: explica que el portal muestra los servicios de siempre y muestra la guía de tamaño", async () => {
    await abrir(marca([]));
    expect(screen.getByText("Sin imágenes: el portal muestra los servicios de siempre.")).toBeInTheDocument();
    const seccion = screen.getByRole("region", { name: "Carrusel de imágenes" });
    expect(within(seccion).getByText(/Tamaño recomendado: 900 × 600 px \(proporción 3:2\)\./)).toBeInTheDocument();
    expect(within(seccion).getByText(/Deja el texto y lo importante en el 80 % central/)).toBeInTheDocument();
    expect(screen.getByText(/en lugar de «Servicios Adicionales»/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Agregar imagen/ })).toBeEnabled();
  });

  it("lleno: lista en orden con miniatura, texto alternativo y enlace; la vista previa muestra la primera", async () => {
    await abrir(marca([item(1, { linkUrl: "https://ejemplo.co/a" }), item(2)]));
    const lista = screen.getByRole("list", { name: "Imágenes del carrusel" });
    const filas = within(lista).getAllByRole("listitem");
    expect(filas).toHaveLength(2);
    expect(within(filas[0]).getByText("https://ejemplo.co/a")).toBeInTheDocument();
    expect(within(filas[1]).getByText("Sin enlace")).toBeInTheDocument();
    await waitFor(() => expect(within(filas[0]).getByAltText("Imagen 1")).toHaveAttribute("src", "blob:/api/v1/admin/ot/portal-marca/imagenes/img-1"));
    const preview = screen.getByTestId("portal-pn-preview-carousel");
    await waitFor(() => expect(within(preview).getByAltText("Primera imagen del carrusel")).toHaveAttribute("src", "blob:/api/v1/admin/ot/portal-marca/imagenes/img-1"));
    expect(within(preview).getByText("Carrusel: 2 imágenes")).toBeInTheDocument();
  });

  it("solo lectura: se ven las imágenes pero no hay acciones", async () => {
    await abrir(marca([item(1)], { canEdit: false }));
    expect(screen.getByText("Imagen 1")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Agregar imagen/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Acciones para la imagen/ })).not.toBeInTheDocument();
  });

  it("al llegar a 6 el botón se deshabilita y dice por qué", async () => {
    await abrir(marca([1, 2, 3, 4, 5, 6].map((n) => item(n))));
    expect(screen.getByRole("button", { name: /Agregar imagen/ })).toBeDisabled();
    expect(screen.getByText(/Llegaste al máximo de 6 imágenes/)).toBeInTheDocument();
  });
});

describe("carrusel — agregar", () => {
  it("sube el archivo con texto alternativo y enlace, y actualiza la lista y el aviso", async () => {
    const user = await abrir(marca([]));
    const file = PNG();
    vi.mocked(uploadPortalMarcaCarrusel).mockResolvedValue(marca([item(1, { alt: "Promo", linkUrl: "https://ejemplo.co" })], { rowVersion: 4, hasUnpublishedChanges: true }));

    await user.click(screen.getByRole("button", { name: /Agregar imagen/ }));
    const dialog = await screen.findByRole("dialog");
    await user.upload(within(dialog).getByLabelText("Imagen"), file);
    await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
    await user.type(within(dialog).getByLabelText("Enlace (opcional)"), "https://ejemplo.co");
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));

    await waitFor(() => expect(uploadPortalMarcaCarrusel).toHaveBeenCalledWith(file, "Promo", "https://ejemplo.co", SCOPE));
    expect(await screen.findByText("Imagen agregada al borrador.")).toBeInTheDocument();
    expect(screen.getByText("Hay cambios en el borrador sin publicar.")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.queryByText("Sin imágenes: el portal muestra los servicios de siempre.")).not.toBeInTheDocument();
  });

  it("rechaza formato y tamaño en el cliente, y exige texto alternativo, sin subir nada", async () => {
    const user = await abrir(marca([]));
    await user.click(screen.getByRole("button", { name: /Agregar imagen/ }));
    const dialog = await screen.findByRole("dialog");
    const input = within(dialog).getByLabelText("Imagen");

    await user.upload(input, new File(["x"], "a.gif", { type: "image/gif" }));
    expect(await within(dialog).findByText("La imagen debe ser PNG, JPEG o WebP.")).toBeInTheDocument();

    await user.upload(input, new File([new Uint8Array(512 * 1024 + 1)], "b.png", { type: "image/png" }));
    expect(await within(dialog).findByText("La imagen pesa más de 512 KB. Elige una imagen más liviana.")).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(uploadPortalMarcaCarrusel).not.toHaveBeenCalled();

    await user.upload(input, PNG());
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(await within(dialog).findByText("Escribe el texto alternativo.")).toBeInTheDocument();
    expect(uploadPortalMarcaCarrusel).not.toHaveBeenCalled();
  });

  it("valida que el enlace sea http o https", async () => {
    const user = await abrir(marca([]));
    await user.click(screen.getByRole("button", { name: /Agregar imagen/ }));
    const dialog = await screen.findByRole("dialog");
    await user.upload(within(dialog).getByLabelText("Imagen"), PNG());
    await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
    await user.type(within(dialog).getByLabelText("Enlace (opcional)"), "javascript:alert(1)");
    expect(within(dialog).getByText("El enlace debe empezar por http:// o https://.")).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(uploadPortalMarcaCarrusel).not.toHaveBeenCalled();
  });

  it("si la API falla muestra su motivo exacto dentro del modal y deja corregir", async () => {
    const user = await abrir(marca([]));
    vi.mocked(uploadPortalMarcaCarrusel).mockRejectedValue(new ApiError(422, "El contenido del archivo no coincide con su extensión.", { code: "PORTAL_MARCA_ARCHIVO_INVALIDO" }));
    await user.click(screen.getByRole("button", { name: /Agregar imagen/ }));
    const dialog = await screen.findByRole("dialog");
    await user.upload(within(dialog).getByLabelText("Imagen"), PNG());
    await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(await within(dialog).findByText("El contenido del archivo no coincide con su extensión.")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("si el servidor responde que está lleno muestra su mensaje", async () => {
    const user = await abrir(marca([]));
    vi.mocked(uploadPortalMarcaCarrusel).mockRejectedValue(new ApiError(409, "El carrusel ya tiene 6 imágenes.", { code: "PORTAL_MARCA_CARRUSEL_LLENO" }));
    await user.click(screen.getByRole("button", { name: /Agregar imagen/ }));
    const dialog = await screen.findByRole("dialog");
    await user.upload(within(dialog).getByLabelText("Imagen"), PNG());
    await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    expect(await within(dialog).findByText("El carrusel ya tiene 6 imágenes.")).toBeInTheDocument();
  });

  it("un conflicto de versión ofrece recargar la marca", async () => {
    const user = await abrir(marca([]));
    vi.mocked(uploadPortalMarcaCarrusel).mockRejectedValue(new ApiError(409, "Otra persona cambió la marca. Recárgala para seguir.", { code: "PORTAL_MARCA_CONFLICTO" }));
    await user.click(screen.getByRole("button", { name: /Agregar imagen/ }));
    const dialog = await screen.findByRole("dialog");
    await user.upload(within(dialog).getByLabelText("Imagen"), PNG());
    await user.type(within(dialog).getByLabelText("Texto alternativo"), "Promo");
    await user.click(within(dialog).getByRole("button", { name: "Agregar" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Recargar la marca" })).toBeInTheDocument());
  });
});

describe("carrusel — editar, ordenar y quitar", () => {
  it("editar cambia texto alternativo y enlace", async () => {
    const user = await abrir(marca([item(1), item(2)]));
    vi.mocked(updatePortalMarcaCarrusel).mockResolvedValue(marca([item(1), item(2, { alt: "Nuevo", linkUrl: "https://x.co" })], { rowVersion: 4 }));

    await accion(user, 1, "Editar");
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).queryByLabelText("Imagen")).not.toBeInTheDocument();
    const alt = within(dialog).getByLabelText("Texto alternativo");
    await user.clear(alt);
    await user.type(alt, "Nuevo");
    await user.type(within(dialog).getByLabelText("Enlace (opcional)"), "https://x.co");
    await user.click(within(dialog).getByRole("button", { name: "Guardar" }));

    await waitFor(() => expect(updatePortalMarcaCarrusel).toHaveBeenCalledWith("img-2", { alt: "Nuevo", linkUrl: "https://x.co" }, SCOPE));
    expect(await screen.findByText("Imagen actualizada en el borrador.")).toBeInTheDocument();
    expect(screen.getByText("https://x.co")).toBeInTheDocument();
  });

  it("bajar reordena enviando la permutación; la primera no puede subir", async () => {
    const user = await abrir(marca([item(1), item(2), item(3)]));
    vi.mocked(reorderPortalMarcaCarrusel).mockResolvedValue(marca([item(2), item(1), item(3)], { rowVersion: 4 }));

    await user.click(screen.getAllByRole("button", { name: /^Acciones para la imagen/ })[0]);
    expect(await screen.findByRole("menuitem", { name: "Subir" })).toBeDisabled();
    await user.click(screen.getByRole("menuitem", { name: "Bajar" }));

    await waitFor(() => expect(reorderPortalMarcaCarrusel).toHaveBeenCalledWith(["img-2", "img-1", "img-3"], SCOPE));
    expect(await screen.findByText("Orden del carrusel guardado en el borrador.")).toBeInTheDocument();
    const filas = within(screen.getByRole("list", { name: "Imágenes del carrusel" })).getAllByRole("listitem");
    expect(within(filas[0]).getByText("Imagen 2")).toBeInTheDocument();
  });

  it("subir mueve una posición hacia arriba", async () => {
    const user = await abrir(marca([item(1), item(2), item(3)]));
    vi.mocked(reorderPortalMarcaCarrusel).mockResolvedValue(marca([item(1), item(3), item(2)]));
    await accion(user, 2, "Subir");
    await waitFor(() => expect(reorderPortalMarcaCarrusel).toHaveBeenCalledWith(["img-1", "img-3", "img-2"], SCOPE));
  });

  it("quitar pide confirmación y no llama a la API hasta confirmar", async () => {
    const user = await abrir(marca([item(1), item(2)]));
    vi.mocked(deletePortalMarcaCarrusel).mockResolvedValue(marca([item(2)], { rowVersion: 4 }));

    await accion(user, 0, "Quitar");
    const dialog = await screen.findByRole("dialog");
    expect(deletePortalMarcaCarrusel).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole("button", { name: "Quitar" }));

    await waitFor(() => expect(deletePortalMarcaCarrusel).toHaveBeenCalledWith("img-1", SCOPE));
    expect(await screen.findByText("Imagen quitada del carrusel.")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("cancelar la confirmación no quita nada", async () => {
    const user = await abrir(marca([item(1)]));
    await accion(user, 0, "Quitar");
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Cancelar" }));
    expect(deletePortalMarcaCarrusel).not.toHaveBeenCalled();
  });

  it("si falla al quitar, el motivo aparece en la confirmación", async () => {
    const user = await abrir(marca([item(1)]));
    vi.mocked(deletePortalMarcaCarrusel).mockRejectedValue(new ApiError(403, "No tienes permiso para cambiar el carrusel.", { code: "PORTAL_MARCA_PROHIBIDO" }));
    await accion(user, 0, "Quitar");
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Quitar" }));
    expect(await screen.findByText("No tienes permiso para cambiar el carrusel.")).toBeInTheDocument();
  });
});
