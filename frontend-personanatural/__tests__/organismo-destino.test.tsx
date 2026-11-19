import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { OrganismoDestinoField, ubicacionDeOrganismo } from "@/components/tramite/OrganismoDestinoField";
import { PlacaPosterior } from "@/components/tramite/renderers/PlacaPosterior";
import { RequisitosPunto } from "@/components/punto/RequisitosPunto";
import { crearCliente, type DefinicionTipo, type OrganismoDestino, type Solicitud } from "@/lib/api/cliente";
import { destinoDelTipo, faltaDestino } from "@/lib/tramite/declaraciones";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { cuerpo, DIAGNOSTICO_OK, guardado, VEHICULO, wizardAbierto, type Manejador } from "./montar-wizard";
import { json, renderConQuery } from "./utilidades";

// Secretaría de destino (traslado de cuenta) y placa posterior. El API es un doble según el contrato.
const DESTINOS: OrganismoDestino[] = [
  { id: "d-1", nombre: "Tránsito de Envigado", municipio: "Envigado", departamento: "Antioquia" },
  { id: "d-2", nombre: "Movilidad de Cali", municipio: "Cali", departamento: "Valle del Cauca" },
  { id: "d-3", nombre: "Tránsito de Sabaneta", municipio: "", departamento: "Antioquia" },
];
const problema = (status: number, code: string) =>
  new Response(JSON.stringify({ type: "about:blank", title: "t", status, code }), { status, headers: { "content-type": "application/problem+json" } });

function clienteDe(rutas: Record<string, Manejador>) {
  const llamadas: { clave: string; init: RequestInit }[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push({ clave, init });
    const ruta = Object.keys(rutas).find((r) => clave.startsWith(r));
    return ruta ? rutas[ruta]!(init) : json({ code: "no_simulado", message: clave }, 404);
  }) as unknown as typeof fetch;
  return { cliente: crearCliente({ fetchImpl }), llamadas };
}

