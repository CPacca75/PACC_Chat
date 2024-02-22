﻿using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Collections.Generic;
using System.Net.Http;
using CopilotChat.WebApi.Options;

namespace CopilotChat.WebApi.Plugins.APIConnector;

/// <summary>
/// This class is a plugin that connects to an API.
/// </summary>
public sealed class ApiConnectorPlugin
{
    private readonly string _bearerToken;
    private readonly ILogger _logger;
    private readonly IHttpClientFactory _clientFactory;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;
    private readonly string _authority;

    //
    // Summary:
    //     Initializes a new instance of the ApiConnectorPlugin to execute the API calls using the OBO Flow.
    //     class.
    //
    // Parameters:
    //   bearerToken:
    //     The bearer token to received by the WebAPI and used to obtain a new access token using the OBO Flow.
    //
    //   clientFactory:
    //     The factory to use to create HttpClient instances.
    //
    //   PlannerOptions.OboOptions:
    //     Configuration for the plugin defined in appsettings.json.
    public ApiConnectorPlugin(string bearerToken, IHttpClientFactory clientFactory, PlannerOptions.OboOptions? onBehalfOfAuth, ILogger logger)
    {
        this._bearerToken = bearerToken ?? throw new ArgumentNullException(bearerToken);
        this._clientFactory = clientFactory;
        this._logger = logger;

        this._clientId = onBehalfOfAuth?.ClientId ?? throw new ArgumentNullException(onBehalfOfAuth?.ClientId);
        this._clientSecret = onBehalfOfAuth?.ClientSecret ?? throw new ArgumentNullException(onBehalfOfAuth?.ClientSecret);
        this._tenantId = onBehalfOfAuth?.TenantId ?? throw new ArgumentNullException(onBehalfOfAuth?.TenantId);
        this._authority = onBehalfOfAuth?.Authority ?? throw new ArgumentNullException(onBehalfOfAuth?.Authority);
    }

    //
    // Summary:
    //     Call a Graph API with the OData query and the Graph API Scopes based on user input.
    //
    // Parameters:
    //   apiURL:
    //     The URL of the GRAPH API with the OData query to call.
    //
    //   graphScopes:
    //     The comma separated value string with the Graph API Scopes needed to execute the
    //     call.
    //
    //   cancellationToken:
    //     The cancellation token.
    //
    // Returns:
    //     The response from the GRAPH API.
    //
    // Exceptions:
    //   T:System.ArgumentNullException:
    //     apiURL is null or empty.
    //
    //   T:System.ArgumentNullException:
    //     graphScopes is null or empty.
    //
    //   T:System.Net.Http.HttpRequestException:
    //     Failed to get token: {response.StatusCode}.
    //
    //   T:System.Net.Http.HttpRequestException:
    //     Failed to get access token.
    //
    //   T:System.Net.Http.HttpRequestException:
    //     Failed to get graph data: {graphResponse.StatusCode}.
    [SKFunction]
    [Description("Call a Graph API with the OData query and the Graph API Scopes based on user input")]
    public async Task<string> CallGraphApiTasksAsync([Description("Url of the GRAPH API with the OData query to call")] string apiURL, [Description("Comma separated value string with the Graph API Scopes needed to execute the call")] string graphScopes, CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrEmpty(apiURL))
        {
            throw new ArgumentNullException(apiURL);
        }

        if (string.IsNullOrEmpty(graphScopes))
        {
            throw new ArgumentNullException(graphScopes);
        }

        string? accessToken = string.Empty;
        using (HttpClient client = this._clientFactory.CreateClient())
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, this._authority + "/" + this._tenantId + "/oauth2/v2.0/token"))
            {
                var keyValues = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new("client_id", this._clientId),
                new("client_secret", this._clientSecret),
                new("assertion", this._bearerToken),
                new("scope", graphScopes),
                new("requested_token_use", "on_behalf_of")
            };

                request.Content = new FormUrlEncodedContent(keyValues);
                var response = await client.SendAsync(request, cancellationToken);
                var responseContent = string.Empty;

                if (response.IsSuccessStatusCode)
                {
                    responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                else
                {
                    throw new HttpRequestException($"Failed to get token: {response.StatusCode}");
                }

                using (JsonDocument doc = JsonDocument.Parse(responseContent))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("access_token", out JsonElement accessTokenElement))
                    {
                        accessToken = accessTokenElement.GetString();
                    }
                    else
                    {
                        throw new HttpRequestException("Failed to get access token");
                    }
                }
            }
        }

        var graphResponseContent = string.Empty;

        using (HttpClient client = this._clientFactory.CreateClient())
        {
            using (var graphRequest = new HttpRequestMessage(HttpMethod.Get, apiURL))
            {
                graphRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                var graphResponse = await client.SendAsync(graphRequest, cancellationToken);

                if (graphResponse.IsSuccessStatusCode)
                {
                    graphResponseContent = await graphResponse.Content.ReadAsStringAsync(cancellationToken);
                }
                else
                {
                    throw new HttpRequestException($"Failed to get graph data: {graphResponse.StatusCode}");
                }
            }
        }
        return graphResponseContent;
    }
}
