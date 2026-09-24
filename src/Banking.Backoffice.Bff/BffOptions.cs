namespace Banking.Backoffice.Bff;

public sealed class BffOptions
{
    public const string SectionName = "Bff";

    /// <summary>Endereço interno da API, para onde o BFF repassa /api.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>Realm no endereço público (emissor dos tokens).</summary>
    public string Authority { get; set; } = "http://localhost:8080/realms/banking";

    /// <summary>
    /// Metadados pelo endereço interno. O Keycloak devolve a URL de login pública e as de backchannel internas (ADR-009).
    /// </summary>
    public string? MetadataAddress { get; set; }

    public string ClientId { get; set; } = "banking-backoffice";

    /// <summary>Segredo do cliente confidencial. Vem de user-secrets ou variável de ambiente, nunca do appsettings.</summary>
    public string? ClientSecret { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Em desenvolvimento, o BFF repassa o que não é /api nem /bff ao servidor do Vite.</summary>
    public string? SpaDevServerUrl { get; set; }
}
