using AspNet.Security.OAuth.BattleNet;
using AspNet.Security.OpenId.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dhadgar.Identity.OAuth;

public static class OAuthProviderRegistry
{
    public const string Steam = "steam";
    public const string BattleNet = "battlenet";
    public const string Epic = "epic";
    public const string Xbox = "xbox";

    private static readonly HashSet<string> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        Steam,
        BattleNet,
        Epic,
        Xbox
    };

    public static bool IsSupported(string provider) => Providers.Contains(provider);

    public static void ConfigureProviders(
        AuthenticationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment,
        OAuthSecretProvider secrets,
        string signInScheme)
    {
        ConfigureSteam(builder, configuration, environment, secrets, signInScheme);
        ConfigureBattleNet(builder, configuration, environment, secrets, signInScheme);
        ConfigureEpic(builder, configuration, environment, secrets, signInScheme);
        ConfigureXbox(builder, configuration, environment, secrets, signInScheme);
    }

    public static void ConfigureMockProviders(AuthenticationBuilder builder, string signInScheme)
    {
        foreach (var provider in Providers)
        {
            builder.AddOAuth<OAuthOptions, MockOAuthHandler>(provider, $"Mock {provider}", options =>
            {
                options.SignInScheme = signInScheme;
                options.CallbackPath = $"/oauth/{provider}/callback";
                options.AuthorizationEndpoint = $"https://example.test/{provider}/authorize";
                options.TokenEndpoint = $"https://example.test/{provider}/token";
                options.UserInformationEndpoint = $"https://example.test/{provider}/userinfo";
                options.ClientId = "mock";
                options.ClientSecret = "mock";
                options.SaveTokens = false;
                OAuthLinkingHandler.Configure(options, provider);
            });
        }
    }

    private static void ConfigureSteam(
        AuthenticationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment,
        OAuthSecretProvider secrets,
        string signInScheme)
    {
        // Key Vault first, then configuration fallback
        var applicationKey = secrets.SteamApiKey
            ?? GetSetting(configuration, "OAuth:Steam:ApplicationKey", environment, Steam);

        builder.AddSteam(Steam, options =>
        {
            options.SignInScheme = signInScheme;
            options.CallbackPath = "/oauth/steam/callback";

            if (!string.IsNullOrWhiteSpace(applicationKey))
            {
                options.ApplicationKey = applicationKey;
            }

            OAuthLinkingHandler.ConfigureOpenId(options, Steam);
        });
    }

    private static void ConfigureBattleNet(
        AuthenticationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment,
        OAuthSecretProvider secrets,
        string signInScheme)
    {
        // Key Vault first, then configuration fallback
        var clientId = secrets.BattleNetClientId
            ?? GetSetting(configuration, "OAuth:BattleNet:ClientId", environment, BattleNet);
        var clientSecret = secrets.BattleNetClientSecret
            ?? GetSetting(configuration, "OAuth:BattleNet:ClientSecret", environment, BattleNet);
        var region = configuration["OAuth:BattleNet:Region"];

        builder.AddBattleNet(BattleNet, options =>
        {
            options.SignInScheme = signInScheme;
            options.CallbackPath = "/oauth/battlenet/callback";
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;

            if (TryParseBattleNetRegion(region, out var parsedRegion))
            {
                options.Region = parsedRegion;
            }

            OAuthLinkingHandler.Configure(options, BattleNet);
        });
    }

    private static void ConfigureEpic(
        AuthenticationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment,
        OAuthSecretProvider secrets,
        string signInScheme)
    {
        // Key Vault first, then configuration fallback
        var clientId = secrets.EpicClientId
            ?? GetSetting(configuration, "OAuth:Epic:ClientId", environment, Epic);
        var clientSecret = secrets.EpicClientSecret
            ?? GetSetting(configuration, "OAuth:Epic:ClientSecret", environment, Epic);
        // Endpoints come from config only (not secrets)
        var authorizationEndpoint = GetSetting(configuration, "OAuth:Epic:AuthorizationEndpoint", environment, Epic);
        var tokenEndpoint = GetSetting(configuration, "OAuth:Epic:TokenEndpoint", environment, Epic);
        var userInfoEndpoint = GetSetting(configuration, "OAuth:Epic:UserInformationEndpoint", environment, Epic);

        builder.AddEpicGames(Epic, options =>
        {
            options.SignInScheme = signInScheme;
            options.CallbackPath = "/oauth/epic/callback";
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.AuthorizationEndpoint = authorizationEndpoint;
            options.TokenEndpoint = tokenEndpoint;
            options.UserInformationEndpoint = userInfoEndpoint;
            options.SaveTokens = false;
            OAuthLinkingHandler.Configure(options, Epic);
        });
    }

    private static void ConfigureXbox(
        AuthenticationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment,
        OAuthSecretProvider secrets,
        string signInScheme)
    {
        // Key Vault first, then configuration fallback
        var clientId = secrets.XboxClientId
            ?? GetSetting(configuration, "OAuth:Xbox:ClientId", environment, Xbox);
        var clientSecret = secrets.XboxClientSecret
            ?? GetSetting(configuration, "OAuth:Xbox:ClientSecret", environment, Xbox);

        builder.AddMicrosoftAccount(Xbox, options =>
        {
            options.SignInScheme = signInScheme;
            options.CallbackPath = "/oauth/xbox/callback";
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.Scope.Add("xboxlive.signin");
            OAuthLinkingHandler.Configure(options, Xbox);
        });
    }

    private static string GetSetting(
        IConfiguration configuration,
        string key,
        IHostEnvironment environment,
        string provider)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value) && environment.IsProduction())
        {
            throw new InvalidOperationException($"OAuth configuration '{key}' is required for {provider} in production.");
        }

        return value ?? string.Empty;
    }

    private static bool TryParseBattleNetRegion(string? region, out BattleNetAuthenticationRegion parsed)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            parsed = BattleNetAuthenticationRegion.Unified;
            return true;
        }

        if (Enum.TryParse(region, true, out parsed))
        {
            return true;
        }

        parsed = BattleNetAuthenticationRegion.Unified;
        return false;
    }
}
