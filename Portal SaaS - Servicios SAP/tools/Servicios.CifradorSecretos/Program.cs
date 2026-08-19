using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Servicios.Common.Seguridad;

// Herramienta de línea de comandos para manejar Seguridad:ClaveMaestraSecretos y los
// secretos cifrados de appsettings.json (DbSecreto/ServiceLayerSecreto) sin depender de
// que alguien arme un proyecto descartable cada vez -- usa exactamente el mismo
// SecretoCifradoService que consumen los servicios en producción, así el formato siempre
// coincide.
//
// Uso:
//   dotnet run -- generar-clave
//   dotnet run -- cifrar <claveMaestraBase64> [textoPlano]
//   dotnet run -- descifrar <claveMaestraBase64> [textoCifrado]
//
// Si se omite el texto, lo pide de forma interactiva sin mostrarlo en pantalla (más
// seguro que pasarlo como argumento, que queda en el historial de la consola).

if (args.Length == 0)
{
    MostrarAyuda();
    return 1;
}

switch (args[0].ToLowerInvariant())
{
    case "generar-clave":
        var clave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Console.WriteLine(clave);
        return 0;

    case "cifrar":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Falta la clave maestra. Uso: dotnet run -- cifrar <claveMaestraBase64> [textoPlano]");
            return 1;
        }

        var servicio = CrearServicio(args[1]);
        var textoPlano = args.Length >= 3 ? args[2] : LeerSecretoOculto("Texto a cifrar");
        Console.WriteLine(servicio.Cifrar(textoPlano));
        return 0;
    }

    case "descifrar":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Falta la clave maestra. Uso: dotnet run -- descifrar <claveMaestraBase64> [textoCifrado]");
            return 1;
        }

        var servicio = CrearServicio(args[1]);
        var textoCifrado = args.Length >= 3 ? args[2] : LeerSecretoOculto("Texto cifrado (base64)");
        Console.WriteLine(servicio.Descifrar(textoCifrado));
        return 0;
    }

    default:
        MostrarAyuda();
        return 1;
}

static SecretoCifradoService CrearServicio(string claveMaestraBase64)
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seguridad:ClaveMaestraSecretos"] = claveMaestraBase64
        })
        .Build();

    return new SecretoCifradoService(config);
}

static string LeerSecretoOculto(string etiqueta)
{
    Console.Write($"{etiqueta}: ");
    var valor = "";
    ConsoleKeyInfo tecla;
    while ((tecla = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (tecla.Key == ConsoleKey.Backspace && valor.Length > 0)
        {
            valor = valor[..^1];
        }
        else if (!char.IsControl(tecla.KeyChar))
        {
            valor += tecla.KeyChar;
        }
    }

    Console.WriteLine();
    return valor;
}

static void MostrarAyuda()
{
    Console.WriteLine("""
        Servicios.CifradorSecretos -- maneja claves maestras y secretos cifrados de appsettings.json.

        Uso:
          dotnet run -- generar-clave
              Genera una clave AES-256 nueva (32 bytes, base64) para Seguridad:ClaveMaestraSecretos.

          dotnet run -- cifrar <claveMaestraBase64> [textoPlano]
              Cifra un texto (ej. una contraseña de DB/Service Layer) para pegar en appsettings.json.
              Si se omite textoPlano, lo pide de forma interactiva sin mostrarlo en pantalla.

          dotnet run -- descifrar <claveMaestraBase64> [textoCifrado]
              Descifra un valor de appsettings.json -- útil para verificar que un DbSecreto/
              ServiceLayerSecreto corresponde de verdad a la clave maestra que creés que tiene.
        """);
}
