using Confluent.Kafka;

namespace BlueberryMart.Api.Configuration;

public static class KafkaConfigExtensions
{
    /// <summary>
    /// Applies SASL_SSL authentication for a managed broker (Confluent Cloud) when an
    /// API key is configured. With no key (local Redpanda), the default PLAINTEXT
    /// settings are left untouched, so the same code runs locally and in production.
    /// </summary>
    public static T WithSecurity<T>(this T config, KafkaOptions opts) where T : ClientConfig
    {
        if (!string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
            config.SaslMechanism = SaslMechanism.Plain;
            config.SaslUsername = opts.ApiKey;
            config.SaslPassword = opts.ApiSecret;
        }
        return config;
    }
}
