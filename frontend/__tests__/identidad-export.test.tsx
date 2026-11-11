// Validación de identidad por actor en Consulta personalizada: cómo se ve en pantalla y cómo sale
// en Excel y CSV (contrato v2 de reportes de identidad).
import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";

import type { XlsxSheet } from "@/lib/xlsx";
import type {
  ActorValidacionDto,
  CompanyQueryRow,
  MaxActoresPorRol,
  RolActor,
} from "@/lib/api/company-queries";

const capturado = vi.hoisted(() => ({ sheet: null as unknown }));
vi.mock("@/lib/xlsx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/xlsx")>();
  return {
    ...actual,
    buildXlsx: (sheet: XlsxSheet) => {
      capturado.sheet = sheet;
      return actual.buildXlsx(sheet);
    },
  };
});

import {
  buildCompanyQueryCsv,
  buildCompanyQueryXlsx,
} from "@/components/atom/modules/_reportes/consultas/company-columns";
import { ActoresValidacionCell } from "@/components/atom/modules/_reportes/consultas/ActoresValidacionCell";
import {
  ESTADO_VALIDACION_LABEL,
  motivoVisible,
} from "@/components/atom/modules/_reportes/consultas/identidad-validacion";
import { exportarPorLotes } from "@/components/consultas/export";

const IDENTIDAD = ["validacion_persona", "validacion_estado", "validacion_intentos", "validacion_motivo"];

function actor(rol: RolActor, ordinal: number, over: Partial<ActorValidacionDto> = {}): ActorValidacionDto {
  return {
    rol,
    ordinal,
    nombre: `${rol} ${ordinal}`,
    tipoDocumento: "CC",
    documento: `${ordinal}0001`,
    estadoValidacion: "aprobado",
    motivoRechazo: null,
    intentos: 1,
    ...over,
  };
}

function fila(actores: ActorValidacionDto[]): CompanyQueryRow {
  return { procedureInstanceId: "p1", referenceNumber: "R-1", actores } as CompanyQueryRow;
}

function max(c: number, v: number, p: number): MaxActoresPorRol {
  return { comprador: c, vendedor: v, propietario: p };
}

function hoja() {
  return capturado.sheet as XlsxSheet;
}

describe("Excel de Consulta: bloques de 5 columnas por persona", () => {
  it.each([
    ["0 actores", max(0, 0, 0), 0],
    ["1 por rol", max(1, 1, 1), 15],
    ["2 por rol", max(2, 2, 2), 30],
    ["4 por rol", max(4, 4, 4), 60],
  ])("%s → %i columnas de identidad, al final", (_n, m, esperadas) => {
    buildCompanyQueryXlsx([fila([actor("comprador", 1)])], ["referencia", ...IDENTIDAD], [], m);
    const h = hoja().columns.map((c) => c.header);
    expect(h[0]).toBe("Radicado");
    expect(h.length - 1).toBe(esperadas);
  });

  it("un traspaso 1+1 trae solo Comprador y Vendedor, con los encabezados exactos", () => {
    buildCompanyQueryXlsx([fila([actor("comprador", 1), actor("vendedor", 1)])], ["referencia", ...IDENTIDAD], [], max(1, 1, 0));
    expect(hoja().columns.map((c) => c.header)).toEqual([
      "Radicado",
      "Comprador", "Documento comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador",
      "Vendedor", "Documento vendedor", "Validación vendedor", "Intentos vendedor", "Motivo rechazo vendedor",
    ]);
  });

  it("desde el segundo actor de un rol el encabezado lleva número", () => {
    buildCompanyQueryXlsx([fila([])], IDENTIDAD, [], max(0, 0, 2));
    const h = hoja().columns.map((c) => c.header);
    expect(h).toContain("Propietario");
    expect(h).toContain("Motivo rechazo propietario");
    expect(h).toContain("Propietario 2");
    expect(h).toContain("Documento propietario 2");
    expect(h).toContain("Intentos propietario 2");
  });

  it("agrupa por persona y deja vacío el actor que no existe en la fila", () => {
    const f = fila([
      actor("vendedor", 2, { estadoValidacion: "rechazado", intentos: 3, motivoRechazo: "Documento ilegible" }),
      actor("vendedor", 1),
      actor("comprador", 1, { estadoValidacion: "sin_validacion", intentos: 0 }),
    ]);
    buildCompanyQueryXlsx([f], IDENTIDAD, [], max(1, 2, 1));
    const celdas = hoja().rows[0];
    // comprador: nombre, doc, validación, intentos, motivo
    expect(celdas.slice(0, 5)).toEqual(["comprador 1", "CC 10001", "Sin validar", 0, ""]);
    // vendedor 1 y vendedor 2 (por ordinal, aunque llegaron al revés)
    expect(celdas[5]).toBe("vendedor 1");
    expect(celdas[10]).toBe("vendedor 2");
    expect(celdas.slice(12, 15)).toEqual(["Rechazado", 3, "Documento ilegible"]);
    // propietario: no existe en la fila → celdas vacías
    expect(celdas.slice(15, 20)).toEqual([null, null, null, null, null]);
  });

  it("si el usuario oculta una columna, no se exporta", () => {
    buildCompanyQueryXlsx([fila([actor("comprador", 1)])], ["validacion_estado", "validacion_intentos"], [], max(1, 0, 0));
    expect(hoja().columns.map((c) => c.header)).toEqual(["Validación comprador", "Intentos comprador"]);
  });

  it("sin maxActoresPorRol no inventa el ancho: deja fuera las columnas de identidad", () => {
    buildCompanyQueryXlsx([fila([actor("comprador", 1)])], ["referencia", ...IDENTIDAD], []);
    expect(hoja().columns.map((c) => c.header)).toEqual(["Radicado"]);
  });
});

