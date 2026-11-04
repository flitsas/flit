#:package RabbitMQ.Client@7.0.0
// Observador PASIVO del bus para la prueba local (HU #14069). Ata colas exclusivas y efimeras (tramites.e2e-probe-*) a
// flit.tramites, flit.consultas, flit.notificaciones y flit.plataforma con '#', y escribe una linea por mensaje: hora, exchange, routing key,
// tamano y el cuerpo recortado con correos y numeros largos (cedulas) enmascarados. Usa el usuario 'tramites' de ESTE broker
// local (permisos: lee flit.*, declara tramites.*): no cambia ningun permiso. Cada cola exclusiva recibe su propia copia,
// asi que no le quita mensajes a los consumidores reales. Se detiene POR PID (apagar.ps1).
//   dotnet run scripts/e2e-personanatural/observar-bus.cs -- <archivo-de-salida.log>
using System.Text;
using System.Text.RegularExpressions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var salida = args.Length > 0 ? args[0] : "bus-mensajes.log";
var clave = Environment.GetEnvironmentVariable("PN_E2E_MQ_TRAMITES_CLAVE");
if (string.IsNullOrWhiteSpace(clave))
{
    Console.Error.WriteLine("Falta PN_E2E_MQ_TRAMITES_CLAVE. Copia scripts/e2e-personanatural/.env.example a .env.local.");
    return 1;
}
var fabrica = new ConnectionFactory { HostName = "127.0.0.1", Port = 5672, VirtualHost = "flit", UserName = "tramites", Password = clave };
await using var con = await fabrica.CreateConnectionAsync();
await using var canal = await con.CreateChannelAsync();
var gate = new object();
foreach (var ex in new[] { "flit.tramites", "flit.consultas", "flit.notificaciones", "flit.plataforma" })
{
    var cola = $"tramites.e2e-probe-{ex["flit.".Length..]}";
    await canal.QueueDeclareAsync(cola, durable: false, exclusive: true, autoDelete: true);
    await canal.QueueBindAsync(cola, ex, "#");
    var consumidor = new AsyncEventingBasicConsumer(canal);
    consumidor.ReceivedAsync += (_, ea) =>
    {
        var texto = Encoding.UTF8.GetString(ea.Body.ToArray());
        if (texto.Length > 300) texto = texto[..300];
        texto = Regex.Replace(texto, @"[\w\.\-]+@[\w\.\-]+", "<correo>");
        texto = Regex.Replace(texto, @"\b\d{7,12}\b", "<num>");
        lock (gate) File.AppendAllText(salida, $"{DateTimeOffset.Now:o} {ea.Exchange} rk={ea.RoutingKey} bytes={ea.Body.Length} {texto}{Environment.NewLine}");
        return Task.CompletedTask;
    };
    await canal.BasicConsumeAsync(cola, autoAck: true, consumer: consumidor);
}
File.AppendAllText(salida, $"{DateTimeOffset.Now:o} observador listo{Environment.NewLine}");
await Task.Delay(Timeout.Infinite);
