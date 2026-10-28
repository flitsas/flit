import { describe, expect, it } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { RespuestaProveedor } from "@/components/tablero/RespuestaFuente";
import { conductorProveedor, empresaProveedor } from "@/lib/tablero/__tests__/datosDetalle";

// Uso de ejemplo: <RespuestaProveedor datos={persona.respuesta_proveedor} etiqueta="Respuesta del proveedor · Propietario 1" />
// pinta TODO el JSON del proveedor (HU #14154, RUNT persona y RUES): ninguna clave se oculta ni se aplana (ids internos, firma
// y campos técnicos incluidos). Objeto anidado → subsección plegable; lista de objetos → tabla con la unión de columnas; lista
// de valores simples → lista; vacío o null → «—»; lista vacía → «Sin registros». Claves conocidas en español; desconocidas con
// su nombre original. «Expandir todo / Contraer todo» abre y cierra todas sus subsecciones, a cualquier profundidad.

const ETIQUETA = "Respuesta del proveedor · Propietario 1";

const filaDe = (tabla: HTMLElement, campo: string) => {
  const fila = within(tabla)
    .getAllByRole("row")
    .find((r) => r.querySelector("th[scope='row']")?.textContent === campo);
  if (!fila) throw new Error(`Sin fila ${campo}`);
  return fila;
};
const valorDe = (tabla: HTMLElement, campo: string) => within(filaDe(tabla, campo)).getByRole("cell");
const rotulos = (tabla: HTMLElement) => within(tabla).getAllByRole("rowheader").map((r) => r.textContent);
const columnas = (tabla: HTMLElement) => within(tabla).getAllByRole("columnheader").map((c) => c.textContent);

describe("HU #14154 AC1 — RespuestaProveedor: todas las claves, sin ocultar ninguna", () => {
  it("pinta los id_*, la firma y los campos técnicos que la respuesta de la fuente oculta", () => {
    const { container } = render(<RespuestaProveedor datos={conductorProveedor()} etiqueta={ETIQUETA} />);
    const tabla = screen.getByRole("table", { name: ETIQUETA });
    expect(rotulos(tabla)).toEqual(["OK", "Mensaje", "id_consulta", "Multas", "Restricciones generales", "Estado HTTP", "Duración (ms)", "campo_sin_traducir", "Observaciones", "Metadatos"]);
    expect(valorDe(tabla, "id_consulta").textContent).toBe("CONS-FICT-0001");
    expect(valorDe(tabla, "Estado HTTP").textContent).toBe("200");
    expect(valorDe(tabla, "Duración (ms)").textContent).toBe("812");
    const firma = screen.getByRole("region", { name: "Firma" });
    expect(valorDe(within(firma).getByRole("table", { name: "Firma" }), "Hash").textContent).toBe("ZmlybWEtZmljdGljaWE=");
    const identidad = screen.getByRole("region", { name: "Identidad" });
    expect(valorDe(within(identidad).getByRole("table", { name: "Identidad" }), "id_tipo_documento").textContent).toBe("1");
    expect(container.textContent).toContain("L-1");
  });

  it("cada clave de cada nivel del JSON aparece (contrato: ninguna se descarta)", () => {
    const datos = empresaProveedor();
    const { container } = render(<RespuestaProveedor datos={datos} etiqueta={ETIQUETA} />);
    const hojas: string[] = [];
    const recorrer = (v: unknown) => {
      if (Array.isArray(v)) v.forEach(recorrer);
      else if (v && typeof v === "object") Object.values(v).forEach(recorrer);
      else if (typeof v === "string" && v) hojas.push(v);
    };
    recorrer(datos);
    for (const h of hojas) expect(container.textContent).toContain(h);
    expect(screen.getByRole("table", { name: ETIQUETA })).toHaveTextContent("FICT1");
  });

  it("no lanza con un JSON que no es objeto (lista o valor suelto)", () => {
    expect(() => render(<RespuestaProveedor datos={[{ a: 1 }, { b: 2 }]} etiqueta={ETIQUETA} />)).not.toThrow();
    expect(screen.getByRole("table", { name: ETIQUETA })).toHaveTextContent("2");
    expect(() => render(<RespuestaProveedor datos="TEXTO PLANO" etiqueta="Otra" />)).not.toThrow();
    expect(screen.getByText("TEXTO PLANO")).toBeInTheDocument();
  });
});

