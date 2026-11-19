#!/usr/bin/env python3
"""Tiempos por clase de prueba a partir de archivos .trx, para repartir carriles por tiempo.

    python3 .github/scripts/ci-tiempos-por-clase.py <archivo.trx> [...] > .github/ci/tiempos-admin.tsv

Para cada clase calcula el «tramo» que ocupa: desde que empieza su primera prueba hasta que termina
la última. xUnit corre las clases en paralelo, así que sumar la duración de las pruebas daría tiempo
de CPU y no de reloj. El tramo es lo que cuesta la clase en el shard.

Si llegan varias corridas (varios .trx de la misma clase en corridas distintas), se toma la MEDIANA
por clase para que una corrida lenta no deforme el reparto. Los .trx de una misma corrida que se
partió en shards se juntan por carpeta: cada carpeta padre distinta del primer nivel cuenta como una
corrida (p. ej. <run_id>/admin-0/... y <run_id>/admin-1/...).

Salida: TSV «clase<TAB>segundos», ordenado por segundos de mayor a menor. El nombre de clase se
deriva igual que en ci-core-api-pruebas.sh: se quitan los argumentos de la Theory y el método.
"""
import re
import statistics
import sys
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def instante(texto: str) -> float:
    # 2026-11-18T19:40:23.5139011+00:00 -> se recorta a microsegundos para fromisoformat.
    texto = re.sub(r"(\.\d{6})\d*", r"\1", texto)
    return datetime.fromisoformat(texto).timestamp()


def clase_de(nombre: str) -> str:
    nombre = nombre.split("(", 1)[0]
    return nombre.rsplit(".", 1)[0]


def tramos(trx: Path) -> dict[str, float]:
    inicio: dict[str, float] = {}
    fin: dict[str, float] = {}
    for r in ET.parse(trx).getroot().iter(f"{NS}UnitTestResult"):
        if "startTime" not in r.attrib or "endTime" not in r.attrib:
            continue
        c = clase_de(r.attrib["testName"])
        a, b = instante(r.attrib["startTime"]), instante(r.attrib["endTime"])
        inicio[c] = min(inicio.get(c, a), a)
        fin[c] = max(fin.get(c, b), b)
    return {c: fin[c] - inicio[c] for c in inicio}


def main(rutas: list[str]) -> None:
    if not rutas:
        sys.exit(__doc__)
    por_corrida: dict[str, dict[str, float]] = {}
    for ruta in map(Path, rutas):
        corrida = ruta.parts[0] if len(ruta.parts) > 1 else str(ruta)
        for c, s in tramos(ruta).items():
            d = por_corrida.setdefault(corrida, {})
            d[c] = max(d.get(c, 0.0), s)
    valores: dict[str, list[float]] = {}
    for d in por_corrida.values():
        for c, s in d.items():
            valores.setdefault(c, []).append(s)
    filas = sorted(((statistics.median(v), c) for c, v in valores.items()), reverse=True)
    for s, c in filas:
        print(f"{c}\t{s:.1f}")


if __name__ == "__main__":
    main(sys.argv[1:])
