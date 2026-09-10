namespace Flit.Admin.Application.Companies.PersonalizedDocuments;

/// <summary>
/// Error de validación (422) del alta/confirmación de un documento personalizado. <c>Code</c> es un
/// código estable para el FE (<c>tipo_documento_invalido</c>, <c>sha256_mismatch</c>, <c>no_es_pdf</c>,
/// <c>pdf_cifrado</c>, <c>pdf_ilegible</c>, <c>excede_tamano</c>, <c>excede_paginas</c>, …). El sobre de
/// respuesta es <c>{ errors: [{ field, code, message }] }</c>. Nunca incluye PII ni fragmentos del PDF.
/// </summary>
public sealed record PersonalizedDocumentValidationError(string Field, string Code, string Message);
