using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Common;

/// <summary>
/// Problem details responses that carry a stable machine-readable code next to the status, so a
/// client can tell a refusal from other responses of the same status.
/// </summary>
public static class ApiProblems
{
    public static ProblemHttpResult WithCode(int statusCode, string title, string detail, string code) =>
        TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// A problem with a code and further members that describe the refusal.
    /// </summary>
    public static ProblemHttpResult WithCode(
        int statusCode, string title, string detail, string code, IReadOnlyDictionary<string, object?> members)
    {
        var extensions = new Dictionary<string, object?>(members) { ["code"] = code };
        return TypedResults.Problem(title: title, detail: detail, statusCode: statusCode, extensions: extensions);
    }
}
