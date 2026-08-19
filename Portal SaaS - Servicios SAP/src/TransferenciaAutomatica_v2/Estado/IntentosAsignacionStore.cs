using System.Text.Json;

namespace Servicios.TransferenciaAutomatica_v2.Estado;

/// <summary>
/// Contador persistente (un archivo JSON por compañía en disco, junto al servicio) de
/// cuántos ciclos consecutivos un documento viene quedando con asignación parcial --
/// sobrevive a un reinicio del Windows Service, a diferencia de guardar esto en memoria.
/// No es una base de datos: el volumen esperado (documentos con faltante real, no todos
/// los pendientes) es bajo, así que se lee/escribe el archivo completo en cada cambio en
/// vez de mantener un motor de persistencia aparte.
/// </summary>
public sealed class IntentosAsignacionStore
{
    private readonly string _folder;
    private readonly object _lock = new();

    public IntentosAsignacionStore(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    /// <summary>Suma un intento parcial para el documento y devuelve el contador acumulado.</summary>
    public int RegistrarIntentoParcial(string companyCode, string objType, int docEntry)
    {
        lock (_lock)
        {
            var estado = Leer(companyCode);
            var clave = Clave(objType, docEntry);
            var intentos = estado.GetValueOrDefault(clave, 0) + 1;
            estado[clave] = intentos;
            Escribir(companyCode, estado);
            return intentos;
        }
    }

    /// <summary>
    /// Borra el contador de un documento -- se llama cuando el documento queda
    /// completado (cubierto al 100% o se agotaron los intentos), para no dejar basura
    /// acumulándose indefinidamente en el archivo de estado.
    /// </summary>
    public void Limpiar(string companyCode, string objType, int docEntry)
    {
        lock (_lock)
        {
            var estado = Leer(companyCode);
            if (estado.Remove(Clave(objType, docEntry)))
            {
                Escribir(companyCode, estado);
            }
        }
    }

    private static string Clave(string objType, int docEntry) => $"{objType}|{docEntry}";

    private string RutaArchivo(string companyCode) => Path.Combine(_folder, $"intentos-{companyCode}.json");

    private Dictionary<string, int> Leer(string companyCode)
    {
        var ruta = RutaArchivo(companyCode);
        if (!File.Exists(ruta))
        {
            return new Dictionary<string, int>();
        }

        var json = File.ReadAllText(ruta);
        return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
    }

    private void Escribir(string companyCode, Dictionary<string, int> estado)
    {
        var ruta = RutaArchivo(companyCode);
        var json = JsonSerializer.Serialize(estado);
        File.WriteAllText(ruta, json);
    }
}
