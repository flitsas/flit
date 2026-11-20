"""Prueba sin red de humo-intempo.py (HU #13876, F10-H4).

Levanta un servidor HTTP en 127.0.0.1 que responde JSON inventado, corre el script con `python -I` contra él y
verifica que la salida solo trae status, duración, bytes y líneas `clave: tipo`: ningún valor del JSON, ni la URL,
ni la placa o el VIN consultados.

Uso de ejemplo: python -I -m unittest scripts/smoke/test_humo_intempo.py
"""
import http.server
import json
import os
import re
import subprocess
import sys
import threading
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "humo-intempo.py")

OBJETO = {
    "plate": "TSTVALOR1",
    "informacionGeneral": {"marca": "MARCAVALOR2", "cilidraje": "1600", "modelo": 2031},
    "propietariosActuales": [
        {"nombreCompleto": "PERSONAVALOR3 PRUEBA", "noDocumento": "99999000777", "direcciones": [{"direccion": "CALLEVALOR4"}]},
        {"nombreCompleto": "PERSONAVALOR5", "tipoPropiedad": 1},
    ],
    "esBlindado": False,
    "polizas": None,
}
ARREGLO = [{"code": 98765, "description": "DESCRIPCIONVALOR6"}, {"code": 98766, "description": "DESCRIPCIONVALOR7"}]
VALORES = ["TSTVALOR1", "MARCAVALOR2", "1600", "2031", "PERSONAVALOR3", "99999000777", "CALLEVALOR4", "PERSONAVALOR5",
           "98765", "98766", "DESCRIPCIONVALOR6", "DESCRIPCIONVALOR7", "False", "false"]
PLACA = "TST901"
VIN = "9ZZPRUEBA00000901"
LINEA = re.compile(r"^(status HTTP: \d{3}|duracion: \d+ ms|bytes: \d+|raiz: \w+|\s*[\w\[\]]+: \w+)$")


class _Servidor(http.server.BaseHTTPRequestHandler):
    cuerpo = b"{}"
    pedidos = []

    def do_POST(self):  # noqa: N802 (nombre del protocolo)
        largo = int(self.headers.get("Content-Length") or 0)
        type(self).pedidos.append((self.path, self.rfile.read(largo), self.headers.get("Content-Type")))
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(self.cuerpo)))
        self.end_headers()
        self.wfile.write(self.cuerpo)

    def log_message(self, *args):
        pass


class HumoIntempoTests(unittest.TestCase):
    def setUp(self):
        _Servidor.pedidos = []
        self.servidor = http.server.HTTPServer(("127.0.0.1", 0), _Servidor)
        self.base = f"http://127.0.0.1:{self.servidor.server_port}"
        threading.Thread(target=self.servidor.serve_forever, daemon=True).start()

    def tearDown(self):
        self.servidor.shutdown()
        self.servidor.server_close()

    def correr(self, cuerpo, *args):
        _Servidor.cuerpo = json.dumps(cuerpo).encode("utf-8")
        entorno = {k: v for k, v in os.environ.items() if not k.startswith("INTEMPO_")}
        entorno["INTEMPO_API_BASE_URL"] = self.base
        r = subprocess.run([sys.executable, "-I", SCRIPT, *args], capture_output=True, text=True, env=entorno, timeout=30)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stderr, "")
        return r.stdout

    def sin_valores(self, salida, consultado):
        for valor in [*VALORES, consultado, self.base, "127.0.0.1", str(self.servidor.server_port)]:
            self.assertNotIn(valor, salida)
        for linea in salida.splitlines():
            self.assertRegex(linea, LINEA)

    def test_ImprimeStatusDuracionYArbolSinValores(self):
        salida = self.correr(OBJETO, PLACA)
        self.assertIn("status HTTP: 200", salida)
        self.assertRegex(salida, r"duracion: \d+ ms")
        self.assertIn(f"bytes: {len(json.dumps(OBJETO).encode('utf-8'))}", salida)
        for linea in ["plate: str", "informacionGeneral: dict", "  marca: str", "  modelo: int", "propietariosActuales: list",
                      "  nombreCompleto: str", "  direcciones: list", "    direccion: str", "  tipoPropiedad: int",
                      "esBlindado: bool", "polizas: NoneType"]:
            self.assertIn(linea, salida.splitlines())
        self.sin_valores(salida, PLACA)
        ruta, cuerpo, tipo = _Servidor.pedidos[0]
        self.assertEqual(len(_Servidor.pedidos), 1, "una sola consulta")
        self.assertEqual(ruta, f"/api/RuntConsulta/consultar-vehiculo?Plate={PLACA}")
        self.assertEqual((cuerpo, tipo), (b"", "application/json"))

    def test_RaizArregloImprimeSoloClaves(self):
        salida = self.correr(ARREGLO, PLACA)
        self.assertIn("raiz: list", salida)
        lineas = salida.splitlines()
        self.assertIn("code: int", lineas)
        self.assertIn("description: str", lineas)
        self.assertEqual(lineas.count("code: int"), 1, "las claves de los elementos, sin repetir")
        self.sin_valores(salida, PLACA)

    def test_ConVinUsaRutaVinYQueryVin(self):
        salida = self.correr(OBJETO, VIN, "--vin")
        self.assertIn("status HTTP: 200", salida)
        self.sin_valores(salida, VIN)
        self.assertEqual([p[0] for p in _Servidor.pedidos], [f"/api/RuntConsulta/consultar-vin?Vin={VIN}"])


if __name__ == "__main__":
    unittest.main()
