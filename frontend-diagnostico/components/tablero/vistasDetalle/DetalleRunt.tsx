import type { ResultadoRuntDetalle } from "@/lib/tablero/detalle";
import { Dato, ListaDatos, TARJETA_DETALLE_CLS } from "./comun";

const plural = (n: number, uno: string, varios: string) => `${n} ${n === 1 ? uno : varios}`;

/**
 * RUNT del vehículo (HU #13801, F06-C13): placa, VIN, servicio, clase, organismo, propietarios (solo tipo de documento y de
 * propiedad, nunca nombres ni números) y cantidad de locatarios.
 */
export function DetalleRunt({ resultado }: { resultado: ResultadoRuntDetalle }) {
  return (
    <div className={TARJETA_DETALLE_CLS}>
      <ListaDatos>
        <Dato rotulo="Placa">
          <span className="font-mono uppercase tracking-wider">{resultado.placa ?? "—"}</span>
        </Dato>
        <Dato rotulo="VIN">
          <span className="font-mono">{resultado.vin ?? "—"}</span>
        </Dato>
        <Dato rotulo="Servicio">{resultado.servicio ?? "—"}</Dato>
        <Dato rotulo="Clase">{resultado.clase ?? "—"}</Dato>
        <Dato rotulo="Organismo de tránsito">{resultado.organismo ?? "—"}</Dato>
        <Dato rotulo="Locatarios">{resultado.locatarios > 0 ? plural(resultado.locatarios, "locatario", "locatarios") : "Sin locatarios"}</Dato>
        <Dato rotulo="Propietarios">
          <span>{plural(resultado.propietarios.length, "propietario", "propietarios")}</span>
          {resultado.propietarios.length > 0 && (
            <ul className="mt-1 flex flex-col gap-0.5 text-xs font-normal">
              {resultado.propietarios.map((p, i) => (
                <li key={i}>{`Documento ${p.tipo_documento ?? "—"} · Propiedad ${p.tipo_propiedad ?? "—"}`}</li>
              ))}
            </ul>
          )}
        </Dato>
      </ListaDatos>
    </div>
  );
}
