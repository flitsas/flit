import { describe, expect, it } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { RespuestaFuente } from "@/components/tablero/RespuestaFuente";
import { respuestaFuenteRunt } from "@/lib/tablero/__tests__/datosDetalle";

// Uso de ejemplo: <RespuestaFuente datos={detalle.respuesta_fuente} etiqueta="Respuesta de la fuente" /> pinta SOLO los datos
// propios de la consulta y TODO en español (HU #14087, página del detalle en pestaña nueva): oculta la auditoría del proveedor
// y los campos internos (proveedor, semaforo, desde_cache, consultado_en, respuesta_cruda, http_status, duracion_ms, errores,
// tipo_error, mensaje_error, serialVersionUID, id e id_*, campos de ResultadoConsulta; clave y fuente en los chequeos), aplana
// el `resultado` objeto que envuelve a ResultadoConsulta y oculta el `resultado` enum del RUNT Intempo. Nombres de campo con
// el diccionario de etiquetasRespuesta (desconocidos humanizados); valores: enums traducidos, Sí/No, «—» para vacío/null y
// «Sin registros» para listas vacías; los textos de la fuente, tal cual.

const filaDe = (tabla: HTMLElement, campo: string) => {
  const fila = within(tabla)
    .getAllByRole("row")
    .find((r) => r.querySelector("th[scope='row']")?.textContent === campo);
  if (!fila) throw new Error(`Sin fila ${campo}`);
  return fila;
};
const valorDe = (tabla: HTMLElement, campo: string) => within(filaDe(tabla, campo)).getByRole("cell");
const rotulos = (tabla: HTMLElement) => within(tabla).getAllByRole("rowheader").map((r) => r.textContent);