describe("HU #14154 AC2 — presentación según el tipo de valor", () => {
  it("objeto anidado → subsección plegable; el de primer nivel abre y los internos arrancan cerrados", () => {
    render(<RespuestaProveedor datos={empresaProveedor()} etiqueta={ETIQUETA} />);
    const datos = screen.getByRole("region", { name: "Datos" });
    expect(datos.querySelector("details")).toHaveAttribute("open");
    const registro = within(datos).getByRole("region", { name: "Registro mercantil" });
    const plegable = registro.querySelector("details");
    expect(plegable).not.toBeNull();
    expect(plegable).not.toHaveAttribute("open");
    expect(within(registro).getByText("Registro mercantil").closest("summary")).not.toBeNull();
  });

  it("lista de objetos → tabla cuyas columnas son la unión de las claves de todas las filas", () => {
    render(<RespuestaProveedor datos={conductorProveedor()} etiqueta={ETIQUETA} />);
    const licencias = screen.getByRole("region", { name: "Licencias (3)" });
    const tabla = within(licencias).getByRole("table", { name: "Licencias" });
    expect(columnas(tabla)).toEqual(["Número de licencia", "Categoría", "Estado de la licencia", "Fecha de vencimiento", "id_licencia", "Restricciones", "motivoSuspension"]);
    expect(within(tabla).getAllByRole("row")).toHaveLength(4);
    expect(tabla).toHaveTextContent("ACTIVA");
    expect(tabla).toHaveTextContent("VENCIDA");
    expect(tabla).toHaveTextContent("SUSPENDIDA");
    expect(tabla).toHaveTextContent("SANCION FICTICIA");
  });

  it("representantes legales y actividades CIIU de la empresa como tablas", () => {
    render(<RespuestaProveedor datos={empresaProveedor()} etiqueta={ETIQUETA} />);
    const rl = within(screen.getByRole("region", { name: "Representantes legales (2)" })).getByRole("table", { name: "Representantes legales" });
    expect(columnas(rl)).toEqual(["Nombre", "Tipo de documento", "Número de documento", "Cargo", "Facultades"]);
    const ciiu = within(screen.getByRole("region", { name: "Actividades económicas (2)" })).getByRole("table", { name: "Actividades económicas" });
    expect(columnas(ciiu)).toEqual(["Código", "Descripción", "Categoría"]);
    expect(ciiu).toHaveTextContent("4511");
  });

  it("lista de valores simples → lista; null y \"\" → «—»; lista vacía → «Sin registros»; objeto vacío → «—»", () => {
    render(<RespuestaProveedor datos={conductorProveedor()} etiqueta={ETIQUETA} />);
    const tabla = screen.getByRole("table", { name: ETIQUETA });
    const lista = within(valorDe(tabla, "Restricciones generales")).getByRole("list");
    expect(within(lista).getAllByRole("listitem").map((li) => li.textContent)).toEqual(["USO DE LENTES", "AUDIFONOS"]);
    expect(valorDe(tabla, "Mensaje").textContent).toBe("—");
    expect(valorDe(tabla, "Observaciones").textContent).toBe("—");
    expect(valorDe(tabla, "Metadatos").textContent).toBe("—");
    expect(valorDe(tabla, "OK").textContent).toBe("Sí");
    expect(valorDe(tabla, "Multas").textContent).toBe("Sin registros");
    const persona = within(screen.getByRole("region", { name: "Persona" })).getByRole("table", { name: "Persona" });
    expect(valorDe(persona, "N.º de paz y salvo").textContent).toBe("—");
    expect(valorDe(persona, "Tiene multas").textContent).toBe("No");
  });

  it("los textos del proveedor salen tal cual (sin traducir valores)", () => {
    render(<RespuestaProveedor datos={{ estado: "VIGENCIA_VIGENTE", nota: "  texto  libre " }} etiqueta={ETIQUETA} />);
    const tabla = screen.getByRole("table", { name: ETIQUETA });
    expect(valorDe(tabla, "Estado").textContent).toBe("VIGENCIA_VIGENTE");
    expect(valorDe(tabla, "nota").textContent).toBe("  texto  libre ");
  });
});

