// HU #12241 AC1 — listado administrable de banners: columnas Nombre, Imagen (endpoint público),
// Tiene enlace, Estado (color por tone semántico), Fecha inicio/fin ("Sin fecha programada" si no
// aplica) y Acciones (Editar/Eliminar).
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BannerListTable } from "../BannerListTable";
import type { Banner } from "@/lib/api/admin-banners";

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

function renderTable(items: Banner[], overrides: Partial<React.ComponentProps<typeof BannerListTable>> = {}) {
  const onEdit = vi.fn();
  const onDelete = vi.fn();
  render(
    <BannerListTable
      items={items}
      totalCount={items.length}
      page={1}
      pageSize={20}
      onPageChange={() => {}}
      onEdit={onEdit}
      onDelete={onDelete}
      {...overrides}
    />,
  );
  return { onEdit, onDelete };
}

describe("BannerListTable", () => {
  it("muestra la imagen desde el endpoint público (nunca el campo imageUrl crudo)", () => {
    renderTable([banner({ id: "abc-123" })]);
    const img = screen.getByAltText(/vista previa del banner promo verano/i) as HTMLImageElement;
    expect(img.src).toContain("/api/v1/public/banners/abc-123/image");
  });

  it("marca 'Tiene enlace' SI cuando el banner tiene linkUrl", () => {
    renderTable([banner({ linkUrl: "https://flitsas.com/promo" })]);
    expect(screen.getByText("SÍ")).toBeInTheDocument();
  });

  it("marca 'Tiene enlace' NO cuando el banner no tiene linkUrl", () => {
    renderTable([banner({ linkUrl: null })]);
    expect(screen.getByText("NO")).toBeInTheDocument();
  });

  it.each([
    ["activo", "Activo"],
    ["inactivo", "Inactivo"],
    ["expirado", "Expirado"],
    ["programado", "Programado"],
  ] as const)("pinta el estado %s con su etiqueta %s", (estado, label) => {
    renderTable([banner({ estado })]);
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it('muestra "Sin fecha programada" cuando no hay validFrom/validUntil', () => {
    renderTable([banner({ validFrom: null, validUntil: null })]);
    const messages = screen.getAllByText(/sin fecha programada/i);
    expect(messages).toHaveLength(2);
  });

  it("formatea las fechas cuando sí hay vigencia configurada", () => {
    renderTable([banner({ validFrom: "2026-09-01T00:00:00Z", validUntil: "2026-09-30T23:59:59Z" })]);
    expect(screen.getByText(/inicio: 01\/09\/2026/i)).toBeInTheDocument();
    expect(screen.getByText(/fin: 30\/09\/2026/i)).toBeInTheDocument();
  });

  it("dispara onEdit / onDelete desde las acciones de la fila", async () => {
    const user = userEvent.setup();
    const { onEdit, onDelete } = renderTable([banner()]);

    await user.click(screen.getByRole("button", { name: /editar promo verano/i }));
    expect(onEdit).toHaveBeenCalledWith(expect.objectContaining({ id: "b1" }));

    await user.click(screen.getByRole("button", { name: /eliminar promo verano/i }));
    expect(onDelete).toHaveBeenCalledWith(expect.objectContaining({ id: "b1" }));
  });
});
