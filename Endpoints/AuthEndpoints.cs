using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace CeroLicenseManager.Endpoints;

public static class AuthEndpoints
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/csrf", async (HttpContext http, IAntiforgery antiforgery,
            UserManager<IdentityUser> users) =>
        {
            var token = antiforgery.GetAndStoreTokens(http).RequestToken;
            return Results.Ok(new
            {
                csrfToken = token,
                setupRequired = !await users.Users.AnyAsync()
            });
        }).AllowAnonymous();

        app.MapPost("/api/auth/setup", async (PasswordRequest request, HttpContext http,
            UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
        {
            if (http.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            await SetupLock.WaitAsync(http.RequestAborted);
            try
            {
                if (await users.Users.AnyAsync(http.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status409Conflict);
                var user = new IdentityUser("gaston") { LockoutEnabled = true };
                var result = await users.CreateAsync(user, request.Password ?? "");
                if (!result.Succeeded)
                    return Results.BadRequest(new
                    {
                        error = "La contraseña debe tener al menos 14 caracteres, mayúscula, minúscula, número y símbolo."
                    });
                await signIn.SignInAsync(user, isPersistent: false);
                return Results.Ok(new { user = "gaston" });
            }
            finally
            {
                SetupLock.Release();
            }
        }).AllowAnonymous().RequireRateLimiting("login");

        app.MapPost("/api/auth/login", async (PasswordRequest request,
            SignInManager<IdentityUser> signIn) =>
        {
            var result = await signIn.PasswordSignInAsync("gaston", request.Password ?? "",
                isPersistent: false, lockoutOnFailure: true);
            return result.Succeeded
                ? Results.Ok(new { user = "gaston" })
                : Results.Json(new { error = "Credenciales inválidas." },
                    statusCode: StatusCodes.Status401Unauthorized);
        }).AllowAnonymous().RequireRateLimiting("login");

        app.MapPost("/api/auth/logout", async (SignInManager<IdentityUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok(new { signedOut = true });
        });
        app.MapGet("/api/auth/me", (HttpContext http) => Results.Ok(new
        {
            user = http.User.Identity?.Name
        }));
        return app;
    }

    private sealed record PasswordRequest(string? Password);
}
