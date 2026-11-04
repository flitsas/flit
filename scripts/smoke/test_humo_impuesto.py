"""Prueba sin red de humo-impuesto.py (C3-11).

Levanta un servidor HTTP en 127.0.0.1, corre el script con `python -I` y verifica que la salida
solo trae clase, duración y status: ningún monto, texto, URL, placa, documento ni nombre.

Uso: python -I -m unittest scripts/smoke/test_humo_impuesto.py
"""
import http.server
import json
import os
import re
import subprocess
import sys
import threading
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "humo-impuesto.py")

PLACA = "TST901"
DOCUMENTO = "99999000901"
NOMBRE = "PERSONAVALOR IMPUESTO"
MONTO = "615000"
LINEA = re.compile(
    r"^(clase: (DEUDA|DEUDA_SIN_VALOR|SIN_DEUDA|NO_CONFIRMADO|CUERPO_ILEGIBLE|FALLO_\w+)"
    r"|duracion: \d+ ms|http: \d{3})$"
)


class _Servidor(http.server.BaseHTTPRequestHandler):
    cuerpo = b"{}"
    status = 200
    pedidos = []

    def do_POST(self):  # noqa: N802
        largo = int(self.headers.get("Content-Length") or 0)
        type(self).pedidos.append((
            self.path,
            self.rfile.read(largo),
            self.headers.get("Authorization"),
            self.headers.get("Content-Type"),
        ))
        cuerpo = type(self).cuerpo
        self.send_response(type(self).status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(cuerpo)))
        self.end_headers()
        self.wfile.write(cuerpo)

    def log_message(self, *args):
        pass


class HumoImpuestoTests(unittest.TestCase):
    def setUp(self):
        _Servidor.pedidos = []
        _Servidor.status = 200
        self.servidor = http.server.HTTPServer(("127.0.0.1", 0), _Servidor)
        self.base = f"http://127.0.0.1:{self.servidor.server_port}"
        threading.Thread(target=self.servidor.serve_forever, daemon=True).start()

    def tearDown(self):
        self.servidor.shutdown()
        self.servidor.server_close()

    def correr(self, cuerpo, *args):
        _Servidor.cuerpo = json.dumps(cuerpo).encode("utf-8") if not isinstance(cuerpo, bytes) else cuerpo
        entorno = {k: v for k, v in os.environ.items() if not k.startswith("IMPUESTO_")}
        entorno["IMPUESTO_API_BASE_URL"] = self.base
        entorno["IMPUESTO_API_PATH"] = "/impuesto"
        r = subprocess.run(
            [sys.executable, "-I", SCRIPT, "--placa", PLACA, "--documento", DOCUMENTO, *args],
            capture_output=True, text=True, env=entorno, timeout=30,
        )
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stderr, "")
        return r.stdout

    def sin_valores(self, salida):
        for valor in (PLACA, DOCUMENTO, NOMBRE, MONTO, self.base, "127.0.0.1", str(self.servidor.server_port), "descriptionErrorDevelopment"):
            self.assertNotIn(valor, salida)
        for linea in salida.splitlines():
            self.assertRegex(linea, LINEA)

    def test_DeudaImprimeClaseSinMonto(self):
        salida = self.correr(
            {"pendingDebtTaxes": True, "pendingTotalToPay": MONTO, "observationError": {"typeError": 0},
             "descriptionErrorDevelopment": "NO-IMPRIMIR", "OwnerName": NOMBRE},
            "--propietario", NOMBRE, "--departamento", "05",
        )
        self.assertIn("clase: DEUDA", salida)
        self.assertRegex(salida, r"duracion: \d+ ms")
        self.assertIn("http: 200", salida)
        self.sin_valores(salida)
        self.assertEqual(len(_Servidor.pedidos), 1)
        ruta, cuerpo, autorizacion, tipo = _Servidor.pedidos[0]
        self.assertEqual(ruta, "/impuesto")
        self.assertIsNone(autorizacion)
        self.assertEqual(tipo, "application/json")
        pedido = json.loads(cuerpo)
        self.assertEqual(pedido["processStep"], 3)
        self.assertNotIn("credentials", pedido)
        self.assertEqual(pedido["plateNumber"], PLACA)
        self.assertEqual(pedido["detail"]["OwnerName"], NOMBRE)

    def test_DeudaSinValor(self):
        salida = self.correr(
            {"pendingDebtTaxes": True, "pendingTotalToPay": "0", "observationError": {"typeError": 0}},
            "--departamento", "05",
        )
        self.assertIn("clase: DEUDA_SIN_VALOR", salida)
        self.sin_valores(salida)

    def test_SinDeuda(self):
        salida = self.correr(
            {"pendingDebtTaxes": False, "observationError": {"typeError": "0"}},
            "--departamento", "76",
        )
        self.assertIn("clase: SIN_DEUDA", salida)
        self.sin_valores(salida)

    def test_TypeErrorDistintoDeCeroNoConfirma(self):
        salida = self.correr(
            {"pendingDebtTaxes": False, "pendingTotalToPay": MONTO, "observationError": {"typeError": 1}},
            "--departamento", "05",
        )
        self.assertIn("clase: NO_CONFIRMADO", salida)
        self.sin_valores(salida)

    def test_CuerpoVacioNoConfirma(self):
        salida = self.correr({}, "--departamento", "05")
        self.assertIn("clase: NO_CONFIRMADO", salida)
        self.sin_valores(salida)

    def test_SinUrlSaleSinImprimirla(self):
        entorno = {k: v for k, v in os.environ.items() if not k.startswith("IMPUESTO_")}
        r = subprocess.run(
            [sys.executable, "-I", SCRIPT, "--placa", PLACA, "--documento", DOCUMENTO, "--departamento", "05"],
            capture_output=True, text=True, env=entorno, timeout=30,
        )
        self.assertEqual(r.returncode, 2)
        self.assertNotIn(PLACA, r.stdout)
        self.assertNotIn(DOCUMENTO, r.stdout)
        self.assertNotIn("http", r.stdout.lower())


if __name__ == "__main__":
    unittest.main()
