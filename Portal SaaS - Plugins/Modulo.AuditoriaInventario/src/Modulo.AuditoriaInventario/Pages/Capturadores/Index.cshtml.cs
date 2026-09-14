using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Capturadores;

/// <summary>
/// Mantenedor de CaptureUser -- catálogo simple con un campo write-only
/// (Password): dejarlo en blanco al editar no cambia la contraseña actual, mismo
/// criterio que el resto de la plataforma para secretos.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    [BindProperty]
    public CapturadorInput Input { get; set; } = new();

    public IReadOnlyList<CaptureUser> Capturadores { get; private set; } = Array.Empty<CaptureUser>();
    public long? Editando { get; set; }

    public async Task OnGetAsync(long? editando, CancellationToken ct)
    {
        await CargarListaAsync(ct);

        if (editando is { } id)
        {
            var capturador = await _db.CaptureUsers.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct);
            if (capturador is not null)
            {
                Editando = id;
                Input = new CapturadorInput { Username = capturador.Username, FullName = capturador.FullName, IsActive = capturador.IsActive };
            }
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync(long? editando, CancellationToken ct)
    {
        if (editando is null && string.IsNullOrWhiteSpace(Input.Password))
        {
            ModelState.AddModelError("Input.Password", "La contraseña es obligatoria para un capturador nuevo.");
        }

        if (!ModelState.IsValid)
        {
            Editando = editando;
            await CargarListaAsync(ct);
            return Page();
        }

        try
        {
            if (editando is { } id)
            {
                var capturador = await _db.CaptureUsers.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct)
                    ?? throw new InvalidOperationException("El capturador no existe.");

                if (await _db.CaptureUsers.AnyAsync(c => c.CompanyId == _currentCompany.CompanyId && c.Username == Input.Username && c.Id != id, ct))
                {
                    throw new InvalidOperationException($"Ya existe un capturador con el usuario '{Input.Username}'.");
                }

                capturador.Username = Input.Username;
                capturador.FullName = string.IsNullOrWhiteSpace(Input.FullName) ? null : Input.FullName;
                capturador.IsActive = Input.IsActive;

                if (!string.IsNullOrWhiteSpace(Input.Password))
                {
                    var (hash, salt) = PasswordHasher.Hash(Input.Password);
                    capturador.PasswordHash = hash;
                    capturador.PasswordSalt = salt;
                }

                SuccessMessage = "Capturador actualizado.";
            }
            else
            {
                if (await _db.CaptureUsers.AnyAsync(c => c.CompanyId == _currentCompany.CompanyId && c.Username == Input.Username, ct))
                {
                    throw new InvalidOperationException($"Ya existe un capturador con el usuario '{Input.Username}'.");
                }

                var (hash, salt) = PasswordHasher.Hash(Input.Password!);
                var capturador = new CaptureUser
                {
                    CompanyId = _currentCompany.CompanyId,
                    Username = Input.Username,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    FullName = string.IsNullOrWhiteSpace(Input.FullName) ? null : Input.FullName,
                    IsActive = Input.IsActive,
                };
                _db.CaptureUsers.Add(capturador);
                SuccessMessage = "Capturador creado.";
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            Editando = editando;
            await CargarListaAsync(ct);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            var capturador = await _db.CaptureUsers.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct)
                ?? throw new InvalidOperationException("El capturador no existe.");
            _db.CaptureUsers.Remove(capturador);
            await _db.SaveChangesAsync(ct);
            SuccessMessage = "Capturador eliminado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    private async Task CargarListaAsync(CancellationToken ct)
    {
        Capturadores = await _db.CaptureUsers
            .Where(c => c.CompanyId == _currentCompany.CompanyId)
            .OrderBy(c => c.Username)
            .ToListAsync(ct);
    }

    public sealed class CapturadorInput
    {
        [Required(ErrorMessage = "El usuario es obligatorio.")]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? FullName { get; set; }

        public string? Password { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
