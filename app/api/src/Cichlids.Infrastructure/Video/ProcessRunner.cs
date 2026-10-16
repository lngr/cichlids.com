using System.Diagnostics;
using System.Text;

namespace Cichlids.Infrastructure.Video;

/// <summary>
/// A child process to run: the program, its arguments as separate values, the time after which it
/// is stopped and an optional niceness that lowers its scheduling priority.
/// </summary>
public sealed record ProcessRequest(
    string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout, int? Nice = null, string? WorkingDirectory = null,
    int? MaxStandardOutput = null);

/// <summary>
/// The exit code and the captured output of a finished child process. Output beyond
/// ProcessRunner.MaxCapturedCharacters is cut from the front, so the end of a long log remains.
/// </summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// A child process that ran longer than its timeout and was stopped.
/// </summary>
public sealed class ProcessTimeoutException(string fileName, TimeSpan timeout)
    : Exception($"{fileName} did not finish within {timeout}.");

/// <summary>
/// A child process that printed more standard output than its request allows and was stopped.
/// </summary>
public sealed class ProcessOutputTooLargeException(string fileName, int limit)
    : Exception($"{fileName} printed more than {limit} characters.");

/// <summary>
/// Runs a child process.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs the process and waits for it asynchronously. Throws ProcessTimeoutException when the
    /// timeout passes and OperationCanceledException when the token is cancelled; in both cases the
    /// process and its children are killed first. Throws ProcessOutputTooLargeException when the
    /// standard output exceeds the request's limit, after killing the process.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Runs child processes without a shell: every argument reaches the program as one value, so file
/// names and other input are never interpreted. A requested niceness is applied by starting the
/// program through nice, which replaces itself with the program.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public const int MaxCapturedCharacters = 16 * 1024;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
        };

        if (request.Nice is { } nice && !OperatingSystem.IsWindows())
        {
            startInfo.FileName = "nice";
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add(nice.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(request.FileName);
        }
        else
        {
            startInfo.FileName = request.FileName;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        var overflowed = false;
        var output = request.MaxStandardOutput is { } limit
            ? ReadLimitedAsync(process.StandardOutput, limit, () =>
            {
                overflowed = true;
                timeout.Cancel();
            })
            : ReadTailAsync(process.StandardOutput);
        var error = ReadTailAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (overflowed)
            {
                throw new ProcessOutputTooLargeException(request.FileName, request.MaxStandardOutput!.Value);
            }

            throw new ProcessTimeoutException(request.FileName, request.Timeout);
        }

        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process ended between the timeout and the kill.
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int limit, Action onOverflow)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, CancellationToken.None)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > limit)
            {
                onOverflow();
                break;
            }
        }

        return text.ToString();
    }

    private static async Task<string> ReadTailAsync(StreamReader reader)
    {
        var tail = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, CancellationToken.None)) > 0)
        {
            tail.Append(buffer, 0, read);
            if (tail.Length > MaxCapturedCharacters)
            {
                tail.Remove(0, tail.Length - MaxCapturedCharacters);
            }
        }

        return tail.ToString();
    }
}
