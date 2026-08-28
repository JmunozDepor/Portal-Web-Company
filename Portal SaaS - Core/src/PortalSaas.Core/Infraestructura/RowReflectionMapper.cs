using System.Data.Common;
using System.Reflection;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Mapeo reflection-based de una fila de reader a T, compartido entre las dos ramas de
/// HanaService (HanaDataReader y SqlDataReader heredan de DbDataReader, este mapeo solo
/// usa miembros de esa base). Portado de PortalSAP_v2 (MapeadorFilaReflection) tal cual.
/// </summary>
internal static class RowReflectionMapper
{
    /// <summary>
    /// Calcula una sola vez por consulta (no por fila) si T es un tipo simple (toma la
    /// primera columna) o un tipo con propiedades a mapear por nombre de columna -- en
    /// catálogos grandes, recalcular esto en el while de cada fila multiplicaba el costo
    /// de reflection por cada fila devuelta.
    /// </summary>
    public static (Type UnderlyingType, bool IsSimpleType, IReadOnlyDictionary<string, PropertyInfo>? Properties) PrepareType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        var isSimpleType = underlyingType.IsPrimitive || underlyingType == typeof(string) || underlyingType == typeof(decimal)
            || underlyingType == typeof(DateTime) || underlyingType == typeof(Guid);
        var properties = isSimpleType
            ? null
            : type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        return (underlyingType, isSimpleType, properties);
    }

    public static T MapRow<T>(DbDataReader reader, Type underlyingType, bool isSimpleType,
        IReadOnlyDictionary<string, PropertyInfo>? properties)
    {
        if (isSimpleType)
        {
            var value = reader.IsDBNull(0) ? null : reader.GetValue(0);
            return (T)Convert.ChangeType(value ?? default(T)!, underlyingType);
        }

        var instance = Activator.CreateInstance<T>();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            if (properties is null || !properties.TryGetValue(columnName, out var property) || reader.IsDBNull(i))
            {
                continue;
            }

            // Convert.ChangeType no acepta Nullable<T> como tipo destino (siempre tira
            // InvalidCastException) -- convertir contra el tipo subyacente y dejar que
            // SetValue haga el box a Nullable<T>.
            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            property.SetValue(instance, Convert.ChangeType(reader.GetValue(i), targetType));
        }

        return instance;
    }
}
