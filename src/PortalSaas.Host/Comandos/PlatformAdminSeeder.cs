using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Comandos;

/// <summary>
/// Crea la primera cuenta de administrador de plataforma (no hay autoregistro --
/// ver docs/03-MODELO-CORE-COMERCIAL.md §2). Uso:
/// dotnet run --project src/PortalSaas.Host -- seed-admin correo@dominio.cl
/// La contraseña se pide por consola sin eco, nunca como argumento (no queda en
/// historial de shell). Reutilizable si se pierde el acceso -- no sobrescribe una
/// cuenta existente.
/// </summary>
public static class PlatformAdminSeeder
{
    public static async Task RunAsync(IServiceProvider services, string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();

        var yaExiste = await db.PlatformAdmins.AnyAsync(a => a.Email.ToLower() == normalizedEmail);
        if (yaExiste)
        {
            Console.Error.WriteLine($"Ya existe un administrador de plataforma con el correo '{normalizedEmail}'.");
            return;
        }

        Console.Write("Contraseña: ");
        var password = LeerContrasenaSinEco();
        Console.Write("\nConfirmar contraseña: ");
        var confirmacion = LeerContrasenaSinEco();
        Console.WriteLine();

        if (password != confirmacion)
        {
            Console.Error.WriteLine("Las contraseñas no coinciden.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("La contraseña no puede estar vacía.");
            return;
        }

        var (hash, salt) = PasswordHasher.Hash(password);
        db.PlatformAdmins.Add(new PlatformAdmin
        {
            Email = normalizedEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
        });
        await db.SaveChangesAsync();

        Console.WriteLine($"Administrador de plataforma '{normalizedEmail}' creado.");
    }

    private static string LeerContrasenaSinEco()
    {
        var buffer = new System.Text.StringBuilder();
        ConsoleKeyInfo tecla;
        while ((tecla = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (tecla.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }

            if (!char.IsControl(tecla.KeyChar))
            {
                buffer.Append(tecla.KeyChar);
                Console.Write('*');
            }
        }

        return buffer.ToString();
    }
}
