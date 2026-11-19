import { describe, expect, it } from "vitest";
import { renderHook } from "@testing-library/react";
import { crearUrlPropia } from "@/lib/urlPropia";

describe("rearmado por URL ajena (dock, enlace) y no por eco propio", () => {
  it("su propio eco no rearma; otra URL sí, aunque repita una query anterior", () => {
    const url = crearUrlPropia();
    const { result, rerender } = renderHook(({ qs }) => url.useVersion(qs), { initialProps: { qs: "" } });
    url.registrar("tipo=Aviso1");
    rerender({ qs: "tipo=Aviso1" });
    expect(result.current).toBe(0);
    rerender({ qs: "" }); // el dock lleva a la pantalla sin query
    expect(result.current).toBe(1);
    url.registrar("");
    rerender({ qs: "tipo=Aviso1" }); // un enlace con la query vieja
    expect(result.current).toBe(2);
  });
});
