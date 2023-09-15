﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CopilotChat.WebApi.Extensions;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticMemory;

namespace CopilotChat.WebApi.Services.MemoryMigration;

/// <summary>
/// Service implementation of <see cref="IChatMemoryMigrationService"/>.
/// </summary>
public class ChatMemoryMigrationService : IChatMemoryMigrationService
{
    private readonly ILogger<ChatMemoryMigrationService> _logger;
    private readonly ISemanticTextMemory memory; // $$$
    private readonly ISemanticMemoryClient _memoryClient;
    private readonly ChatSessionRepository _chatSessionRepository;
    private readonly ChatMemorySourceRepository _memorySourceRepository;
    private readonly PromptsOptions _promptOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMigrationMonitor"/> class.
    /// </summary>
    public ChatMemoryMigrationService(
        ILogger<ChatMemoryMigrationService> logger,
        IOptions<DocumentMemoryOptions> documentMemoryOptions,
        IOptions<PromptsOptions> promptOptions,
        ISemanticMemoryClient memoryClient,
        ChatSessionRepository chatSessionRepository,
        ChatMemorySourceRepository memorySourceRepository)
    {
        this._logger = logger;
        this._promptOptions = promptOptions.Value;
        this._memoryClient = memoryClient;
        this._chatSessionRepository = chatSessionRepository;
        this._memorySourceRepository = memorySourceRepository;
    }

    ///<inheritdoc/>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var shouldMigrate = false;

        var tokenMemory = await GetTokenMemory(cancellationToken).ConfigureAwait(false);
        if (tokenMemory == null)
        {
            //  Create token memory
            var token = Guid.NewGuid().ToString();
            await SetTokenMemory(token, cancellationToken).ConfigureAwait(false);
            // Allow writes that are racing time to land
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            // Retrieve token memory
            tokenMemory = await GetTokenMemory(cancellationToken).ConfigureAwait(false);
            // Set migrate flag if token matches
            shouldMigrate = tokenMemory != null && tokenMemory.Metadata.Text.Equals(token, StringComparison.OrdinalIgnoreCase);
        }

        if (!shouldMigrate)
        {
            return;
        }

        await RemoveMemorySourcesAsync().ConfigureAwait(false);

        // Extract and store memories, using the original id to avoid duplication should a retry be required.
        await foreach ((string chatId, string memoryName, string memoryId, string memoryText) in QueryMemoriesAsync())
        {
            await this._memoryClient.StoreMemoryAsync(this._promptOptions.MemoryIndexName, chatId, memoryName, memoryId, memoryText, cancellationToken);
        }

        await SetTokenMemory(ChatMigrationMonitor.MigrationCompletionToken, cancellationToken).ConfigureAwait(false);

        // Inline function to extract all memories for a given chat and memory type.
        async IAsyncEnumerable<(string chatId, string memoryName, string memoryId, string memoryText)> QueryMemoriesAsync()
        {
            var chats = await this._chatSessionRepository.GetAllChatsAsync().ConfigureAwait(false);
            foreach (var chat in chats)
            {
                foreach (var memoryType in this._promptOptions.MemoryMap.Keys)
                {
                    var indexName = $"{chat.Id}-{memoryType}";
                    var memories = await this.memory.SearchAsync(indexName, "*", limit: int.MaxValue, minRelevanceScore: -1, withEmbeddings: false, cancellationToken).ToArrayAsync(cancellationToken);

                    foreach (var memory in memories)
                    {
                        yield return (chat.Id, memoryType, memory.Metadata.Id, memory.Metadata.Text);
                    }
                }
            }
        }

        // Inline function to read the token memory
        async Task<MemoryQueryResult?> GetTokenMemory(CancellationToken cancellationToken)
        {
            return await this.memory.GetAsync(this._promptOptions.MemoryIndexName, ChatMigrationMonitor.MigrationKey, withEmbedding: false, cancellationToken).ConfigureAwait(false);
        }

        // Inline function to write the token memory
        async Task SetTokenMemory(string token, CancellationToken cancellationToken)
        {
            await this.memory.SaveInformationAsync(this._promptOptions.MemoryIndexName, token, ChatMigrationMonitor.MigrationKey, description: null, additionalMetadata: null, cancellationToken).ConfigureAwait(false);
        }

        async Task RemoveMemorySourcesAsync()
        {
            var documentMemories = await this._memorySourceRepository.GetAllAsync().ConfigureAwait(false);

            await Task.WhenAll(documentMemories.Select(memory => this._memorySourceRepository.DeleteAsync(memory))).ConfigureAwait(false);
        }
    }
}
