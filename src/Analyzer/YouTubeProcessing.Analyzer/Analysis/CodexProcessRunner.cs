using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Analyzer.Analysis;

public interface ICodexProcessRunner
{
    Task<string> RunAsync(
        string prompt,
        IReadOnlyList<AnalysisScenarioV1> scenarios,
        CancellationToken cancellationToken);
}

public sealed class CodexProcessRunner(IOptions<AnalyzerOptions> options) : ICodexProcessRunner
{
    public async Task<string> RunAsync(
        string prompt,
        IReadOnlyList<AnalysisScenarioV1> scenarios,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "youtube-analyzer-codex",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var schemaPath = Path.Combine(
                AppContext.BaseDirectory,
                AnalysisOutputSchemaSelector.Select(scenarios));
            if (!File.Exists(schemaPath))
            {
                throw new InvalidOperationException(
                    $"Codex output schema was not found at '{schemaPath}'.");
            }

            using var process = new Process
            {
                StartInfo = CodexCommandFactory.Create(settings, workingDirectory, schemaPath)
            };

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Codex CLI could not be started.");
                }
            }
            catch (Win32Exception exception)
            {
                throw new InvalidOperationException(
                    $"Codex CLI executable '{settings.CodexExecutable}' was not found or could not be started.",
                    exception);
            }

            try
            {
                var stdoutTask = ReadBoundedAsync(
                    process.StandardOutput,
                    settings.MaxProviderOutputCharacters);
                var stderrTask = ReadBoundedAsync(process.StandardError, 8_192);

                await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken);
                process.StandardInput.Close();

                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(settings.ProviderTimeoutSeconds));
                using var combined = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token);
                try
                {
                    await process.WaitForExitAsync(combined.Token);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    Kill(process);
                    await Task.WhenAll(stdoutTask, stderrTask);
                    throw new TaskCanceledException(
                        $"Codex analysis exceeded the {settings.ProviderTimeoutSeconds}-second timeout.");
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    await Task.WhenAll(stdoutTask, stderrTask);
                    throw;
                }

                var stdout = await stdoutTask;
                _ = await stderrTask;
                if (process.ExitCode != 0)
                {
                    throw new HttpRequestException(
                        $"Codex CLI exited with code {process.ExitCode}.");
                }
                if (stdout.WasTruncated)
                {
                    throw new InvalidOperationException(
                        $"Codex output exceeded {settings.MaxProviderOutputCharacters} characters.");
                }
                if (string.IsNullOrWhiteSpace(stdout.Text))
                {
                    throw new InvalidOperationException("Codex returned an empty response.");
                }

                return stdout.Text;
            }
            finally
            {
                Kill(process);
            }
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    private static async Task<BoundedText> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters)
    {
        var result = new StringBuilder(Math.Min(maximumCharacters, 4_096));
        var buffer = new char[4_096];
        var wasTruncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            var remaining = maximumCharacters - result.Length;
            if (remaining > 0)
            {
                result.Append(buffer, 0, Math.Min(remaining, read));
            }
            wasTruncated |= read > remaining;
        }

        return new BoundedText(result.ToString(), wasTruncated);
    }

    private static void Kill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private sealed record BoundedText(string Text, bool WasTruncated);
}

public static class AnalysisOutputSchemaSelector
{
    public static string Select(IReadOnlyList<AnalysisScenarioV1> scenarios)
    {
        var hasSummary = scenarios.Contains(AnalysisScenarioV1.Summary);
        var hasTopics = scenarios.Contains(AnalysisScenarioV1.Topics);
        return (hasSummary, hasTopics) switch
        {
            (true, true) => "analysis-output-schema.json",
            (true, false) => "summary-output-schema.json",
            (false, true) => "topics-output-schema.json",
            _ => throw new ArgumentException(
                "At least one analysis scenario is required.",
                nameof(scenarios))
        };
    }
}

public static class CodexCommandFactory
{
    public static ProcessStartInfo Create(
        AnalyzerOptions settings,
        string workingDirectory,
        string outputSchemaPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.CodexExecutable,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        AddArguments(
            startInfo,
            "exec",
            "--skip-git-repo-check",
            "--ephemeral",
            "--ignore-user-config",
            "--ignore-rules",
            "--model",
            settings.Model,
            "--config",
            $"model_reasoning_effort=\"{settings.ReasoningEffort}\"",
            "--config",
            "approval_policy=\"never\"",
            "--config",
            "allow_login_shell=false",
            "--config",
            "web_search=\"disabled\"",
            "--config",
            "shell_environment_policy.inherit=\"none\"",
            "--config",
            "shell_environment_policy.ignore_default_excludes=false",
            "--config",
            "default_permissions=\"analyzer\"",
            "--config",
            "permissions.analyzer.filesystem.:root=\"deny\"",
            "--config",
            "permissions.analyzer.filesystem.:minimal=\"read\"",
            "--config",
            "permissions.analyzer.network.enabled=false",
            "--output-schema",
            outputSchemaPath,
            "-");

        return startInfo;
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }
}
