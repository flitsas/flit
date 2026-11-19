// Insignia del dock (saneamiento, spec-ui §1.4 · FE-0): `badge` opcional en NavItem, pintado en la entrada del submenú, en la
// píldora del grupo y en el lanzador móvil. Oculta en 0, «99+» sobre 99 y el número real en el nombre accesible. Sin
// `badge` el DOM es el de siempre (no regresión para frontend, comparendos y hub).
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { Box } from "lucide-react";
import { buildDock, formatoInsignia, type NavCatalog, type NavItem } from "../nav";
import { Dock } from "../dock/Dock";
import { DockMobile } from "../dock/DockMobile";

// Uso de ejemplo: { key: "backoffice-saneamientos", label: "Saneamientos", href: "/backoffice/saneamientos", section: "backoffice",
//   icon, badge: 3, badgeTexto: "solicitudes sin tomar" } → «Saneamientos, 3 solicitudes sin tomar» y la insignia «3».

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));
afterEach(cleanup);

const viewer = { permissions: ["ver.secreto"], isSuperAdmin: false };

function catalogo(badge?: number, extra: Partial<NavItem> = {}): NavCatalog {
  return {
    sections: [
      { id: "operacion", label: "Operación", icon: Box, side: "left" },
      { id: "backoffice", label: "Backoffice", icon: Box, side: "right" },
    ],
    items: [
      { key: "inicio", label: "Diagnósticos", href: "/diagnosticos", section: "operacion", icon: Box },
      { key: "bo", label: "Resumen", href: "/backoffice", section: "backoffice", icon: Box },
      {
        key: "bo-san",
        label: "Saneamientos",
        href: "/backoffice/saneamientos",
        section: "backoffice",
        icon: Box,
        ...(badge === undefined ? {} : { badge, badgeTexto: "solicitudes sin tomar" }),
        ...extra,
      },
    ],
  };
}

function pintar(badge?: number, extra: Partial<NavItem> = {}) {
  const groups = buildDock(catalogo(badge, extra), viewer, "/");
  return render(
    <>
      <Dock groups={groups} homeHref="/" homeLabel="Inicio" homeIconSrc="/favicon.svg" homeActive={false} />
      <DockMobile groups={groups} homeIconSrc="/favicon.svg" />
    </>,
  );
}

/** Los ids de useId cambian entre montajes: se comparan las estructuras sin ellos. */
const sinIds = (html: string) => html.replace(/(id|aria-describedby|aria-controls|aria-labelledby)="[^"]*"/g, '$1="X"');

describe("formatoInsignia", () => {
  it("1 a 99 se muestran tal cual y más de 99 como 99+", () => {
    expect(formatoInsignia(1)).toBe("1");
    expect(formatoInsignia(99)).toBe("99");
    expect(formatoInsignia(100)).toBe("99+");
    expect(formatoInsignia(150)).toBe("99+");
  });

  it("0, negativos, no enteros, NaN o ausente no se pintan", () => {
    for (const n of [0, -1, 1.5, Number.NaN, Number.POSITIVE_INFINITY, undefined]) expect(formatoInsignia(n), String(n)).toBeNull();
  });
});

describe("buildDock con insignia", () => {
  it("copia badge y badgeTexto a la entrada y el grupo suma las hojas visibles", () => {
    const groups = buildDock(catalogo(7), viewer, "/");
    const bo = groups.find((g) => g.id === "backoffice")!;
    expect(bo.items.find((e) => e.key === "bo-san")).toMatchObject({ badge: 7, badgeTexto: "solicitudes sin tomar" });
    expect(bo.badge).toBe(7);
    expect(groups.find((g) => g.id === "operacion")!.badge).toBeUndefined();
  });

  it("una hoja que el usuario no ve no suma, y un hijo de contenedor sí", () => {
    const cat: NavCatalog = {
      sections: [{ id: "s", label: "S", icon: Box, side: "left" }],
      items: [
        { key: "oculta", label: "Oculta", href: "/o", section: "s", icon: Box, badge: 5, permission: "otro.permiso" },
        {
          key: "cont",
          label: "Contenedor",
          href: "",
          section: "s",
          icon: Box,
          children: [{ key: "hija", label: "Hija", href: "/h", section: "s", icon: Box, badge: 2 }],
        },
      ],
    };
    const [g] = buildDock(cat, viewer, "/");
    expect(g.badge).toBe(2);
  });

  it("un grupo con suma 0 no lleva insignia", () => {
    const bo = buildDock(catalogo(0), viewer, "/").find((g) => g.id === "backoffice")!;
    expect(bo.badge).toBeUndefined();
  });
});

