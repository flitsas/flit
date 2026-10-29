// Normalizadores de lo que escribe el ciudadano. Puros: no guardan nada, solo limpian el texto del campo.

export const soloDigitos = (valor: string, max = 20): string => valor.replace(/\D/g, "").slice(0, max);

/** Placa en mayúsculas, solo letras y números, hasta 6 caracteres (como la tarjeta de la landing). */
export const normalizarPlaca = (valor: string): string => valor.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6);

/** VIN: alfanumérico en mayúsculas, de 11 a 30 caracteres (el mismo rango que valida el BFF; los de motos y antiguos no tienen 17). */
export const normalizarVin = (valor: string): string => valor.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 30);

/** Radicado: letras, números y guion en mayúsculas (los códigos del portal y los números de FLIT 2). */
export const normalizarRadicado = (valor: string): string => valor.toUpperCase().replace(/[^A-Z0-9-]/g, "").slice(0, 40);

export const CEDULA_MINIMA = 6;
export const cedulaValida = (cedula: string): boolean => cedula.length >= CEDULA_MINIMA;
export const placaValida = (placa: string): boolean => /^[A-Z0-9]{5,6}$/.test(placa);
export const vinValido = (vin: string): boolean => /^[A-Z0-9]{11,30}$/.test(vin);
export const correoValido = (correo: string): boolean => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(correo.trim());
