﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.AI;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Models.Response;

namespace CopilotChat.WebApi.Services;

public record AnalysisResult(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("riskLevel")] short RiskLevel
);

public record ImageContent([property: JsonPropertyName("content")] string Content);

public record ImageAnalysisRequest(
    [property: JsonPropertyName("image")] ImageContent Image,
    [property: JsonPropertyName("categories")] List<string> Categories
);

public sealed class AzureContentModerator : IDisposable
{
    private const string HttpUserAgent = "Copilot Chat";

    private readonly Uri _endpoint;
    private readonly HttpClient _httpClient;
    private readonly HttpClientHandler? _httpClientHandler;

    /// <summary>
    /// Options for the content moderator.
    /// </summary>
    private readonly ContentModeratorOptions? _contentModeratorOptions;

    /// <summary>
    /// Gets the options for the content moderator.
    /// </summary>
    public ContentModeratorOptions? ContentModeratorOptions => this._contentModeratorOptions;

    private static readonly List<string> s_categories = new()
    {
        "HateSpeech",
        "Sexual",
        "SelfHarm",
        "Violence"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureContentModerator"/> class.
    /// </summary>
    /// <param name="endpoint">Endpoint for service API call.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="contentModeratorOptions">Content moderator options from appsettings.</param>
    /// <param name="httpClientHandler">Instance of <see cref="HttpClientHandler"/> to setup specific scenarios.</param>
    public AzureContentModerator(Uri endpoint, string apiKey, ContentModeratorOptions contentModeratorOptions, HttpClientHandler httpClientHandler)
    {
        this._endpoint = endpoint;
        this._contentModeratorOptions = contentModeratorOptions;
        this._httpClient = new(httpClientHandler);

        this._httpClient.DefaultRequestHeaders.Add("User-Agent", HttpUserAgent);

        // Subscription Key header required to authenticate requests to Azure API Management (APIM) service
        this._httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureContentModerator"/> class.
    /// </summary>
    /// <param name="endpoint">Endpoint for service API call.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="contentModeratorOptions">Content moderator options from appsettings.</param>
    public AzureContentModerator(Uri endpoint, string apiKey, ContentModeratorOptions contentModeratorOptions)
    {
        this._endpoint = endpoint;
        this._contentModeratorOptions = contentModeratorOptions;

        this._httpClientHandler = new() { CheckCertificateRevocationList = true };
        this._httpClient = new(this._httpClientHandler);

        this._httpClient.DefaultRequestHeaders.Add("User-Agent", HttpUserAgent);

        // Subscription Key header required to authenticate requests to Azure API Management (APIM) service
        this._httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
    }

    /// <summary>
    /// Parse the analysis result and return the violated categories.
    /// </summary>
    /// <param name="analysisResult">The content analysis result.</param>
    /// <param name="threshold">The violation threshold.</param>
    /// <returns>The list of violated category names. Will return an empty list if there is no violation.</returns>
    public static List<string> ParseViolatedCategories(Dictionary<string, AnalysisResult> analysisResult, short threshold)
    {
        var violatedCategories = new List<string>();

        foreach (var category in analysisResult.Values)
        {
            if (category.RiskLevel > threshold)
            {
                violatedCategories.Add(category.Category);
            }
        }
        return violatedCategories;
    }

    /// <summary>
    /// Invokes a sync API to perform harmful content analysis on image.
    /// <param name="base64Image">Base64 envoding content of image</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// </summary>
    /// <returns>SKContext containing the image analysis result.</returns>
    public async Task<ImageAnalysisResponse> ImageAnalysisAsync(string base64Image, CancellationToken cancellationToken)
    {
        var image = base64Image.Replace("data:image/png;base64,", "", StringComparison.InvariantCultureIgnoreCase).Replace("data:image/jpeg;base64,", "", StringComparison.InvariantCultureIgnoreCase);
        ImageContent content = new(image);
        ImageAnalysisRequest requestBody = new(content, s_categories);

        using var httpRequestMessage = new HttpRequestMessage()
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{this._endpoint}/contentsafety/image:analyze?api-version=2023-04-30-preview"),
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };

        var response = await this._httpClient.SendAsync(httpRequestMessage, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || body is null)
        {
            throw new AIException(
                AIException.ErrorCodes.UnknownError,
                $"Content moderator: Failed analyzing the image. {response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<ImageAnalysisResponse>(body!);
        if (result is null)
        {
            throw new AIException(
                AIException.ErrorCodes.UnknownError,
                $"Content moderator: Failed analyzing the image. {body}");
        }
        return result;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this._httpClient.Dispose();
        this._httpClientHandler?.Dispose();
    }
}