describe("Dock y DockMobile con insignia", () => {
  it("la píldora del grupo muestra 99+ y su nombre lleva el número real", () => {
    pintar(150);
    const dock = screen.getByRole("navigation", { name: "Navegación principal" });
    const pill = within(dock).getByRole("button", { name: "Backoffice, 150 pendientes" });
    const insignia = pill.querySelector(".dock-insignia");
    expect(insignia).not.toBeNull();
    expect(insignia).toHaveTextContent("99+");
    expect(insignia).toHaveAttribute("aria-hidden", "true");
  });

  it("la entrada del submenú lleva la insignia y el nombre con su texto", () => {
    pintar(3);
    fireEvent.click(screen.getByRole("button", { name: "Backoffice, 3 pendientes" }));
    const region = screen.getByRole("region", { name: "Backoffice" });
    const entrada = within(region).getByRole("link", { name: "Saneamientos, 3 solicitudes sin tomar" });
    expect(entrada.querySelector(".dock-insignia")).toHaveTextContent("3");
    expect(within(region).getByRole("link", { name: "Resumen" }).querySelector(".dock-insignia")).toBeNull();
  });

  it("sin badgeTexto, la entrada dice «pendientes»", () => {
    const cat = catalogo(undefined, { badge: 4 });
    const groups = buildDock(cat, viewer, "/");
    render(<Dock groups={groups} homeHref="/" homeLabel="Inicio" homeIconSrc="/favicon.svg" homeActive={false} />);
    fireEvent.click(screen.getByRole("button", { name: "Backoffice, 4 pendientes" }));
    expect(screen.getByRole("link", { name: "Saneamientos, 4 pendientes" })).toBeInTheDocument();
  });

  it("una píldora que es enlace directo también lleva la insignia", () => {
    const cat: NavCatalog = {
      sections: [{ id: "s", label: "Saneamientos", icon: Box, side: "left" }],
      items: [{ key: "san", label: "Saneamientos", href: "/san", section: "s", icon: Box, badge: 2 }],
    };
    render(<Dock groups={buildDock(cat, viewer, "/")} homeHref="/" homeLabel="Inicio" homeIconSrc="/favicon.svg" homeActive={false} />);
    const enlace = screen.getByRole("link", { name: "Saneamientos, 2 pendientes" });
    expect(enlace.querySelector(".dock-insignia")).toHaveTextContent("2");
  });

  it("el lanzador móvil suma todos los grupos y la hoja repite la insignia de la entrada", () => {
    pintar(12);
    const lanzador = screen.getByRole("button", { name: "Abrir menú de navegación, 12 pendientes" });
    expect(lanzador.querySelector(".dock-insignia")).toHaveTextContent("12");
    fireEvent.click(lanzador);
    const hoja = screen.getByRole("dialog", { name: "Navegación" });
    expect(within(hoja).getByRole("link", { name: "Saneamientos, 12 solicitudes sin tomar" })).toBeInTheDocument();
  });

  it("con 0 no se pinta ninguna insignia y los nombres son los de siempre", () => {
    const { container } = pintar(0);
    expect(container.querySelector(".dock-insignia")).toBeNull();
    expect(screen.getByRole("button", { name: "Backoffice" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Abrir menú de navegación" })).toBeInTheDocument();
  });
});

describe("no regresión: sin badge el DOM es el de siempre", () => {
  it("dock cerrado, submenú abierto y hoja móvil sin cambios de marcado", () => {
    const { container } = pintar();
    const cerrado = sinIds(container.innerHTML);
    fireEvent.click(screen.getByRole("button", { name: "Backoffice" }));
    const abierto = sinIds(container.innerHTML);
    fireEvent.click(screen.getByRole("button", { name: "Abrir menú de navegación" }));
    const hoja = sinIds(container.innerHTML);
    expect({ cerrado, abierto, hoja }).toMatchSnapshot();
  });

  it("badge 0 deja exactamente el mismo DOM que sin badge", () => {
    const sin = sinIds(pintar().container.innerHTML);
    cleanup();
    const cero = sinIds(pintar(0).container.innerHTML);
    expect(cero).toBe(sin);
  });
});
