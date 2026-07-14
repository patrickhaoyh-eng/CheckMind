using System.Text.Json.Serialization;

namespace CheckMind.App.Core;

public sealed record AppConfig(
    IReadOnlyList<LlmEndpointConfig> LlmEndpoints,
    AutomationUiConfig? AutomationUi = null
);

public sealed record LlmEndpointConfig(
    string BaseUrl,
    string? DefaultModelId = null,
    IReadOnlyList<string>? PreferredModelIds = null,
    string? ApiKeyEnvVar = null,
    IReadOnlyList<string>? Tags = null
);

[JsonSerializable(typeof(AutomationUiConfig))]
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class AppConfigJsonContext : JsonSerializerContext;

public sealed record AutomationUiConfig(
    bool OverlayEnabled = false,
    bool SuppressMouseCapturePrompt = false
);
