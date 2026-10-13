using System.Net;

namespace Cichlids.Etl.Identity;

/// <summary>
/// A failed Keycloak admin request. The message names the operation and the HTTP status and, when
/// Keycloak sent a short error code, that code. The response body is never part of the exception,
/// because it can contain usernames and email addresses of accounts.
/// </summary>
public sealed class KeycloakAdminException : InvalidOperationException
{
    public KeycloakAdminException(string operation, HttpStatusCode statusCode, string? errorCode)
        : base(BuildMessage(operation, statusCode, errorCode))
    {
        Operation = operation;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    /// <summary>
    /// The operation that failed, in words.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// The HTTP status of the answer.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The error code from the answer, or null when the answer has none or the value is not a
    /// short identifier.
    /// </summary>
    public string? ErrorCode { get; }

    private static string BuildMessage(string operation, HttpStatusCode statusCode, string? errorCode) =>
        errorCode is null
            ? $"Keycloak request to {operation} failed with {(int)statusCode} {statusCode}."
            : $"Keycloak request to {operation} failed with {(int)statusCode} {statusCode} (error code {errorCode}).";
}
