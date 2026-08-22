using System.Globalization;

namespace AutoFpl.Api.Intelligence;

public sealed record EvidenceSemanticReviewProviderOptions
{
    public const string ConfigurationSection =
        "AutoFpl:Ai:EvidenceSemanticReview";

    private static readonly IReadOnlyDictionary<string, string> DefaultHosts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openai"] = "api.openai.com",
            ["openrouter"] = "openrouter.ai",
        };

    private EvidenceSemanticReviewProviderOptions(
        bool enabled,
        string provider,
        Uri? endpoint,
        string model,
        string? apiKey,
        string routingPolicyVersion,
        string tokenLimitParameter,
        int maximumOutputTokens,
        int maximumRequestBytes,
        int maximumResponseBytes,
        TimeSpan timeout,
        TimeSpan? interval)
    {
        Enabled = enabled;
        Provider = provider;
        Endpoint = endpoint;
        Model = model;
        ApiKey = apiKey;
        RoutingPolicyVersion = routingPolicyVersion;
        TokenLimitParameter = tokenLimitParameter;
        MaximumOutputTokens = maximumOutputTokens;
        MaximumRequestBytes = maximumRequestBytes;
        MaximumResponseBytes = maximumResponseBytes;
        Timeout = timeout;
        Interval = interval;
    }

    public bool Enabled { get; }
    public string Provider { get; }
    public Uri? Endpoint { get; }
    public string Model { get; }
    public string? ApiKey { get; }
    public string RoutingPolicyVersion { get; }
    public string TokenLimitParameter { get; }
    public int MaximumOutputTokens { get; }
    public int MaximumRequestBytes { get; }
    public int MaximumResponseBytes { get; }
    public TimeSpan Timeout { get; }
    public TimeSpan? Interval { get; }

    public static EvidenceSemanticReviewProviderOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetSection(
            ConfigurationSection);
        bool enabled = ReadBool(section, "Enabled", false);
        if (!enabled)
        {
            return Disabled();
        }

        string provider = Required(section, "Provider").ToLowerInvariant();
        if (provider is not ("openai" or "openrouter" or "hermes"))
        {
            throw Invalid("Provider must be openai, openrouter, or hermes.");
        }

        string endpointText = Required(section, "Endpoint");
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out Uri? endpoint)
            || endpoint.UserInfo.Length > 0
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw Invalid("Endpoint must be an absolute URI without user info, query, or fragment.");
        }
        ValidateEndpoint(section, provider, endpoint);

        string model = Required(section, "Model");
        string? apiKey = section["ApiKey"]?.Trim();
        if (provider is "openai" or "openrouter"
            && string.IsNullOrWhiteSpace(apiKey))
        {
            throw Invalid("ApiKey is required for openai and openrouter.");
        }

        string routingPolicyVersion = section["RoutingPolicyVersion"]?.Trim()
            ?? "owner-fixed-route-v1";
        if (routingPolicyVersion.Length is < 1 or > 80)
        {
            throw Invalid("RoutingPolicyVersion must contain 1 through 80 characters.");
        }

        string tokenLimitParameter = section["TokenLimitParameter"]?.Trim()
            ?? (provider == "openai" ? "max_completion_tokens" : "max_tokens");
        if (tokenLimitParameter is not ("max_completion_tokens" or "max_tokens"))
        {
            throw Invalid("TokenLimitParameter must be max_completion_tokens or max_tokens.");
        }

        int maximumOutputTokens = ReadInt(section, "MaximumOutputTokens", 4096, 256, 8192);
        int maximumRequestBytes = ReadInt(section, "MaximumRequestBytes", 524_288, 65_536, 2_097_152);
        int maximumResponseBytes = ReadInt(section, "MaximumResponseBytes", 524_288, 4_096, 1_048_576);
        int timeoutSeconds = ReadInt(section, "TimeoutSeconds", 45, 1, 120);
        int intervalMinutes = ReadInt(section, "PollIntervalMinutes", 5, 1, 1_440);

        return new(
            true,
            provider,
            endpoint,
            model,
            apiKey,
            routingPolicyVersion,
            tokenLimitParameter,
            maximumOutputTokens,
            maximumRequestBytes,
            maximumResponseBytes,
            TimeSpan.FromSeconds(timeoutSeconds),
            TimeSpan.FromMinutes(intervalMinutes));
    }

    internal static EvidenceSemanticReviewProviderOptions CreateForTests(
        string provider,
        Uri endpoint,
        string model,
        string? apiKey = "test-key") =>
        new(
            true,
            provider,
            endpoint,
            model,
            apiKey,
            "test-route-v1",
            provider == "openai" ? "max_completion_tokens" : "max_tokens",
            4096,
            524_288,
            524_288,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(5));

    private static EvidenceSemanticReviewProviderOptions Disabled() =>
        new(
            false,
            string.Empty,
            null,
            string.Empty,
            null,
            string.Empty,
            "max_tokens",
            0,
            0,
            0,
            TimeSpan.Zero,
            null);

    private static void ValidateEndpoint(
        IConfigurationSection section,
        string provider,
        Uri endpoint)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps
            && !(provider == "hermes" && endpoint.Scheme == Uri.UriSchemeHttp))
        {
            throw Invalid("Endpoint must use HTTPS; an owner-allowlisted Hermes endpoint may use HTTP.");
        }
        if (!endpoint.AbsolutePath.EndsWith(
                "/chat/completions",
                StringComparison.Ordinal))
        {
            throw Invalid("Endpoint path must end with /chat/completions.");
        }
        if (provider == "openai"
            && endpoint.AbsolutePath != "/v1/chat/completions")
        {
            throw Invalid("The OpenAI endpoint path must be /v1/chat/completions.");
        }
        if (provider == "openrouter"
            && endpoint.AbsolutePath != "/api/v1/chat/completions")
        {
            throw Invalid("The OpenRouter endpoint path must be /api/v1/chat/completions.");
        }

        string[] allowedHosts = (section["AllowedEndpointHosts"]
                ?? (DefaultHosts.TryGetValue(provider, out string? host) ? host : string.Empty))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowedHosts.Length == 0
            || !allowedHosts.Contains(endpoint.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            throw Invalid("Endpoint host must be present in AllowedEndpointHosts.");
        }
    }

    private static string Required(IConfigurationSection section, string key)
    {
        string? value = section[key]?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw Invalid($"{key} is required when enabled.")
            : value;
    }

    private static bool ReadBool(
        IConfigurationSection section,
        string key,
        bool defaultValue)
    {
        string? value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        return bool.TryParse(value, out bool parsed)
            ? parsed
            : throw Invalid($"{key} must be true or false.");
    }

    private static int ReadInt(
        IConfigurationSection section,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        string? value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed >= minimum
            && parsed <= maximum
            ? parsed
            : throw Invalid($"{key} must be an integer from {minimum} through {maximum}.");
    }

    private static InvalidOperationException Invalid(string message) =>
        new($"{ConfigurationSection}:{message}");
}
