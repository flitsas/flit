import { BarraProgreso } from "@flit/ui/BarraProgreso";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, CONTENEDOR_TABLA_CLS, FILA_TABLA_CLS } from "@/components/comun/estilos";
import { Marca } from "@/components/liquidaciones/ChipEstado";
import { conTope } from "@/lib/errores";
import { ERRORES_LIQUIDACIONES, nombreMotivoFila } from "@/lib/liquidaciones/estados";
import { formatoCifra } from "@/lib/liquidaciones/formato";
import type { AnalisisCarga } from "@/lib/liquidaciones/tipos";

/**
 * Paso 2 de la carga masiva (spec-visual §3.4): conteos (válidas, duplicadas que se liquidan una vez, inválidas), la barra
 * de las válidas frente al tope del lote y la tabla de filas inválidas con su motivo rotulado. Con `superaTope`, el
 * literal LIMITE_SUPERADO del contrato con el tope (F3-CF2), en `idAvisoTope` para el aria-describedby del CTA.
 */
export function ResumenAnalisis({ analisis, idAvisoTope }: { analisis: AnalisisCarga; idAvisoTope: string }) {
  const { validas, duplicadas, invalidas, tope, superaTope } = analisis;
  return (
    <div className="flex flex-col gap-4">
      <ul aria-label="Resultado del análisis" className="flex flex-wrap gap-2">
        <li>
          <Marca tono="success">{`Válidas: ${formatoCifra(validas)}`}</Marca>
        </li>
        <li>
          <Marca tono="neutral">{`Duplicadas (se liquidan una vez): ${formatoCifra(duplicadas)}`}</Marca>
        </li>
        <li>
          <Marca tono="danger">{`Inválidas: ${formatoCifra(invalidas.length)}`}</Marca>
        </li>
      </ul>
      <div className="flex flex-col gap-1">
        <BarraProgreso etiqueta="Placas válidas frente al máximo del lote" valor={validas} maximo={tope} textoValor={`${validas} de ${tope}`} />
        <p className="text-xs text-muted-foreground">{`${formatoCifra(validas)} de ${formatoCifra(tope)} placas por lote`}</p>
      </div>
      {superaTope ? (
        <InlineAlert tone="warning" title="Divide el archivo para continuar" id={idAvisoTope}>
          {conTope(ERRORES_LIQUIDACIONES.LIMITE_SUPERADO, tope)}
        </InlineAlert>
      ) : null}
      {invalidas.length > 0 ? (
        <div className={CONTENEDOR_TABLA_CLS}>
          <table aria-label="Filas inválidas" className="w-full border-collapse text-sm">
            <thead>
              <tr>
                {["Fila", "Valor", "Motivo"].map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {invalidas.map((f) => (
                <tr key={`${f.fila}-${f.valor}`} className={`${FILA_TABLA_CLS} bg-card`}>
                  <td className={`${CELDA_TABLA_CLS} tabular-nums`}>{f.fila}</td>
                  <td className={`${CELDA_TABLA_CLS} font-mono`}>{f.valor}</td>
                  <td className={CELDA_TABLA_CLS}>{nombreMotivoFila(f.motivo)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </div>
  );
}
