using System.IO;

namespace CheckMind.App.Core;

public sealed class OcrModelSelection
{
    public OcrModelSelection(IOcrAdapter adapter, string description)
    {
        Adapter = adapter;
        Description = description;
    }

    public IOcrAdapter Adapter { get; }
    public string Description { get; }
}

public sealed class OcrModelSelector
{
    public async Task<OcrModelSelection> SelectAsync(
        AppConfig config,
        string? sampleImagePath,
        RunContext probeRun,
        CancellationToken ct = default
    )
    {
        var mode = EnvironmentValueResolver.Get("CHECKMIND_OCR_MODE");
        if (string.Equals(mode, "mock", StringComparison.OrdinalIgnoreCase))
        {
            return new OcrModelSelection(new MockOcrAdapter(), "mock");
        }

        var explicitBaseUrl = EnvironmentValueResolver.Get("CHECKMIND_OCR_BASE_URL");
        var explicitModelId = EnvironmentValueResolver.Get("CHECKMIND_OCR_MODEL_ID");
        var explicitApiKeyEnv = EnvironmentValueResolver.Get("CHECKMIND_OCR_API_KEY_ENV");
        if (!string.IsNullOrWhiteSpace(explicitBaseUrl) && !string.IsNullOrWhiteSpace(explicitModelId))
        {
            var explicitApiKey = string.IsNullOrWhiteSpace(explicitApiKeyEnv) ? null : EnvironmentValueResolver.Get(explicitApiKeyEnv);
            return new OcrModelSelection(
                new LlmVisionTextOcrAdapter(new LlmClient(LlmClient.CreateHttpClient(explicitBaseUrl, explicitApiKey)), explicitModelId),
                $"{explicitBaseUrl}|{explicitModelId}"
            );
        }

        var probeEnabled = EnvironmentValueResolver.Get("CHECKMIND_OCR_PROBE");
        if (string.Equals(probeEnabled, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(probeEnabled, "false", StringComparison.OrdinalIgnoreCase))
        {
            return CreateFirstConfigured(config) ?? new OcrModelSelection(new MockOcrAdapter(), "mock");
        }

        if (string.IsNullOrWhiteSpace(sampleImagePath) || !File.Exists(sampleImagePath))
        {
            return CreateFirstConfigured(config) ?? new OcrModelSelection(new MockOcrAdapter(), "mock");
        }

        var imageBytes = File.ReadAllBytes(sampleImagePath);
        var imageMime = OcrRunner.InferMimeFromPath(sampleImagePath);
        var (w, h) = ImageGeometry.GetSize(imageBytes);
        var probeRoi = ImageGeometry.FromRelative(0.52, 0.00, 0.46, 0.22, w, h);

        foreach (var endpoint in config.LlmEndpoints)
        {
            var apiKey = string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvVar)
                ? null
                : EnvironmentValueResolver.Get(endpoint.ApiKeyEnvVar);
            var modelIds = endpoint.PreferredModelIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                ?? (string.IsNullOrWhiteSpace(endpoint.DefaultModelId) ? Array.Empty<string>() : new[] { endpoint.DefaultModelId! });

            foreach (var modelId in modelIds)
            {
                try
                {
                    var adapter = new LlmVisionTextOcrAdapter(
                        new LlmClient(LlmClient.CreateHttpClient(endpoint.BaseUrl, apiKey)),
                        modelId
                    );

                    var ocrId = OcrId.Make("probe", $"{Path.GetFileNameWithoutExtension(sampleImagePath)}_{modelId}");
                    var (_, result) = await new OcrRunner(adapter, new OcrStore())
                        .RunAsync(probeRun, ocrId, imageBytes, imageMime, probeRoi, "probe 0146 header", ct);

                    var text = result.Blocks.FirstOrDefault()?.Text ?? string.Empty;
                    if (text.Contains("0146", StringComparison.OrdinalIgnoreCase))
                    {
                        return new OcrModelSelection(adapter, $"{endpoint.BaseUrl}|{modelId}");
                    }
                }
                catch
                {
                    // Probe failure means keep trying candidates.
                }
            }
        }

        return CreateFirstConfigured(config) ?? new OcrModelSelection(new MockOcrAdapter(), "mock");
    }

    private static OcrModelSelection? CreateFirstConfigured(AppConfig config)
    {
        foreach (var endpoint in config.LlmEndpoints)
        {
            var apiKey = string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvVar)
                ? null
                : EnvironmentValueResolver.Get(endpoint.ApiKeyEnvVar);
            var modelId = endpoint.PreferredModelIds?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? endpoint.DefaultModelId;
            if (string.IsNullOrWhiteSpace(endpoint.BaseUrl) || string.IsNullOrWhiteSpace(modelId))
            {
                continue;
            }

            var adapter = new LlmVisionTextOcrAdapter(
                new LlmClient(LlmClient.CreateHttpClient(endpoint.BaseUrl, apiKey)),
                modelId
            );
            return new OcrModelSelection(adapter, $"{endpoint.BaseUrl}|{modelId}");
        }

        return null;
    }
}