describe("CSV de Consulta", () => {
  it("usa los mismos encabezados y el mismo orden que el Excel", () => {
    const csv = buildCompanyQueryCsv([fila([actor("comprador", 1)])], ["referencia", ...IDENTIDAD], [], max(1, 0, 0));
    const [cabecera, datos] = csv.replace("﻿", "").split("\r\n");
    expect(cabecera).toBe(
      '"Radicado";"Comprador";"Documento comprador";"Validación comprador";"Intentos comprador";"Motivo rechazo comprador"',
    );
    expect(datos).toBe('"R-1";"comprador 1";"CC 10001";"Aprobado";"1";""');
  });
});

describe("exportarPorLotes", () => {
  it("entrega el mismo maxActoresPorRol a todas las partes", async () => {
    const partes: (MaxActoresPorRol | undefined)[] = [];
    await exportarPorLotes<number>({
      total: 5,
      pageSize: 2,
      batchSize: 2,
      maxActoresPorRol: max(1, 4, 2),
      traerPagina: async (page) => (page === 1 ? [1, 2] : page === 2 ? [3, 4] : [5]),
      volcar: (_l, parte) => partes.push(parte.maxActoresPorRol),
    });
    expect(partes).toHaveLength(3);
    for (const p of partes) expect(p).toEqual(max(1, 4, 2));
  });
});

describe("nada revela cómo se validó", () => {
  it("la tabla de etiquetas no menciona fuente alguna", () => {
    const todo = Object.values(ESTADO_VALIDACION_LABEL).join(" ");
    expect(todo).not.toMatch(/kyverum|manual|proveedor|simulad|migraci/i);
    expect(ESTADO_VALIDACION_LABEL.aprobado).toBe("Aprobado");
    // Los códigos que viajan en el JSON tampoco dicen «manual».
    expect(Object.keys(ESTADO_VALIDACION_LABEL).join(" ")).not.toMatch(/manual/);
    expect(ESTADO_VALIDACION_LABEL.esperando_captura).toBe("Esperando captura del cliente");
    expect(ESTADO_VALIDACION_LABEL.pendiente_revision).toBe("Pendiente de revisión");
  });

  it("un motivo que nombre la fuente no llega a pantalla ni a archivo", () => {
    expect(motivoVisible("Rechazado por Kyverum")).toBe("Validación rechazada");
    expect(motivoVisible("Revisión manual fallida")).toBe("Validación rechazada");
    expect(motivoVisible("Error en webhook del scorer mock")).toBe("Validación rechazada");
    expect(motivoVisible("Documento ilegible")).toBe("Documento ilegible");
    expect(motivoVisible("  ")).toBeNull();
    // Las frases reales del servidor se muestran tal cual, con su punto final.
    expect(motivoVisible("El rostro no coincide con el documento.")).toBe(
      "El rostro no coincide con el documento.",
    );
    expect(motivoVisible("No se pudo verificar la identidad con las imágenes recibidas.")).toBe(
      "No se pudo verificar la identidad con las imágenes recibidas.",
    );
    const csv = buildCompanyQueryCsv(
      [fila([actor("comprador", 1, { estadoValidacion: "rechazado", motivoRechazo: "Proveedor sin respuesta" })])],
      IDENTIDAD,
      [],
      max(1, 0, 0),
    );
    expect(csv).not.toMatch(/kyverum|manual|proveedor/i);
  });
});

