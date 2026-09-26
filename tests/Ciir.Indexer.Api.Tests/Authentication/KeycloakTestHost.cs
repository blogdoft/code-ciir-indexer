using Ciir.Indexer.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Ciir.Indexer.Api.Tests.Authentication;

/// <summary>
/// A minimal in-memory host wired the way <c>Program.cs</c> wires Keycloak authentication, with the
/// realm's OpenID Connect configuration replaced by a fixed symmetric signing key so no discovery
/// document has to be fetched from a real Keycloak.
/// </summary>
internal static class KeycloakTestHost
{
    public const string Issuer = "https://keycloak.test/realms/test";

    public const string OpenEndpoint = "/health";

    // An endpoint with no [Authorize]/AllowAnonymous of its own: only the fallback policy protects it.
    public const string ProtectedEndpoint = "/api/anything";

    private static readonly SymmetricSecurityKey SigningKey = NewKey('a');

    public static SymmetricSecurityKey NewKey(char fill) => new(Encoding.UTF8.GetBytes(new string(fill, 64)));

    public static async Task<IHost> StartAsync(
        KeycloakOptions? keycloak,
        Action<IServiceCollection>? configureServices = null,
        Action<IEndpointRouteBuilder>? mapEndpoints = null)
    {
        var builder = new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAuthorization();
                if (keycloak is not null)
                {
                    services.AddKeycloakAuthentication(keycloak);

                    // Configure (not PostConfigure): the handler's own post-configuration builds its
                    // configuration manager from Authority unless Configuration is already set.
                    services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, bearer =>
                        bearer.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } });
                }

                configureServices?.Invoke(services);
            })
            .Configure(app =>
            {
                app.UseRouting();
                if (keycloak is not null)
                {
                    app.UseAuthentication();
                }

                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet(OpenEndpoint, () => "healthy").AllowAnonymous();
                    endpoints.MapGet(ProtectedEndpoint, () => "ok");
                    mapEndpoints?.Invoke(endpoints);
                });
            }));

        return await builder.StartAsync();
    }

    public static string CreateToken(
        string issuer = Issuer,
        string? audience = null,
        TimeSpan? lifetime = null,
        SecurityKey? signingKey = null)
    {
        var now = DateTime.UtcNow;
        var expiresIn = lifetime ?? TimeSpan.FromMinutes(5);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = now - TimeSpan.FromHours(3),
            Expires = now + expiresIn,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    public static HttpRequestMessage Get(string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }
}
