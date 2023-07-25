﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.SemanticMemory.Core.AppBuilders;
using Microsoft.SemanticKernel.SemanticMemory.Core.Configuration;
using Microsoft.SemanticKernel.SemanticMemory.Core.Diagnostics;
using Microsoft.SemanticKernel.SemanticMemory.Core.Handlers;
using Microsoft.SemanticKernel.SemanticMemory.Core.Pipeline;
using Microsoft.SemanticKernel.SemanticMemory.Core20;

namespace Microsoft.SemanticKernel.SemanticMemory.SemanticMemoryPipelineClient;

public class MemoryPipelineClient : ISemanticMemoryClient
{
    private readonly Lazy<Task<InProcessPipelineOrchestrator>> _inProcessOrchestrator = new(BuildInProcessOrchestratorAsync);

    private Task<InProcessPipelineOrchestrator> Orchestrator
    {
        get { return this._inProcessOrchestrator.Value; }
    }

    public Task ImportFileAsync(string file, ImportFileOptions options)
    {
        return this.ImportFilesInternalAsync(new[] { file }, options);
    }

    public Task ImportFilesAsync(string[] files, ImportFileOptions options)
    {
        return this.ImportFilesInternalAsync(files, options);
    }

    public async Task<string> AskAsync(string question)
    {
        await Task.Delay(0).ConfigureAwait(false);
        return "...work in progress...";
    }

    private async Task ImportFilesInternalAsync(string[] files, ImportFileOptions options)
    {
        options.Sanitize();
        options.Validate();

        InProcessPipelineOrchestrator orchestrator = await this.Orchestrator.ConfigureAwait(false);

        var pipeline = orchestrator
            .PrepareNewFileUploadPipeline(options.RequestId, options.UserId, options.VaultIds);

        // Include all files
        for (int index = 0; index < files.Length; index++)
        {
            string file = files[index];
            pipeline.AddUploadFile($"file{index + 1}", file, file);
        }

        pipeline
            .Then("extract")
            .Then("partition")
            .Then("gen_embeddings")
            .Then("save_embeddings")
            .Build();

        // Execute pipeline
        await orchestrator.RunPipelineAsync(pipeline).ConfigureAwait(false);
    }

    private static async Task<InProcessPipelineOrchestrator> BuildInProcessOrchestratorAsync()
    {
        IServiceProvider services = AppBuilder.Build().Services;

        var orchestrator = GetOrchestrator(services);

        // Text extraction handler
        TextExtractionHandler textExtraction = new("extract",
            orchestrator, GetLogger<TextExtractionHandler>(services));
        await orchestrator.AddHandlerAsync(textExtraction).ConfigureAwait(false);

        // Text partitioning handler
        TextPartitioningHandler textPartitioning = new("partition",
            orchestrator, GetLogger<TextPartitioningHandler>(services));
        await orchestrator.AddHandlerAsync(textPartitioning).ConfigureAwait(false);

        // Embedding generation handler
        GenerateEmbeddingsHandler textEmbedding = new("gen_embeddings",
            orchestrator, GetConfig(services), GetLogger<GenerateEmbeddingsHandler>(services));
        await orchestrator.AddHandlerAsync(textEmbedding).ConfigureAwait(false);

        // Embedding storage handler
        SaveEmbeddingsToAzureCognitiveSearchHandler saveEmbedding = new("save_embeddings",
            orchestrator, GetConfig(services), GetLogger<SaveEmbeddingsToAzureCognitiveSearchHandler>(services));
        await orchestrator.AddHandlerAsync(saveEmbedding).ConfigureAwait(false);

        return orchestrator;
    }

    private static InProcessPipelineOrchestrator GetOrchestrator(IServiceProvider services)
    {
        var orchestrator = services.GetService<InProcessPipelineOrchestrator>();
        if (orchestrator == null)
        {
            throw new ArgumentNullException(nameof(orchestrator),
                $"Unable to instantiate {typeof(InProcessPipelineOrchestrator)} with AppBuilder");
        }

        return orchestrator;
    }

    private static SKMemoryConfig GetConfig(IServiceProvider services)
    {
        var config = services.GetService<SKMemoryConfig>();
        if (config == null)
        {
            throw new OrchestrationException("Unable to load configuration, object is NULL");
        }

        return config;
    }

    private static ILogger<T>? GetLogger<T>(IServiceProvider services)
    {
        return services.GetService<ILogger<T>>();
    }
}
