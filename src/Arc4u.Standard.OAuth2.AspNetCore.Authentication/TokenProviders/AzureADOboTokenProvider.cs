using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arc4u.Caching;
using Arc4u.Dependency.Attribute;
using Arc4u.Diagnostics;
using Arc4u.OAuth2.Options;
using Arc4u.OAuth2.Token;
using Arc4u.Security.Principal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;

namespace Arc4u.OAuth2.TokenProviders;

[Export(ProviderName, typeof(ITokenProvider))]
public class AzureADOboTokenProvider : ITokenProvider
{

    public AzureADOboTokenProvider(TokenRefreshInfo tokenRefreshInfo,
                                   ICacheContext cacheContext,
                                   IActivitySourceFactory activitySourceFactory,
                                   IApplicationContext applicationContext,
                                   IOptionsMonitor<OnBehalfOfAuthenticationOptions> onBehalfOfOptions,
                                   ILogger<AzureADOboTokenProvider> logger)
    {
        _onBehalfOfAuthenticationOptions = onBehalfOfOptions.CurrentValue;
        _logger = logger;
        _cacheContext = cacheContext;
        _tokenRefreshInfo = tokenRefreshInfo;
        _activitySource = activitySourceFactory?.Get("Arc4u");
        _applicationContext = applicationContext;
    }

    const string ProviderName = "Obo";

    private readonly ILogger<AzureADOboTokenProvider> _logger;
    private readonly ICacheContext _cacheContext;
    private readonly TokenRefreshInfo _tokenRefreshInfo;
    private readonly ActivitySource? _activitySource;
    private readonly OnBehalfOfAuthenticationOptions _onBehalfOfAuthenticationOptions;
    private readonly IApplicationContext _applicationContext;

    public async Task<TokenInfo> GetTokenAsync(IKeyValueSettings settings, object platformParameters)
    {
        ArgumentNullException.ThrowIfNull(settings);

        using var activity = _activitySource?.StartActivity("Get on behal of token", ActivityKind.Producer);

        var identity = _applicationContext.Principal.Identity as ClaimsIdentity;

        var currentToken = (identity is not null && identity.BootstrapContext is not null) ? identity.BootstrapContext?.ToString() : _tokenRefreshInfo?.AccessToken?.Token;

        if (currentToken is null)
        {
            // this should never happend!
            if (identity is null)
            {
                throw new NullReferenceException(nameof(identity));
            }

            throw new NullReferenceException($"Token cannot be retrieved for the AuthenticationType: {identity.AuthenticationType}");
        }

        var cache = string.IsNullOrEmpty(_cacheContext.Principal?.CacheName) ? _cacheContext.Default : _cacheContext[_cacheContext.Principal!.CacheName];

        // the key is defined by user!
        var cacheKey = $"_Obo_{settings.Values[TokenKeys.ClientIdKey]}_{currentToken.GetHashCode()}_{settings.Values[TokenKeys.Scope]}";

        var tokenFromCache = await cache.GetAsync<TokenInfo>(cacheKey).ConfigureAwait(false);

        // if the token is expired => we need to refresh it! 
        // Not all caches have a TTl defined for a specific key.
        if (tokenFromCache is not null)
        {
            JwtSecurityToken token = new(tokenFromCache.Token);

            // arbitrary 1 minute to have time to perform a request => must be a variable.
            if (token.ValidTo.Subtract(DateTime.UtcNow).TotalSeconds > 60)
            {
                return tokenFromCache;
            }
        }

        // We consider that the access token is still valid.
        // In a Obo from OpenIdConnect this is always the case.
        var pairs = new Dictionary<string, string>()
                                    {
                                            { "grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer" },
                                            { "client_id", settings.Values[TokenKeys.ClientIdKey] },
                                            { "client_secret", settings.Values[TokenKeys.ApplicationKey] },
                                            { "assertion", currentToken },
                                            { "scope", settings.Values[TokenKeys.Scope] },
                                            { "requested_token_use", "on_behalf_of" }
                                    };
        using (var handler = new HttpClientHandler { UseDefaultCredentials = true })
        using (var client = new HttpClient(handler))
        {
            var content = new FormUrlEncodedContent(pairs);

            using var tokenResponse = await client.PostAsync(_onBehalfOfAuthenticationOptions.GetEndpoint(), content, CancellationToken.None).ConfigureAwait(false);
            {
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    if (IdentityModelEventSource.ShowPII)
                    {
                        _logger.Technical().LogError($"Getting the Access token with Obo failed. {tokenResponse.ReasonPhrase}");
                    }
                    else
                    {
                        _logger.Technical().LogError("Getting the Access token with Obo failed. Enable PII to have more info.");
                    }
                }

                // throws an exception is not 200OK.
                tokenResponse.EnsureSuccessStatusCode();

                TokenInfo? oboToken = null;

                if (tokenResponse.IsSuccessStatusCode)
                {
                    using var payload = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false));

                    // Persist the new acess token
                    if (payload.RootElement.TryGetProperty("expires_in", out var property) && property.TryGetInt32(out var seconds))
                    {
                        var expirationAt = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
                        oboToken = new TokenInfo("access_token", payload!.RootElement!.GetString("access_token"), expirationAt.ToUniversalTime());
                    }
                    else
                    {
                        oboToken = new TokenInfo("access_token", payload!.RootElement!.GetString("access_token"));
                    }

                    await cache.PutAsync(cacheKey, oboToken.ExpiresOnUtc - DateTime.UtcNow, oboToken).ConfigureAwait(false);
                }

                if (oboToken is null)
                {
                    _logger.Technical().LogError("No token was in the paylod of the message during the Obo request.");
                    throw new NullReferenceException(nameof(oboToken));
                }

                return oboToken;
            }
        }
    }

    public ValueTask SignOutAsync(IKeyValueSettings settings, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
