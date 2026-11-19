// Sustituto vacío de `server-only` para Vitest (alias en vitest.config.ts): el paquete real lanza al importarse fuera
// del entorno react-server, y las pruebas importan lib/auth.server.ts y lib/config.server.ts directamente.
export {};
