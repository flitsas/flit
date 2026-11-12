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
import {
  ActoresValidacionCell,
  renderCeldaIdentidad,
} from "@/components/atom/modules/_reportes/consultas/ActoresValidacionCell";
import {
  ESTADO_VALIDACION_LABEL,
  motivoVisible,
} from "@/components/atom/modules/_reportes/consultas/identidad-validacion";
import { exportarPorLotes } from "@/components/consultas/export";

const COMPRADOR = ["comprador", "validacion_comprador", "intentos_comprador", "motivo_comprador"];
const VENDEDOR = ["vendedor", "validacion_vendedor", "intentos_vendedor", "motivo_vendedor"];
const OCHO = [...COMPRADOR, ...VENDEDOR];

const BLOQUE = (rol: string, n = 1) => {
  const sufijo = n > 1 ? ` ${n}` : "";
  const nombre = rol[0].toUpperCase() + rol.slice(1);
  return [`${nombre}${sufijo}`, `Validación ${rol}${sufijo}`, `Intentos ${rol}${sufijo}`, `Motivo rechazo ${rol}${sufijo}`];
};

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

function fila(actores: ActorValidacionDto[], over: Partial<CompanyQueryRow> = {}): CompanyQueryRow {
  return { procedureInstanceId: "p1", referenceNumber: "R-1", placa: "ABC123", actores, ...over } as CompanyQueryRow;
}

function max(c: number, v: number, p: number): MaxActoresPorRol {
  return { comprador: c, vendedor: v, propietario: p };
}

function hoja() {
  return capturado.sheet as XlsxSheet;
}

describe("Excel de Consulta: bloques por persona, en su sitio", () => {
  const traspaso = fila([
    actor("comprador", 2, { estadoValidacion: "rechazado", intentos: 3, motivoRechazo: "El rostro no coincide con el documento." }),
    actor("comprador", 1),
    actor("vendedor", 1, { estadoValidacion: "sin_validacion", intentos: 0 }),
  ]);

  it("traspaso 2 compradores + 1 vendedor: cada bloque donde va su columna, no al final", () => {
    buildCompanyQueryXlsx([traspaso], ["referencia", ...OCHO, "prenda"], [], max(2, 1, 0));
    expect(hoja().columns.map((c) => c.header)).toEqual([
      "Radicado",
      ...BLOQUE("comprador"),
      ...BLOQUE("comprador", 2),
      ...BLOQUE("vendedor"),
      "Prenda",
    ]);
    const celdas = hoja().rows[0];
    expect(celdas[0]).toBe("R-1");
    expect(celdas.slice(1, 5)).toEqual(["comprador 1", "Aprobado", 1, ""]);
    expect(celdas.slice(5, 9)).toEqual(["comprador 2", "Rechazado", 3, "El rostro no coincide con el documento."]);
    expect(celdas.slice(9, 13)).toEqual(["vendedor 1", "Sin validar", 0, ""]);
    expect(celdas[13]).toBe("No");
  });

  it("matrícula con propietario: va en las columnas de comprador y Vendedor queda vacío", () => {
    const matricula = fila([actor("propietario", 1, { nombre: "Dueño Uno" })]);
    buildCompanyQueryXlsx([matricula], OCHO, [], max(0, 0, 1));
    expect(hoja().columns.map((c) => c.header)).toEqual([...BLOQUE("comprador"), ...BLOQUE("vendedor")]);
    expect(hoja().rows[0]).toEqual(["Dueño Uno", "Aprobado", 1, "", null, null, null, null]);
  });

  it("un maxActoresPorRol incompleto no hace desaparecer la columna Comprador", () => {
    const incompleto = (m: Partial<MaxActoresPorRol>) => m as MaxActoresPorRol;
    buildCompanyQueryXlsx([fila([])], COMPRADOR, [], incompleto({ comprador: 2 }));
    expect(hoja().columns).toHaveLength(8); // sin propietario: 2 bloques
    buildCompanyQueryXlsx([fila([])], COMPRADOR, [], incompleto({ propietario: 3 }));
    expect(hoja().columns).toHaveLength(12); // sin comprador: 3 bloques
    buildCompanyQueryXlsx([fila([])], OCHO, [], incompleto({}));
    expect(hoja().columns.map((c) => c.header)).toEqual([...BLOQUE("comprador"), ...BLOQUE("vendedor")]);
    expect(hoja().columns.every((c) => c.header && !/NaN/.test(c.header))).toBe(true);
  });

  it("el ancho del grupo comprador es max(comprador, propietario), mínimo 1", () => {
    buildCompanyQueryXlsx([fila([])], COMPRADOR, [], max(1, 0, 3));
    expect(hoja().columns).toHaveLength(12);
    buildCompanyQueryXlsx([fila([])], OCHO, [], max(0, 0, 0));
    expect(hoja().columns.map((c) => c.header)).toEqual([...BLOQUE("comprador"), ...BLOQUE("vendedor")]);
  });

  it.each([
    ["0", max(0, 0, 0), 8],
    ["1+1", max(1, 1, 0), 8],
    ["2+2", max(2, 2, 0), 16],
    ["4+4", max(4, 4, 0), 32],
  ])("ancho con máximos %s → %i columnas", (_n, m, esperadas) => {
    buildCompanyQueryXlsx([fila([])], OCHO, [], m);
    expect(hoja().columns).toHaveLength(esperadas);
  });

  it("una columna oculta no sale en ningún bloque", () => {
    buildCompanyQueryXlsx([traspaso], ["comprador", "validacion_comprador", "motivo_comprador", "vendedor"], [], max(2, 1, 0));
    const h = hoja().columns.map((c) => c.header);
    expect(h).toEqual([
      "Comprador", "Validación comprador", "Motivo rechazo comprador",
      "Comprador 2", "Validación comprador 2", "Motivo rechazo comprador 2",
      "Vendedor",
    ]);
    expect(h.some((x) => /Intentos/.test(x))).toBe(false);
  });

  it("una consulta vieja que solo pide «comprador» sale con Comprador, Comprador 2…", () => {
    buildCompanyQueryXlsx([traspaso], ["referencia", "comprador"], [], max(2, 1, 0));
    expect(hoja().columns.map((c) => c.header)).toEqual(["Radicado", "Comprador", "Comprador 2"]);
    expect(hoja().rows[0]).toEqual(["R-1", "comprador 1", "comprador 2"]);
  });

  it("solo con el rol validación visible, el bloque va en el sitio de la primera visible", () => {
    buildCompanyQueryXlsx([traspaso], ["referencia", "intentos_comprador", "prenda"], [], max(1, 0, 0));
    expect(hoja().columns.map((c) => c.header)).toEqual(["Radicado", "Intentos comprador", "Prenda"]);
  });

  it("sin maxActoresPorRol no inventa el ancho: queda «Comprador»/«Vendedor» con el texto de siempre", () => {
    const vieja = fila([], { comprador: "Texto Comprador", vendedor: "Texto Vendedor" });
    buildCompanyQueryXlsx([vieja], ["referencia", ...OCHO], []);
    expect(hoja().columns.map((c) => c.header)).toEqual(["Radicado", "Comprador", "Vendedor"]);
    expect(hoja().rows[0]).toEqual(["R-1", "Texto Comprador", "Texto Vendedor"]);
  });

  it("sin actores en la fila, el primer bloque cae al texto viejo y no hay Documento", () => {
    const vieja = fila([], { comprador: "Texto Comprador", vendedor: null });
    buildCompanyQueryXlsx([vieja], OCHO, [], max(1, 1, 0));
    expect(hoja().rows[0]).toEqual(["Texto Comprador", null, null, null, null, null, null, null]);
    expect(hoja().columns.some((c) => /Documento/.test(c.header))).toBe(false);
  });
});