describe("pantalla: una línea de 46 px por actor", () => {
  const actores = [
    actor("comprador", 1, { nombre: "Ana Pérez" }),
    actor("vendedor", 1, { estadoValidacion: "rechazado", intentos: 2, motivoRechazo: "Documento ilegible" }),
  ];

  it("las cuatro columnas pintan el mismo número de líneas, en el mismo orden", () => {
    for (const campo of ["persona", "validacion", "intentos", "motivo"] as const) {
      const { unmount } = render(<ActoresValidacionCell actores={actores} campo={campo} />);
      const lineas = screen.getAllByTestId(`actor-${campo}`);
      expect(lineas).toHaveLength(2);
      for (const l of lineas) expect(l.className).toContain("h-[46px]");
      unmount();
    }
  });

  it("Persona: nombre y debajo «Rol · documento»", () => {
    render(<ActoresValidacionCell actores={actores} campo="persona" />);
    const [primera] = screen.getAllByTestId("actor-persona");
    expect(within(primera).getByText("Ana Pérez")).toBeTruthy();
    expect(within(primera).getByText(/^Comprador · CC /)).toBeTruthy();
  });

  it("Validación, Intentos y Motivo; «—» cuando no hay motivo", () => {
    const { unmount } = render(<ActoresValidacionCell actores={actores} campo="validacion" />);
    expect(screen.getByText("Aprobado")).toBeTruthy();
    expect(screen.getByText("Rechazado")).toBeTruthy();
    unmount();
    render(<ActoresValidacionCell actores={actores} campo="motivo" />);
    expect(screen.getByText("—")).toBeTruthy();
    expect(screen.getByTitle("Documento ilegible")).toBeTruthy();
  });

  it("el chip neutro no usa --border como color de texto", () => {
    render(
      <ActoresValidacionCell
        actores={[actor("comprador", 1, { estadoValidacion: "sin_validacion", intentos: 0 })]}
        campo="validacion"
      />,
    );
    const chip = screen.getByText("Sin validar");
    expect(chip.getAttribute("style")).not.toMatch(/var\(--border\)/);
    expect(chip.style.color).toBe("var(--badge-neutral-fg)");
    expect(chip.style.background).toBe("var(--badge-neutral-bg)");
  });

  it("con un nombre larguísimo, Persona y Motivo tienen ancho máximo y el alto no cambia", () => {
    const largo = "BANCOLOMBIA S.A, ADEMÁS PODRÁ GIRAR BAJO LA DENOMINACIÓN ".repeat(4).trim();
    for (const [campo, tope] of [["persona", "max-w-[250px]"], ["motivo", "max-w-[260px]"]] as const) {
      const { unmount } = render(
        <ActoresValidacionCell
          actores={[actor("comprador", 1, { nombre: largo, estadoValidacion: "rechazado", motivoRechazo: largo })]}
          campo={campo}
        />,
      );
      expect(screen.getByTestId(`actores-${campo}`).className).toContain(tope);
      expect(screen.getByTestId(`actor-${campo}`).className).toContain("h-[46px]");
      unmount();
    }
    render(<ActoresValidacionCell actores={[actor("comprador", 1, { nombre: largo })]} campo="persona" />);
    expect(screen.getByTitle(largo).className).toContain("truncate");
  });

  it("sin actores deja un solo «—» del mismo alto", () => {
    render(<ActoresValidacionCell actores={[]} campo="persona" />);
    expect(screen.getByText("—").className).toContain("h-[46px]");
  });
});
