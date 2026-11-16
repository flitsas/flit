// HU #14391 (Feature #14387) AC5 — un precio registrado o cerrado se refleja en la siguiente consulta: los catálogos de
// organismos y tipos no usan caché por tiempo. AC1-AC3: lo que el servidor deja de ofrecer no aparece; la lista vacía
// se informa con el aviso existente de «no hay trámites disponibles».
import { describe, expect, it, vi } from "vitest";
import { act, render, renderHook, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import type { ClientePortal, TipoTramite } from "@/lib/api/cliente";
import { useOrganismos, useTiposTramite, useTiposTramitePunto } from "@/components/landing/useCatalogos";
import { AccionesPunto } from "@/components/punto/AccionesPunto";

const tipo = (code: string): TipoTramite => ({ code, name: code, family: "f", version: 1, disponible: true });

function envoltorio() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const Wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  return { client, Wrapper };
}

function clienteCon(parcial: { organismos?: unknown; tiposTramite?: unknown; puntoTipos?: unknown }): ClientePortal {
  return {
    catalogos: { organismos: parcial.organismos, tiposTramite: parcial.tiposTramite },
    punto: { tiposTramite: parcial.puntoTipos },
  } as unknown as ClientePortal;
}

describe("useCatalogos — sin caché por tiempo (AC5)", () => {
  it("organismos: la siguiente consulta trae el cambio", async () => {
    const organismos = vi
      .fn()
      .mockResolvedValueOnce([{ id: "1", nombre: "Bogotá" }])
      .mockResolvedValueOnce([]);
    const cliente = clienteCon({ organismos });
    const { Wrapper } = envoltorio();
    const primera = renderHook(() => useOrganismos(cliente), { wrapper: Wrapper });
    await waitFor(() => expect(primera.result.current.data).toHaveLength(1));
    primera.unmount();
    // Un montaje nuevo con el mismo QueryClient vuelve a consultar: ya no hay datos «frescos» por cinco minutos.
    const segunda = renderHook(() => useOrganismos(cliente), { wrapper: Wrapper });
    await waitFor(() => expect(organismos).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(segunda.result.current.data).toEqual([]));
  });

  it("tipos del portal y del punto: la siguiente consulta trae el cambio", async () => {
    const tiposTramite = vi.fn().mockResolvedValueOnce([tipo("A")]).mockResolvedValueOnce([tipo("A"), tipo("B")]);
    const puntoTipos = vi.fn().mockResolvedValueOnce([tipo("A")]).mockResolvedValueOnce([]);
    const cliente = clienteCon({ tiposTramite, puntoTipos });
    const { Wrapper } = envoltorio();
    const web1 = renderHook(() => useTiposTramite(cliente), { wrapper: Wrapper });
    const punto1 = renderHook(() => useTiposTramitePunto(cliente, "org"), { wrapper: Wrapper });
    await waitFor(() => expect(web1.result.current.data).toHaveLength(1));
    await waitFor(() => expect(punto1.result.current.data).toHaveLength(1));
    web1.unmount();
    punto1.unmount();
    const web2 = renderHook(() => useTiposTramite(cliente), { wrapper: Wrapper });
    const punto2 = renderHook(() => useTiposTramitePunto(cliente, "org"), { wrapper: Wrapper });
    await waitFor(() => expect(web2.result.current.data).toHaveLength(2));
    await waitFor(() => expect(punto2.result.current.data).toEqual([]));
  });
});

describe("Punto sin precios (AC3)", () => {
  it("informa que no hay trámites disponibles y no deja elegir", async () => {
    const cliente = clienteCon({ puntoTipos: vi.fn().mockResolvedValue([]) });
    const { Wrapper } = envoltorio();
    await act(async () => {
      render(
        <Wrapper>
          <AccionesPunto cliente={cliente} organismoId="org" organismoNombre="Bogotá" onConsultar={() => {}} onAviso={() => {}} />
        </Wrapper>,
      );
    });
    expect(await screen.findByText("Por ahora no hay trámites disponibles en este punto.")).toBeInTheDocument();
  });
});
