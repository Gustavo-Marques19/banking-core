using Banking.Application.Common;
using Banking.Application.Idempotency;

namespace Banking.Api.Http;

internal static class ApiResults
{
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static IResult From<T>(Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value!) : Problem(result.Error!);

    public static IResult From<T>(IdempotentResult<T> outcome, HttpContext context, Func<T, IResult> onSuccess)
    {
        if (outcome.Replayed)
        {
            context.Response.Headers[ReplayedHeader] = "true";
        }

        return From(outcome.Result, onSuccess);
    }

    public static IResult Problem(Error error) => Results.Problem(
        statusCode: error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        },
        title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    /// <summary>Recusa de negócio: a operação existe, foi gravada e a resposta se repete na idempotência.</summary>
    public static IResult Rejected(string code, string title, string resourceName, Guid resourceId) => Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code, [resourceName] = resourceId });
}
