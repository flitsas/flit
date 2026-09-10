// HU #12241 AC2/AC4 — formulario de alta/edición con vista previa en vivo (blob local antes de
// guardar, endpoint público tras guardar) y guía de tamaño recomendado junto al campo de imagen.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BannerFormPanel } from "../BannerFormPanel";
import type { Banner, BannerFormInput } from "@/lib/api/admin-banners";

function banner(overrides: Partial<Banner> = {}): Banner {
  return {
    id: "b1",
    name: "Promo verano",
    imageUrl: "/public/banners/b1/image",
    imageSha256: "hash",
    linkUrl: null,
    validFrom: null,
    validUntil: null,
    isActive: true,
    estado: "activo",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: null,
    rowVersion: 1,
    ...overrides,
  };
}

function renderPanel(overrides: Partial<React.ComponentProps<typeof BannerFormPanel>> = {}) {
  const onSubmit = vi.fn(
    (input: BannerFormInput): Promise<Banner> => {
      void input;
      return Promise.resolve(banner({ id: "new-id" }));
    },
  );
  const onSaved = vi.fn();
  render(
    <BannerFormPanel open editing={null} onClose={() => {}} onSubmit={onSubmit} onSaved={onSaved} {...overrides} />,
  );
  return { onSubmit, onSaved };
}

beforeEach(() => {
  vi.stubGlobal("URL", {
    ...URL,
    createObjectURL: vi.fn(() => "blob:mock-preview"),
    revokeObjectURL: vi.fn(),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("BannerFormPanel — AC4 guía de tamaño", () => {
  it("muestra el texto de ayuda del campo de imagen", () => {
    renderPanel();
    expect(
      screen.getByText(/tamaño recomendado: 1500 x 500 px \(proporción 3:1\)/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/máximo\s*2mb/i)).toBeInTheDocument();
  });
});

describe("BannerFormPanel — AC2 vista previa en vivo", () => {
  it("crea un object URL local al seleccionar una imagen (antes de guardar)", async () => {
    const user = userEvent.setup();
    renderPanel();

    const file = new File(["img"], "banner.png", { type: "image/png" });
    await user.upload(screen.getByLabelText(/selecciona la imagen del banner/i), file);

    expect(URL.createObjectURL).toHaveBeenCalledWith(file);
    const preview = await screen.findByAltText(/vista previa en vivo del banner/i);
    expect(preview).toHaveAttribute("src", "blob:mock-preview");
  });

  it("en edición sin archivo nuevo, la vista previa cae al endpoint público de imagen", () => {
    renderPanel({
      editing: banner({ id: "edit-1", name: "Promo invierno" }),
    });
    const preview = screen.getByAltText(/vista previa en vivo del banner/i);
    expect(preview.getAttribute("src")).toContain("/api/v1/public/banners/edit-1/image");
  });
});

describe("BannerFormPanel — validación y envío", () => {
  it("exige nombre e imagen en alta; no envía si faltan", async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderPanel();

    await user.click(screen.getByRole("button", { name: /crear banner/i }));

    expect(await screen.findByText("El nombre del banner es obligatorio.")).toBeInTheDocument();
    expect(screen.getByText("La imagen del banner es obligatoria.")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("en edición NO exige una imagen nueva (conserva la custodiada)", async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderPanel({ editing: banner() });

    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].file).toBeNull();
  });

  it("exige ambas fechas de vigencia o ninguna", async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderPanel({ editing: banner() });

    await user.type(screen.getByLabelText(/fecha inicio/i), "2026-09-01");
    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByText("Indica ambas fechas de vigencia, o ninguna.")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("envía los datos completos y llama a onSaved con la respuesta", async () => {
    const user = userEvent.setup();
    const { onSubmit, onSaved } = renderPanel();

    await user.type(screen.getByLabelText(/^nombre/i), "Promo verano");
    await user.type(screen.getByLabelText(/enlace/i), "https://flitsas.com/promo");
    const file = new File(["img"], "banner.png", { type: "image/png" });
    await user.upload(screen.getByLabelText(/selecciona la imagen del banner/i), file);

    await user.click(screen.getByRole("button", { name: /crear banner/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const input = onSubmit.mock.calls[0][0] as BannerFormInput;
    expect(input.name).toBe("Promo verano");
    expect(input.linkUrl).toBe("https://flitsas.com/promo");
    expect(input.file).toBe(file);
    expect(input.isActive).toBe(true);
    expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ id: "new-id" }));
  });
});

describe("BannerFormPanel — activar/inhabilitar", () => {
  it("nace activo por defecto en alta", async () => {
    renderPanel();
    expect(screen.getByRole("switch", { name: /banner activo/i })).toBeChecked();
  });

  it("precarga el estado real del banner en edición", () => {
    renderPanel({ editing: banner({ isActive: false }) });
    expect(screen.getByRole("switch", { name: /banner activo/i })).not.toBeChecked();
  });

  it("al desactivarlo, envía isActive: false en el submit", async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderPanel({ editing: banner() });

    await user.click(screen.getByRole("switch", { name: /banner activo/i }));
    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect((onSubmit.mock.calls[0][0] as BannerFormInput).isActive).toBe(false);
  });
});