describe("HU #14154 AC3 — etiquetas en español y claves desconocidas con su nombre original", () => {
  it("las conocidas (RUNT persona y RUES) en español; las desconocidas con el nombre tal cual, nunca humanizadas", () => {
    render(<RespuestaProveedor datos={conductorProveedor()} etiqueta={ETIQUETA} />);
    const persona = within(screen.getByRole("region", { name: "Persona" })).getByRole("table", { name: "Persona" });
    expect(rotulos(persona)).toEqual([
      "Tipo de documento",
      "Documento",
      "Nombres",
      "Apellidos",
      "Estado de la persona",
      "Estado del conductor",
      "N.º de paz y salvo",
      "Tiene licencias",
      "Tiene multas",
    ]);
    const tabla = screen.getByRole("table", { name: ETIQUETA });
    expect(rotulos(tabla)).toContain("campo_sin_traducir");
    expect(rotulos(tabla)).not.toContain("Campo sin traducir");
  });

  it("RUES: registro mercantil traducido y el id del registro con su etiqueta", () => {
    render(<RespuestaProveedor datos={empresaProveedor()} etiqueta={ETIQUETA} />);
    const registro = within(screen.getByRole("region", { name: "Registro mercantil" })).getByRole("table", { name: "Registro mercantil" });
    expect(rotulos(registro)).toEqual(["NIT", "Razón social", "Estado de la matrícula", "Número de matrícula", "Cámara de comercio", "Id del registro mercantil"]);
    expect(valorDe(registro, "Razón social").textContent).toBe("EMPRESA FICTICIA S.A.S.");
  });
});

describe("HU #14154 AC7 — expandir/contraer y accesibilidad", () => {
  it("«Expandir todo» abre todas las subsecciones (también las internas) y «Contraer todo» las cierra", () => {
    const { container } = render(<RespuestaProveedor datos={empresaProveedor()} etiqueta={ETIQUETA} />);
    const plegables = () => Array.from(container.querySelectorAll("details"));
    expect(plegables().length).toBeGreaterThanOrEqual(5);
    fireEvent.click(screen.getByRole("button", { name: "Expandir todo" }));
    expect(plegables().every((d) => d.open)).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Contraer todo" }));
    expect(plegables().every((d) => !d.open)).toBe(true);
  });

  it("tablas con encabezados (scope col/row), plegables enfocables con teclado y botón con nombre accesible", () => {
    const { container } = render(<RespuestaProveedor datos={conductorProveedor()} etiqueta={ETIQUETA} />);
    for (const th of container.querySelectorAll("thead th")) expect(th).toHaveAttribute("scope", "col");
    for (const th of container.querySelectorAll("tbody th")) expect(th).toHaveAttribute("scope", "row");
    for (const tabla of screen.getAllByRole("table")) expect(tabla).toHaveAccessibleName();
    for (const s of container.querySelectorAll("summary")) expect(s.className).toMatch(/focus-visible/);
    for (const r of screen.getAllByRole("region")) expect(r).toHaveAccessibleName();
    screen.getAllByRole("button").forEach((b) => expect(b.textContent?.trim()).toBeTruthy());
  });
});
