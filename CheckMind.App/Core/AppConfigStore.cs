using System.Text;
using System.Text.Json;
using System.IO;

namespace CheckMind.App.Core;

public sealed class AppConfigStore
{
    private readonly string _configPath;

    public AppConfigStore(string? configPath = null)
    {
        _configPath = configPath ?? GetDefaultConfigPath();
    }

    public string ConfigPath => _configPath;

    public AppConfig LoadOrCreateDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);

        if (!File.Exists(_configPath))
        {
            var created = CreateDefault();
            Save(created);
            return created;
        }

        var json = File.ReadAllText(_configPath, Encoding.UTF8);
        var cfg = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
        cfg ??= CreateDefault();
        if (cfg.AutomationUi is null)
        {
            cfg = cfg with { AutomationUi = new AutomationUiConfig() };
        }
        return cfg;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
        File.WriteAllText(_configPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static AppConfig CreateDefault()
    {
        return new AppConfig(
            new[]
            {
                new LlmEndpointConfig(
                    BaseUrl: "http://172.18.3.68:11434/v1",
                    DefaultModelId: "gemma4:26b",
                    PreferredModelIds: new[] { "gemma4:26b", "gemma4:e4b" },
                    ApiKeyEnvVar: "CHECKMIND_LLM_API_KEY_11434",
                    Tags: new[] { "internal", "vision", "multimodal" }
                ),
                new LlmEndpointConfig(
                    BaseUrl: "http://172.18.3.68:18080/v1",
                    DefaultModelId: "Qwen3.5-35B-A3B-Q4_K_M",
                    PreferredModelIds: new[] { "Qwen3.5-35B-A3B-Q4_K_M" },
                    ApiKeyEnvVar: "CHECKMIND_LLM_API_KEY_18080",
                    Tags: new[] { "internal", "vision", "multimodal" }
                )
            },
            new AutomationUiConfig()
        );
    }

    private static string GetDefaultConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CheckMind", "config", "config.json");
    }
}