describe("RespuestaFuente — nombres de campo en español", () => {
  it("objeto → tabla campo/valor con las etiquetas del diccionario y las desconocidas humanizadas", () => {
    render(<RespuestaFuente datos={{ plate: "TST001", vin: "9ZZPRUEBA00000001", document_type: "C", cilidraje: "1600", no_motor: "M-1", campo_nuevo_libre: "x" }} etiqueta="Respuesta de la fuente" />);
    const tabla = screen.getByRole("table", { name: "Respuesta de la fuente" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Campo", "Valor"]);
    for (const c of within(tabla).getAllByRole("columnheader")) expect(c).toHaveAttribute("scope", "col");
    expect(rotulos(tabla)).toEqual(["Placa", "VIN", "Tipo de documento", "Cilindraje", "N.º de motor", "Campo nuevo libre"]);
    expect(valorDe(tabla, "Placa").textContent).toBe("TST001");
    expect(tabla.textContent).not.toMatch(/plate|document_type|cilidraje|no_motor|campo_nuevo_libre/);
  });

  it("subsecciones y columnas de tablas de arrays también traducidas, con el contador de elementos", () => {
    render(
      <RespuestaFuente
        datos={{ owner: [{ document_type: "C", document_number: "****1222", first_name: "PERSONA" }], informacion_general: { marca: "MARCA PRUEBA", modelo: "2020" } }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    const propietarios = screen.getByRole("region", { name: "Propietarios (1)" });
    const tabla = within(propietarios).getByRole("table", { name: "Propietarios" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Tipo de documento", "Número de documento", "Primer nombre"]);
    const general = screen.getByRole("region", { name: "Información general" });
    expect(valorDe(within(general).getByRole("table", { name: "Información general" }), "Marca").textContent).toBe("MARCA PRUEBA");
  });
});

describe("RespuestaFuente — valores internos traducidos; los de la fuente, tal cual", () => {
  it("booleanos Sí/No, null y \"\" como «—», lista vacía «Sin registros», números y textos sin tocar", () => {
    render(
      <RespuestaFuente
        datos={{ inscrito: true, encontrada: false, estado_conductor: null, color: "", licencias: [], valor_sugerido: 45000000, linea: "LINEA  PRUEBA" }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    const tabla = screen.getByRole("table", { name: "Respuesta de la fuente" });
    expect(valorDe(tabla, "Inscrito").textContent).toBe("Sí");
    expect(valorDe(tabla, "Encontrada").textContent).toBe("No");
    expect(valorDe(tabla, "Estado del conductor").textContent).toBe("—");
    expect(valorDe(tabla, "Color").textContent).toBe("—");
    expect(valorDe(tabla, "Licencias").textContent).toBe("Sin registros");
    expect(valorDe(tabla, "Valor sugerido").textContent).toBe("45000000");
    expect(valorDe(tabla, "Línea").textContent).toBe("LINEA  PRUEBA");
    expect(tabla.textContent).not.toMatch(/true|false|null|\[\]/);
  });

  it("enums de chequeo, avalúo y vigencia en español", () => {
    render(
      <RespuestaFuente
        datos={{
          chequeos: [
            { etiqueta: "Chequeo uno", estado: "ESTADO_CHEQUEO_OK" },
            { etiqueta: "Chequeo dos", estado: "ESTADO_CHEQUEO_WARN" },
            { etiqueta: "Chequeo tres", estado: "ESTADO_CHEQUEO_FAIL" },
            { etiqueta: "Chequeo cuatro", estado: "ESTADO_CHEQUEO_UNKNOWN" },
            { etiqueta: "Chequeo cinco", estado: "ESTADO_CHEQUEO_ERROR" },
          ],
          avaluos: [{ fuente: "fasecolda", estado: "ESTADO_AVALUO_SIN_DATOS", valor: 0 }],
        }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    const chequeos = screen.getByRole("table", { name: "Chequeos" });
    expect(within(chequeos).getAllByRole("row").slice(1).map((f) => within(f).getAllByRole("cell")[1].textContent)).toEqual(["OK", "Advertencia", "Falla", "Desconocido", "Error"]);
    const avaluos = screen.getByRole("table", { name: "Avalúos" });
    expect(within(avaluos).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Fuente", "Estado", "Valor"]);
    expect(within(avaluos).getAllByRole("cell").map((c) => c.textContent)).toEqual(["fasecolda", "Sin datos", "0"]);
    expect(document.body.textContent).not.toMatch(/ESTADO_/);
  });
});

describe("RespuestaFuente — sin la auditoría del proveedor ni campos internos", () => {
  it("oculta en cualquier nivel proveedor, semaforo, desde_cache, consultado_en, respuesta_cruda, http_status, duracion_ms, errores, tipo_error, mensaje_error, serialVersionUID, id e id_*", () => {
    render(
      <RespuestaFuente
        datos={{
          proveedor: "PROVEEDOR-FICTICIO",
          semaforo: "SEMAFORO_ROJO",
          desde_cache: true,
          consultado_en: "2026-10-22T15:00:00Z",
          respuesta_cruda: { json: "{}" },
          http_status: 200,
          duracion_ms: 15,
          errores: [{ code: 1, description: "x" }],
          tipo_error: "TIPO_ERROR_FUENTE_UNSPECIFIED",
          mensaje_error: "",
          placa: "TST001",
          vehiculo: {
            serialVersionUID: "1",
            serial_version_uid: "1",
            propietarios_actuales: [{ id: 99, id_tipo_documento: "1", nombre_completo: "PERSONA FICTICIA", direcciones: [{ id: 7, id_departamento: "05", id_ciudad: "001", ciudad: "CIUDAD PRUEBA" }] }],
            garantias_mobiliarias: [{ id_prenda: "P1", id_vehiculo_prenda: "VP1", entidad: "ENTIDAD PRUEBA" }],
          },
        }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    expect(rotulos(screen.getByRole("table", { name: "Respuesta de la fuente" }))).toEqual(["Placa"]);
    const texto = document.body.textContent ?? "";
    expect(texto).not.toMatch(/PROVEEDOR-FICTICIO|Proveedor|Semáforo|caché|Consultado|Estado HTTP|Duración|Errores|serial|99|P1|VP1|05/i);
    expect(within(screen.getByRole("table", { name: "Direcciones" })).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Ciudad"]);
    expect(within(screen.getByRole("table", { name: "Garantías mobiliarias" })).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Entidad"]);
  });

  it("en los chequeos oculta clave y fuente; campos (ResultadoConsulta) no sale", () => {
    render(
      <RespuestaFuente
        datos={{ chequeos: [{ clave: "CLAVE_INTERNA", etiqueta: "Licencia vigente", estado: "ESTADO_CHEQUEO_OK", fuente: "FUENTE_INTERNA", mensaje: "Todo bien" }], campos: [{ clave: "k", valor_texto: "v" }] }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    const chequeos = screen.getByRole("table", { name: "Chequeos" });
    expect(within(chequeos).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Etiqueta", "Estado", "Mensaje"]);
    expect(document.body.textContent).not.toMatch(/CLAVE_INTERNA|FUENTE_INTERNA|valor_texto/);
  });

  it("el `resultado` objeto (ResultadoConsulta) se aplana: su contenido sale directo, sin subsección «resultado»", () => {
    render(
      <RespuestaFuente
        datos={{ resultado: { proveedor: "PROVEEDOR-FICTICIO", desde_cache: false, chequeos: [{ etiqueta: "Matrícula activa", estado: "ESTADO_CHEQUEO_OK" }], certificaciones: { soat: [] } } }}
        etiqueta="Respuesta de la fuente"
      />,
    );
    expect(screen.queryByRole("region", { name: /resultado/i })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Chequeos (1)" })).toHaveTextContent("Matrícula activa");
    expect(screen.getByRole("region", { name: "Certificaciones" })).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/PROVEEDOR-FICTICIO|resultado/i);
  });

  it("el `resultado` enum del RUNT Intempo se oculta", () => {
    render(<RespuestaFuente datos={{ resultado: "RESULTADO_RUNT_INTEMPO_ENCONTRADO", vehiculo: { plate: "TST001" } }} etiqueta="Respuesta de la fuente" />);
    expect(document.body.textContent).not.toMatch(/RESULTADO_RUNT|Encontrado|Resultado/);
    expect(screen.getByRole("region", { name: "Vehículo" })).toHaveTextContent("TST001");
  });

  it("el mapa libre `campos` de la siniestralidad se aplana con sus claves humanizadas", () => {
    render(<RespuestaFuente datos={{ accident_rate: [{ campos: { siniestros: "0", aseguradora: "ASEGURADORA PRUEBA" } }] }} etiqueta="Respuesta de la fuente" />);
    const tabla = screen.getByRole("table", { name: "Siniestralidad" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Siniestros", "Aseguradora"]);
    expect(tabla).toHaveTextContent("ASEGURADORA PRUEBA");
  });
});

describe("RespuestaFuente — respuesta completa grande y profunda (RUNT Intempo ficticio)", () => {
  const pintar = () => render(<RespuestaFuente datos={respuestaFuenteRunt()} etiqueta="Respuesta de la fuente" />);

  it("sin auditoría del proveedor en el primer nivel; vehículo traducido y anidados con contador", () => {
    pintar();
    expect(screen.queryByRole("table", { name: "Respuesta de la fuente" })).not.toBeInTheDocument();
    const vehiculo = screen.getByRole("region", { name: "Vehículo" });
    expect(valorDe(within(vehiculo).getByRole("table", { name: "Vehículo" }), "Placa").textContent).toBe("TST001");
    const general = within(vehiculo).getByRole("region", { name: "Información general" });
    expect(valorDe(within(general).getByRole("table", { name: "Información general" }), "Color").textContent).toBe("—");
    const soat = within(vehiculo).getByRole("region", { name: "SOAT (2)" });
    expect(within(within(soat).getByRole("table", { name: "SOAT" })).getAllByRole("row").slice(1)).toHaveLength(2);
    expect(valorDe(within(vehiculo).getByRole("table", { name: "Vehículo" }), "Revisión técnico-mecánica").textContent).toBe("Sin registros");
    const propietarios = within(vehiculo).getByRole("table", { name: "Propietarios actuales" });
    expect(within(propietarios).getByRole("table", { name: "Direcciones" })).toHaveTextContent("DEPTO PRUEBA");
    expect(within(propietarios).getByText("PERSONA FICTICIA UNO")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/PROVEEDOR-FICTICIO|http_status|duracion_ms|desde_cache/);
  });

  it("plegables: el primer nivel abierto, el segundo cerrado; el título es el summary con nombre accesible", () => {
    const { container } = pintar();
    const vehiculo = screen.getByRole("region", { name: "Vehículo" });
    const plegable = vehiculo.querySelector("details")!;
    expect(plegable).toHaveAttribute("open");
    expect(plegable.querySelector(":scope > summary")).toHaveTextContent("Vehículo");
    const soat = within(vehiculo).getByRole("region", { name: "SOAT (2)" }).querySelector("details")!;
    expect(soat).not.toHaveAttribute("open");
    for (const s of container.querySelectorAll("summary")) expect(s.className).toMatch(/focus-visible:outline/);
  });

  it("«Expandir todo» abre todas las subsecciones y «Contraer todo» las cierra, sin rehacer el árbol", () => {
    const { container } = pintar();
    const plegables = Array.from(container.querySelectorAll("details"));
    expect(plegables.length).toBeGreaterThan(2);
    fireEvent.click(screen.getByRole("button", { name: "Expandir todo" }));
    expect(plegables.every((d) => d.open)).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Contraer todo" }));
    expect(plegables.every((d) => !d.open)).toBe(true);
    expect(Array.from(container.querySelectorAll("details"))).toEqual(plegables);
  });

  it("sin subsecciones no ofrece «Expandir todo»", () => {
    render(<RespuestaFuente datos={{ placa: "TST002", inscrito: true }} etiqueta="Respuesta de la fuente" />);
    expect(screen.queryByRole("button", { name: /expandir todo/i })).not.toBeInTheDocument();
  });

  it("todo oculto: lo dice sin pintar una tabla vacía", () => {
    render(<RespuestaFuente datos={{ proveedor: "X", http_status: 200 }} etiqueta="Respuesta de la fuente" />);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.getByText("Sin registros")).toBeInTheDocument();
  });
});