describe("OrganismoDestinoField", () => {
  it("lista las secretarías con su ubicación y entrega la elegida", async () => {
    const { cliente } = clienteDe({ "GET /organismos-destino": () => json(DESTINOS) });
    const onElegir = vi.fn();
    function Conectado() {
      const [valor, setValor] = useState("");
      return (
        <OrganismoDestinoField
          cliente={cliente}
          valor={valor}
          onElegir={(o) => {
            onElegir(o);
            setValor(o.id);
          }}
        />
      );
    }
    renderConQuery(<Conectado />);

    const combo = screen.getByRole("combobox", { name: "Secretaría de destino" });
    expect(screen.getByText("Secretaría de destino *")).toBeInTheDocument();
    fireEvent.focus(combo);
    expect(await screen.findAllByRole("option")).toHaveLength(3);
    expect(screen.getByText("Envigado, Antioquia")).toBeInTheDocument();
    expect(screen.getByText("Antioquia", { selector: "span" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("option", { name: /Movilidad de Cali/ }));

    expect(onElegir).toHaveBeenCalledWith(DESTINOS[1]);
    expect(combo).toHaveValue("Movilidad de Cali");
  });

  it("busca en el servidor con el texto escrito, sin una petición por tecla", async () => {
    const { cliente, llamadas } = clienteDe({
      "GET /organismos-destino?texto=cali": () => json([DESTINOS[1]]),
      "GET /organismos-destino": () => json(DESTINOS),
    });
    renderConQuery(<OrganismoDestinoField cliente={cliente} valor="" onElegir={vi.fn()} />);
    const combo = screen.getByRole("combobox", { name: "Secretaría de destino" });
    fireEvent.focus(combo);
    await screen.findAllByRole("option");

    fireEvent.change(combo, { target: { value: "c" } });
    fireEvent.change(combo, { target: { value: "ca" } });
    fireEvent.change(combo, { target: { value: "cali" } });
    await waitFor(() => expect(screen.getAllByRole("option")).toHaveLength(1));
    expect(screen.getByRole("option", { name: /Movilidad de Cali/ })).toBeInTheDocument();
    expect(llamadas.filter((l) => l.clave.startsWith("GET /organismos-destino?texto=cali&limite=20"))).toHaveLength(1);
    expect(llamadas.some((l) => l.clave.startsWith("GET /organismos-destino?texto=ca&"))).toBe(false);
  });

  it("sin coincidencias lo dice; con error del servidor avisa con role=alert y reintenta", async () => {
    let falla = true;
    const { cliente } = clienteDe({
      "GET /organismos-destino?texto=zzz": () => json([]),
      "GET /organismos-destino": () => (falla ? problema(503, "servicio_no_disponible") : json(DESTINOS)),
    });
    renderConQuery(<OrganismoDestinoField cliente={cliente} valor="" onElegir={vi.fn()} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/servicio no está disponible/i);
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    fireEvent.focus(screen.getByRole("combobox", { name: "Secretaría de destino" }));
    expect(await screen.findAllByRole("option")).toHaveLength(3);

    fireEvent.change(screen.getByRole("combobox", { name: "Secretaría de destino" }), { target: { value: "zzz" } });
    expect(await screen.findByText("No hay secretarías que coincidan.")).toBeInTheDocument();
  });

  it("muestra el error recibido en aria-invalid y deja ver el nombre ya elegido", () => {
    const { cliente } = clienteDe({ "GET /organismos-destino": () => json(DESTINOS) });
    renderConQuery(<OrganismoDestinoField cliente={cliente} valor="d-1" nombreElegido="Tránsito de Envigado" error="Este campo es obligatorio" onElegir={vi.fn()} />);
    const combo = screen.getByRole("combobox", { name: "Secretaría de destino" });
    expect(combo).toHaveValue("Tránsito de Envigado");
    expect(combo).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent("Este campo es obligatorio");
  });

  it("la ubicación omite lo que falta", () => {
    expect(ubicacionDeOrganismo(DESTINOS[0]!)).toBe("Envigado, Antioquia");
    expect(ubicacionDeOrganismo(DESTINOS[2]!)).toBe("Antioquia");
    expect(ubicacionDeOrganismo({})).toBe("");
  });
});

describe("destino del tipo (lógica)", () => {
  const conFlag = (extra: Partial<DefinicionTipo["flags"]> = {}) => definicionDe([["vehicle_query"]], { flags: { ...definicionDe([["x"]]).flags, ...extra } });

  it("el trámite lleva destino por el flag o por su declaración; usa la clave del back o la de siempre", () => {
    expect(destinoDelTipo(conFlag())).toBeNull();
    expect(destinoDelTipo(conFlag({ requiereDestino: true }))).toEqual({ clave: "destino", fieldKey: "transit_office_destino_id", obligatoria: true });
    const decl = { clave: "dest", control: "organismo_destino", etiqueta: "Destino", obligatoria: false, opciones: [], fieldKey: "destino_id", fieldKeysExtra: [], docCodeSoporte: "", soporteObligatorio: false, catalogo: "" };
    expect(destinoDelTipo(definicionDe([["vehicle_query"]], { declaraciones: [decl] }))).toEqual({ clave: "dest", fieldKey: "destino_id", obligatoria: false });
  });

  it("falta el destino solo si es obligatorio y el borrador no lo tiene", () => {
    const d = conFlag({ requiereDestino: true });
    expect(faltaDestino(d, {})).toBe(true);
    expect(faltaDestino(d, { transit_office_destino_id: "  " })).toBe(true);
    expect(faltaDestino(d, { transit_office_destino_id: "d-1" })).toBe(false);
    expect(faltaDestino(conFlag(), {})).toBe(false);
  });
});

describe("diagnóstico del portal con secretaría de destino", () => {
  const definicion = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], { flags: { ...definicionDe([["x"]]).flags, requiereDestino: true } });
  const RUTA_DEFINICION = "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1";

  it("no consulta el vehículo hasta elegir el destino; al elegirlo lo guarda y consulta", async () => {
    let conDestino = false;
    let consultado = false;
    const { llamadas } = await wizardAbierto({
      [RUTA_DEFINICION]: () => json(definicion),
      "GET /organismos-destino?texto=&limite=20": () => json(DESTINOS),
      "PUT /solicitudes/s-1/campos": () => {
        conDestino = true;
        return json(solicitudDe({ version: 4, fieldValues: { transit_office_destino_id: "d-2" } }));
      },
      "POST /solicitudes/s-1/consultas/vehiculo": () => json(VEHICULO),
      "POST /solicitudes/s-1/diagnostico": () => {
        consultado = true;
        return json(DIAGNOSTICO_OK);
      },
      "GET /solicitudes/s-1": () => json({ ...(consultado ? guardado(DIAGNOSTICO_OK) : solicitudDe()), fieldValues: conDestino ? { transit_office_destino_id: "d-2" } : {} }),
    });
    const tarjeta = await screen.findByRole("region", { name: "Secretaría de destino" });
    expect(screen.getByRole("button", { name: "Continuar" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Continuar" })).toHaveAccessibleDescription("Elige la secretaría de destino para consultar el vehículo.");
    expect(llamadas.some((l) => l.clave === "POST /solicitudes/s-1/consultas/vehiculo")).toBe(false);

    const combo = within(tarjeta).getByRole("combobox", { name: "Secretaría de destino" });
    fireEvent.focus(combo);
    fireEvent.click(await screen.findByRole("option", { name: /Movilidad de Cali/ }));

    await waitFor(() => expect(llamadas.some((l) => l.clave === "PUT /solicitudes/s-1/campos")).toBe(true));
    expect(cuerpo(llamadas, "PUT /solicitudes/s-1/campos").valores).toEqual({ transit_office_destino_id: "d-2" });
    await waitFor(() => expect(llamadas.some((l) => l.clave === "POST /solicitudes/s-1/consultas/vehiculo")).toBe(true));
    await screen.findByText(/Consultado en RUNT/);
    await waitFor(() => expect(screen.getByRole("button", { name: "Continuar" })).toBeEnabled());
  });

  it("si guardar el destino falla, lo dice junto al campo y no consulta", async () => {
    const { llamadas } = await wizardAbierto({
      [RUTA_DEFINICION]: () => json(definicion),
      "GET /organismos-destino?texto=&limite=20": () => json(DESTINOS),
      "PUT /solicitudes/s-1/campos": () => problema(500, "error_interno"),
      "GET /solicitudes/s-1": () => json(solicitudDe()),
    });
    const tarjeta = await screen.findByRole("region", { name: "Secretaría de destino" });
    fireEvent.focus(within(tarjeta).getByRole("combobox", { name: "Secretaría de destino" }));
    fireEvent.click(await screen.findByRole("option", { name: /Envigado/ }));

    expect(await within(tarjeta).findByRole("alert")).toBeInTheDocument();
    expect(within(tarjeta).getByRole("combobox", { name: "Secretaría de destino" })).toHaveAttribute("aria-invalid", "true");
    expect(llamadas.some((l) => l.clave === "POST /solicitudes/s-1/consultas/vehiculo")).toBe(false);
  });

  it("un tipo sin destino no pinta la tarjeta", async () => {
    await wizardAbierto({ "GET /solicitudes/s-1": () => json(solicitudDe()) });
    expect(screen.queryByRole("region", { name: "Secretaría de destino" })).toBeNull();
  });
});

describe("punto: el destino se elige en Requisitos (no hay diagnóstico propio)", () => {
  it("pinta la tarjeta de destino y avisa si falta", async () => {
    const definicion: DefinicionTipo = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]], {
      code: "TRASLADO",
      flags: { ...definicionDe([["x"]]).flags, requiereDestino: true },
    });
    const { cliente } = clienteDe({
      "GET /punto/requisitos?tipoCodigo=TRASLADO": () => json({ tipoCodigo: "TRASLADO", organismoId: "o-1", huella: "h", version: 1, grupos: [] }),
      "GET /punto/requisitos/valor-sugerido": () => json({ fuente: "fasecolda" }),
      "GET /tipos-tramite/TRASLADO/definicion": () => json(definicion),
      "GET /solicitudes/s-1": () => json(solicitudDe({ version: 4 }) as Solicitud),
      "GET /organismos-destino": () => json(DESTINOS),
    });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <RequisitosPunto cliente={cliente} solicitudId="s-1" tipoCodigo="TRASLADO" placa="ABC123" onAtras={() => undefined} onContinuarPago={() => undefined} onAviso={() => undefined} />
      </QueryClientProvider>,
    );
    const tarjeta = await screen.findByRole("region", { name: "Secretaría de destino" });
    expect(within(tarjeta).getByRole("combobox", { name: "Secretaría de destino" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
    expect(await within(tarjeta).findByText("Este campo es obligatorio")).toBeInTheDocument();
  });
});

describe("PlacaPosterior (plate_request)", () => {
  it("informa que la placa la asigna el organismo y no bloquea el avance", () => {
    const onEstado = vi.fn();
    renderConQuery(<PlacaPosterior seccion={{ clave: "p1:plate_request", sectionType: "plate_request", paso: null }} activa intentoAvance={0} onEstado={onEstado} />);
    expect(screen.getByRole("heading", { level: 3, name: "Placa del vehículo" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(/la placa la asigna el organismo de tránsito después de la entrega/i);
    expect(onEstado).toHaveBeenCalledWith({ completo: true });
    expect(screen.queryByText(/aún no está disponible/i)).toBeNull();
  });

  it("usa el título del paso de la definición cuando lo trae", () => {
    renderConQuery(<PlacaPosterior seccion={{ clave: "p1:plate_request", sectionType: "plate_request", paso: { clave: "p1", titulo: "Placa", sectionTypes: ["plate_request"], campos: [] } }} activa intentoAvance={0} onEstado={vi.fn()} />);
    expect(screen.getByRole("heading", { level: 3, name: "Placa" })).toBeInTheDocument();
  });
});
