namespace Banking.Bff;

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

    /// <summary>
    /// Papéis que entram neste front, separados por vírgula. Quem não tem nenhum deles recebe 403 em tudo além do login.
    /// Texto e não lista: o binder somaria os itens de outra fonte de configuração aos padrões em vez de trocá-los.
    /// </summary>
    public string AllowedRoles { get; set; } = "operator,admin";

    public string[] AllowedRoleList => AllowedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Um cookie por front, para que as duas sessões não se misturem no mesmo navegador. Precisa do prefixo __Host-.</summary>
    public string CookieName { get; set; } = "__Host-backoffice";

    /// <summary>Segredo do cliente confidencial. Vem de user-secrets ou variável de ambiente, nunca do appsettings.</summary>
    public string? ClientSecret { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Endereço pelo qual o navegador chega a esta instância, quando não é o da própria requisição: atrás de um proxy
    /// que termina o TLS, como o encaminhamento de portas do Codespaces, o BFF vê http e o host interno. Vale para os
    /// endereços de retorno do login e do logout, que o Keycloak confere contra a lista do cliente.
    /// </summary>
    /// <remarks>Texto, e vazio conta como ausente: o devcontainer passa a variável vazia fora do Codespaces.</remarks>
    public string? PublicUrl { get; set; }

    /// <summary>Em desenvolvimento, o BFF repassa o que não é /api nem /bff ao servidor do Vite.</summary>
    public string? SpaDevServerUrl { get; set; }
}
