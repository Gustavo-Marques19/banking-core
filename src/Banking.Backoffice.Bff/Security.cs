namespace Banking.Backoffice.Bff;

/// <summary>
/// Requisição que muda estado em /api ou /bff precisa do header X-CSRF. Outro site não consegue enviá-lo sem CORS,
/// e o BFF não habilita CORS; junto com o cookie SameSite=Strict, fecha o CSRF.
/// </summary>
internal sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public const string Header = "X-CSRF";

    public Task InvokeAsync(HttpContext context)
    {
        var protectedPath = context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/bff");
        var safeMethod = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method);

        if (protectedPath && !safeMethod && context.Request.Headers[Header] != "1")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new { title = "Falta o header X-CSRF.", code = "csrf_header_required" });
        }

        return next(context);
    }
}

/// <summary>CSP estrita e headers de segurança em toda resposta.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; "
            + "connect-src 'self'" + (environment.IsDevelopment() ? " ws:" : string.Empty)
            + "; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        return next(context);
    }
}