describe("CSV de Consulta", () => {
  it("usa los mismos encabezados, el mismo orden y el mismo sitio que el Excel", () => {
    const f = fila([actor("comprador", 1), actor("vendedor", 1)]);
    const csv = buildCompanyQueryCsv([f], ["referencia", ...OCHO, "prenda"], [], max(1, 1, 0));
    const [cabecera, datos] = csv.replace("\ufeff", "").split("\r\n");
    expect(cabecera).toBe(
      ["Radicado", ...BLOQUE("comprador"), ...BLOQUE("vendedor"), "Prenda"].map((h) => `"${h}"`).join(";"),
    );
    expect(datos).toBe('"R-1";"comprador 1";"Aprobado";"1";"";"vendedor 1";"Aprobado";"1";"";"No"');
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
    expect(motivoVisible("Rechazado según la fuente")).toBe("Validación rechazada");
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
      OCHO,
      [],
      max(1, 0, 0),
    );
    expect(csv).not.toMatch(/kyverum|manual|proveedor/i);
  });
});

describe("pantalla: una línea de 46 px por actor", () => {
  // Dos compradores en el mismo rol: una celda por columna, una línea por actor.
  const actores = [
    actor("comprador", 1, { nombre: "Ana Pérez" }),
    actor("comprador", 2, { estadoValidacion: "rechazado", intentos: 2, motivoRechazo: "Documento ilegible" }),
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

  it("Comprador: nombre y debajo el documento, sin el rol", () => {
    render(<ActoresValidacionCell actores={actores} campo="persona" />);
    const [primera] = screen.getAllByTestId("actor-persona");
    expect(within(primera).getByText("Ana Pérez")).toBeTruthy();
    expect(within(primera).getByText("CC 10001")).toBeTruthy();
  });

  it("renderCeldaIdentidad: el propietario va en Comprador, Vendedor dice «—»", () => {
    const f = { actores: [actor("propietario", 1, { nombre: "Dueño Uno" })] };
    const { unmount } = render(<>{renderCeldaIdentidad("comprador", f)}</>);
    expect(screen.getByText("Dueño Uno")).toBeTruthy();
    unmount();
    render(<>{renderCeldaIdentidad("vendedor", f)}</>);
    expect(screen.getByText("—")).toBeTruthy();
  });

  it("renderCeldaIdentidad: sin actores deja el texto de siempre en Comprador/Vendedor", () => {
    expect(renderCeldaIdentidad("comprador", { actores: [] })).toBeNull();
    expect(renderCeldaIdentidad("vendedor", {})).toBeNull();
    expect(renderCeldaIdentidad("placa", { actores: [] })).toBeNull();
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
