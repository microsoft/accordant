// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// Persists and reloads <see cref="RecordedTrace"/> snapshots as single indented JSON
/// files.
///
/// Two saving modes are supported, for two different kinds of trace file:
/// <list type="bullet">
/// <item><see cref="SaveAsync(string, RecordedTrace)"/> saves an anonymous, ID-named
/// trace that is immutable once persisted - the right choice for an unbounded, ever-
/// growing corpus of recorded evidence, where no two traces are ever supposed to
/// collide or overwrite one another.</item>
/// <item><see cref="SaveAsync(string, RecordedTrace, string)"/> saves a caller-named
/// trace that is deliberately overwritable - the right choice for a small, curated set
/// of traces each identified by a stable name (for example, a hand-authored test case's
/// expected trace, re-recorded on purpose whenever the caller chooses to).</item>
/// </list>
/// </summary>
public static class TraceStore
{
    /// <summary>
    /// Atomically saves <paramref name="trace"/> as a new JSON file in
    /// <paramref name="tracesDirectory"/>, creating the directory if it does not exist.
    /// The file name is derived from the trace's unique <see cref="RecordedTrace.TraceId"/>.
    /// A trace file, once persisted, is immutable: this method never overwrites an
    /// existing file, whether that file holds a completed or an interrupted trace.
    /// </summary>
    /// <param name="tracesDirectory">The directory to write the trace file into.</param>
    /// <param name="trace">The trace to persist.</param>
    /// <returns>The full path of the file that was written.</returns>
    /// <exception cref="InvalidOperationException">
    /// A trace file for <paramref name="trace"/>'s ID already exists.
    /// </exception>
    public static Task<string> SaveAsync(string tracesDirectory, RecordedTrace trace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracesDirectory);
        ArgumentNullException.ThrowIfNull(trace);

        return SaveCoreAsync(tracesDirectory, trace, $"{trace.TraceId:N}", overwrite: false);
    }

    /// <summary>
    /// Atomically saves <paramref name="trace"/> as <c>{name}.json</c> in
    /// <paramref name="tracesDirectory"/>, creating the directory if it does not exist.
    /// Unlike <see cref="SaveAsync(string, RecordedTrace)"/>, this always (over)writes the
    /// file: callers use a stable, meaningful <paramref name="name"/> - rather than the
    /// trace's own ID - precisely so a later save under the same name is expected to
    /// replace a previous one (for example, re-recording a named test case's trace).
    /// </summary>
    /// <param name="tracesDirectory">The directory to write the trace file into.</param>
    /// <param name="trace">The trace to persist.</param>
    /// <param name="name">
    /// The stable name to persist the trace under, without any file extension.
    /// </param>
    /// <returns>The full path of the file that was written.</returns>
    public static Task<string> SaveAsync(string tracesDirectory, RecordedTrace trace, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracesDirectory);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return SaveCoreAsync(tracesDirectory, trace, name, overwrite: true);
    }

    private static async Task<string> SaveCoreAsync(
        string tracesDirectory, RecordedTrace trace, string fileStem, bool overwrite)
    {
        Directory.CreateDirectory(tracesDirectory);

        var finalPath = Path.Combine(tracesDirectory, $"{fileStem}.json");

        if (!overwrite && File.Exists(finalPath))
        {
            throw new InvalidOperationException(
                $"A trace file already exists at '{finalPath}'. Trace files are immutable once persisted.");
        }

        // Write to a private temporary file first, then move it into place, so a reader
        // never observes a partially written trace file.
        var tempPath = Path.Combine(tracesDirectory, $"{fileStem}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, trace, TraceJson.FileOptions).ConfigureAwait(false);
            }

            // Without the overwrite flag, File.Move throws if finalPath already exists, so
            // a concurrent writer targeting the same anonymous trace ID can never silently
            // clobber an already-persisted trace. With it, a caller-named trace is
            // deliberately replaced, as documented on the overload that requested it.
            File.Move(tempPath, finalPath, overwrite);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        return finalPath;
    }

    /// <summary>
    /// Reloads a previously persisted <see cref="RecordedTrace"/> from a file path.
    /// </summary>
    /// <param name="filePath">The path of a trace file previously written by one of the <see cref="SaveAsync(string, RecordedTrace)"/> overloads.</param>
    public static async Task<RecordedTrace> LoadAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = File.OpenRead(filePath);

        var trace = await JsonSerializer.DeserializeAsync<RecordedTrace>(stream, TraceJson.FileOptions)
            .ConfigureAwait(false);

        return trace ?? throw new InvalidDataException($"Trace file '{filePath}' did not deserialize to a trace.");
    }
}
