using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BlindSpotScanner.Api.Auth;

/// <summary>
/// Sign-in (D26): Local mode is one built-in admin; Oidc mode is server-side OpenID Connect with an
/// HttpOnly cookie session (the browser never sees tokens). API and hub requests get 401/403, never
/// a login redirect. Policies are the role names; anything not marked otherwise needs Viewer.
/// </summary>
public static class AuthSetup
{
    public const string CallbackPath = "/api/auth/callback";

    public static WebApplicationBuilder AddBlindSpotScannerAuth(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        builder.Services.AddOptions<AuthOptions>().BindConfiguration("Auth");

        if (options.IsOidc)
        {
            // The redirect URIs come from the public address: behind nginx or a TLS proxy the request's
            // own scheme and host are not what users (and the provider's registration) see.
            var publicUrl = (builder.Configuration["App:PublicUrl"] is { Length: > 0 } url ? url : "http://localhost:8080").TrimEnd('/');

            // The cookie answers every challenge (401/403 for API and hub calls); only /api/auth/login
            // challenges OpenID Connect, explicitly.
            builder.Services.AddAuthentication(o =>
                {
                    o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    o.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddCookie(o =>
                {
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                    o.Cookie.SameSite = SameSiteMode.Lax;
                    o.Events.OnRedirectToLogin = context => Status(context, StatusCodes.Status401Unauthorized);
                    o.Events.OnRedirectToAccessDenied = context => Status(context, StatusCodes.Status403Forbidden);
                })
                .AddOpenIdConnect(o =>
                {
                    o.Authority = options.Authority;
                    o.ClientId = options.ClientId;
                    o.ClientSecret = options.ClientSecret;
                    o.ResponseType = OpenIdConnectResponseType.Code;
                    o.CallbackPath = CallbackPath;
                    o.SignedOutCallbackPath = "/api/auth/signed-out";
                    o.SaveTokens = false;
                    o.RequireHttpsMetadata = options.RequireHttpsMetadata;
                    o.GetClaimsFromUserInfoEndpoint = true;
                    o.MapInboundClaims = false;
                    o.Scope.Add("email");
                    o.TokenValidationParameters.NameClaimType = "name";
                    o.Events.OnRedirectToIdentityProvider = context =>
                    {
                        context.ProtocolMessage.RedirectUri = publicUrl + CallbackPath;
                        return Task.CompletedTask;
                    };
                    o.Events.OnRedirectToIdentityProviderForSignOut = context =>
                    {
                        context.ProtocolMessage.PostLogoutRedirectUri = publicUrl + "/api/auth/signed-out";
                        return Task.CompletedTask;
                    };
                    o.Events.OnTokenValidated = context =>
                    {
                        // One app role from the provider's role claim values (unknown values never elevate).
                        var claimed = context.Principal!.FindAll(options.RoleClaim).Select(c => c.Value);
                        (context.Principal.Identity as ClaimsIdentity)!.AddClaim(new Claim(ClaimTypes.Role, RoleClaims.Effective(claimed, options.DefaultRole)));
                        return Task.CompletedTask;
                    };
                });
        }
        else
        {
            builder.Services.AddAuthentication(LocalAdminHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, LocalAdminHandler>(LocalAdminHandler.SchemeName, _ => { });
        }

        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(Roles.Viewer, p => p.RequireRole(Roles.Viewer, Roles.Member, Roles.Admin));
            o.AddPolicy(Roles.Member, p => p.RequireRole(Roles.Member, Roles.Admin));
            o.AddPolicy(Roles.Admin, p => p.RequireRole(Roles.Admin));
            o.FallbackPolicy = o.GetPolicy(Roles.Viewer);
        });
        return builder;
    }

    /// <summary>API and hub calls get a status code; only other paths are redirected.</summary>
    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
