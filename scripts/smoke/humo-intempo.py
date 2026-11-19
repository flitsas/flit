"""Prueba de humo de Intempo (HU #13876, F10-H4): hace UNA consulta real y muestra solo el status HTTP, la duración,
los bytes y la estructura de la respuesta (nombres de clave y tipo). Nunca imprime valores, la URL ni mensajes de
excepción: la respuesta trae datos personales y el endpoint no tiene autenticación.

Configuración (las mismas variables que core-consultas; la URL base no tiene defecto y no se versiona):
  INTEMPO_API_BASE_URL            obligatoria (o --base-url)
  INTEMPO_API_VEHICLE_PLATE_PATH  defecto /api/RuntConsulta/consultar-vehiculo
  INTEMPO_API_VEHICLE_VIN_PATH    defecto /api/RuntConsulta/consultar-vin
  INTEMPO_API_SECONDS_TIMEOUT     defecto 20

Uso:
  python -I scripts/smoke/humo-intempo.py <placa>
  python -I scripts/smoke/humo-intempo.py <vin> --vin

La salida no va a ADO, logs ni commits. La placa o el VIN entran solo por argumento.
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

RUTA_PLACA = "/api/RuntConsulta/consultar-vehiculo"
RUTA_VIN = "/api/RuntConsulta/consultar-vin"
TIMEOUT = 20


def recorrer(nodo, sangria=""):
    """Imprime `clave: tipo` por cada clave; en un arreglo, las claves de sus elementos (sin repetir)."""
    if isinstance(nodo, dict):
        for clave, valor in nodo.items():
            print(f"{sangria}{clave}: {type(valor).__name__}")
            recorrer(valor, sangria + "  ")
    elif isinstance(nodo, list):
        vistas = set()
        for elemento in nodo:
            if isinstance(elemento, dict):
                nuevas = [k for k in elemento if k not in vistas]
                if nuevas:
                    recorrer({k: elemento[k] for k in nuevas}, sangria)
                    vistas.update(nuevas)


def entero(texto, defecto):
    try:
        n = int(texto)
    except (TypeError, ValueError):
        return defecto
    return n if n > 0 else defecto


def main():
    args = argparse.ArgumentParser(description="Humo de Intempo: solo status, duración, bytes y claves/tipos.")
    args.add_argument("valor", help="placa (o VIN con --vin)")
    args.add_argument("--vin", action="store_true", help="consulta por VIN (ruta VIN, query Vin)")
    args.add_argument("--base-url", default=None, help="URL base; si no, INTEMPO_API_BASE_URL")
    a = args.parse_args()

    base = (a.base_url or os.environ.get("INTEMPO_API_BASE_URL") or "").strip().rstrip("/")
    if not base.startswith(("http://", "https://")):
        print("falta INTEMPO_API_BASE_URL (o --base-url) con una URL http/https absoluta")
        return 2
    if a.vin:
        ruta, parametro = os.environ.get("INTEMPO_API_VEHICLE_VIN_PATH") or RUTA_VIN, "Vin"
    else:
        ruta, parametro = os.environ.get("INTEMPO_API_VEHICLE_PLATE_PATH") or RUTA_PLACA, "Plate"
    timeout = entero(os.environ.get("INTEMPO_API_SECONDS_TIMEOUT"), TIMEOUT)

    url = f"{base}{ruta}?{urllib.parse.urlencode({parametro: a.valor})}"
    peticion = urllib.request.Request(url, data=b"", method="POST", headers={"Content-Type": "application/json"})
    inicio = time.monotonic()
    try:
        with urllib.request.urlopen(peticion, timeout=timeout) as r:
            status, cuerpo = r.status, r.read()
    except urllib.error.HTTPError as e:
        status, cuerpo = e.code, e.read()
    except Exception as e:  # noqa: BLE001 — red, timeout, TLS: solo el tipo, sin mensaje (puede llevar la URL)
        print(f"fallo: {type(e).__name__} tras {int((time.monotonic() - inicio) * 1000)} ms")
        return 1
    print(f"status HTTP: {status}")
    print(f"duracion: {int((time.monotonic() - inicio) * 1000)} ms")
    print(f"bytes: {len(cuerpo)}")
    try:
        datos = json.loads(cuerpo.decode("utf-8-sig"))
    except (ValueError, UnicodeDecodeError):
        print("cuerpo: no es JSON")
        return 0
    print(f"raiz: {type(datos).__name__}")
    recorrer(datos)
    return 0


if __name__ == "__main__":
    sys.exit(main())
