"""Humo del servicio de impuesto (C3-11). Hace UN POST y escribe solo la clase y el tiempo.

Nunca imprime el cuerpo, la URL, la placa, el documento, el nombre ni el mensaje de una excepción.

Variables (las mismas de core-consultas; no tienen valor en el repo):
  IMPUESTO_API_BASE_URL         obligatoria (o --base-url)
  IMPUESTO_API_PATH             obligatoria (o --path)
  IMPUESTO_API_TIMEOUT_SECONDS  defecto 300

Uso:
  python -I scripts/smoke/humo-impuesto.py --placa TST001 --documento 9999900001 --departamento 05

La salida no va a ADO, logs ni commits. Placa y documento entran solo por argumento.
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request


def entero(texto, defecto):
    try:
        n = int(texto)
    except (TypeError, ValueError):
        return defecto
    return n if n > 0 else defecto


def clase_de(datos):
    """Clase gruesa. No devuelve montos ni textos del proveedor."""
    if not isinstance(datos, dict) or not datos:
        return "NO_CONFIRMADO"
    observacion = datos.get("observationError")
    tipo = None
    if isinstance(observacion, dict):
        bruto = observacion.get("typeError")
        if isinstance(bruto, bool):
            tipo = None
        elif isinstance(bruto, int):
            tipo = bruto
        elif isinstance(bruto, str) and bruto.isdigit():
            tipo = int(bruto)
    if tipo != 0:
        return "NO_CONFIRMADO"
    deuda = datos.get("pendingDebtTaxes")
    if deuda is True:
        total = datos.get("pendingTotalToPay")
        if total in (None, "", "0", 0):
            return "DEUDA_SIN_VALOR"
        return "DEUDA"
    if deuda is False:
        return "SIN_DEUDA"
    return "NO_CONFIRMADO"


def main():
    args = argparse.ArgumentParser(description="Humo de impuesto: solo clase y tiempo.")
    args.add_argument("--placa", required=True)
    args.add_argument("--documento", required=True)
    args.add_argument("--tipo-documento", default="CC")
    args.add_argument("--departamento", required=True)
    args.add_argument("--departamento-nombre", default="")
    args.add_argument("--organismo", default="")
    args.add_argument("--municipio", default="")
    args.add_argument("--modelo", default="")
    args.add_argument("--propietario", default="")
    args.add_argument("--base-url", default=None)
    args.add_argument("--path", default=None)
    a = args.parse_args()

    base = (a.base_url or os.environ.get("IMPUESTO_API_BASE_URL") or "").strip().rstrip("/")
    ruta = (a.path or os.environ.get("IMPUESTO_API_PATH") or "").strip()
    if not base.startswith(("http://", "https://")) or not ruta.startswith("/"):
        print("faltan IMPUESTO_API_BASE_URL y IMPUESTO_API_PATH (URL absoluta y ruta que empieza por /)")
        return 2
    timeout = entero(os.environ.get("IMPUESTO_API_TIMEOUT_SECONDS"), 300)
    cuerpo = {
        "documentType": a.tipo_documento,
        "documentNumber": a.documento,
        "plateNumber": a.placa,
        "processStep": 3,
        "detail": {
            "secretaryDepartmentCode": a.departamento,
            "secretaryDepartmentDescription": a.departamento_nombre,
            "secretaryName": a.organismo,
            "secretaryCity": a.municipio,
            "vehicleModel": a.modelo,
            "OwnerName": a.propietario,
        },
    }
    datos = json.dumps(cuerpo).encode("utf-8")
    pedido = urllib.request.Request(
        base + ruta,
        data=datos,
        method="POST",
        headers={"Content-Type": "application/json"},
    )
    inicio = time.monotonic()
    try:
        with urllib.request.urlopen(pedido, timeout=timeout) as respuesta:
            status, crudo = respuesta.status, respuesta.read()
    except urllib.error.HTTPError as error:
        status, crudo = error.code, error.read()
    except Exception as error:  # noqa: BLE001 — solo el tipo: el mensaje puede traer la URL
        print(f"clase: FALLO_{type(error).__name__}")
        print(f"duracion: {int((time.monotonic() - inicio) * 1000)} ms")
        return 1
    duracion = int((time.monotonic() - inicio) * 1000)
    try:
        leido = json.loads(crudo.decode("utf-8-sig"))
        clase = clase_de(leido)
    except (ValueError, UnicodeDecodeError):
        clase = "CUERPO_ILEGIBLE"
    print(f"clase: {clase}")
    print(f"duracion: {duracion} ms")
    print(f"http: {status}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
