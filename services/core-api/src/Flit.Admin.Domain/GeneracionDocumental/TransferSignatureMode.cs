namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// <c>{{modo_firma}}</c> del anexo §9.0. <b>En el alcance vigente el único valor admitido es
/// <see cref="Manuscrita"/></b> (adenda 15.4 del diseño, decisión del PO): líneas de firma en
/// blanco con nombre y documento debajo, SIN leyenda de firma electrónica, SIN sello del baúl de
/// firmas y SIN sello de validación de identidad, en ningún bloque y en ningún escenario, aunque la
/// persona tenga firma custodiada vigente.
///
/// <para>Por eso el generador de transferencia no recibe ningún puerto de baúl de firmas: no es que
/// consulte y decida no estampar — es que no tiene con qué consultar. El modo <c>ESTAMPADA</c> está
/// DIFERIDO (anexo §9.4) y exige ADR previo sobre la evidencia de consentimiento.</para>
///
/// <para><b>Regla estructural que no depende de este valor</b> (anexo §9.0.3): el ESCENARIO decide
/// cuántos bloques de firma existen; el modo de firma solo decide qué va dentro de un bloque que ya
/// existe.</para>
/// </summary>
public static class TransferSignatureMode
{
    public const string Manuscrita = "MANUSCRITA";
}
