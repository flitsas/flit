// HU #12241 AC3 (negativo) — eliminación exige confirmación previa: cancelar no borra nada,
// confirmar llama a deleteBanner (que manda confirm=true), y un error del backend mantiene el
// diálogo abierto en vez de cerrarlo silenciosamente.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BannerDeleteDialog } from "../BannerDeleteDialog";
import * as adminBanners from "@/lib/api/admin-banners";
import type { Banner } from "@/lib/api/admin-banners";

const BANNER: Banner = {
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
};

beforeEach(() => {
  vi.restoreAllMocks();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("BannerDeleteDialog", () => {
  it("no elimina nada al cancelar", async () => {
    const user = userEvent.setup();
    const deleteSpy = vi.spyOn(adminBanners, "deleteBanner");
    const onClose = vi.fn();
    render(<BannerDeleteDialog banner={BANNER} onClose={onClose} onDeleted={vi.fn()} />);

    await user.click(screen.getByRole("button", { name: /cancelar/i }));

    expect(deleteSpy).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("al confirmar, llama a deleteBanner y notifica onDeleted", async () => {
    const user = userEvent.setup();
    vi.spyOn(adminBanners, "deleteBanner").mockResolvedValue(undefined);
    const onDeleted = vi.fn();
    render(<BannerDeleteDialog banner={BANNER} onClose={vi.fn()} onDeleted={onDeleted} />);

    await user.click(screen.getByRole("button", { name: /^eliminar$/i }));

    await waitFor(() => expect(onDeleted).toHaveBeenCalledWith("b1"));
    expect(adminBanners.deleteBanner).toHaveBeenCalledWith("b1");
  });

  it("si el backend falla, muestra el error y NO cierra el diálogo", async () => {
    const user = userEvent.setup();
    vi.spyOn(adminBanners, "deleteBanner").mockRejectedValue(new Error("No se pudo eliminar el banner."));
    const onDeleted = vi.fn();
    render(<BannerDeleteDialog banner={BANNER} onClose={vi.fn()} onDeleted={onDeleted} />);

    await user.click(screen.getByRole("button", { name: /^eliminar$/i }));

    expect(await screen.findByText("No se pudo eliminar el banner.")).toBeInTheDocument();
    expect(onDeleted).not.toHaveBeenCalled();
  });
});
