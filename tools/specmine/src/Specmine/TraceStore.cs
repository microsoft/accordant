// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine;

using System.Text.Json;

/// <summary>
/// Persists and reloads <see cref="RecordedTrace"/> snapshots as single indented JSON
/// files. A trace file is written once and is never overwritten afterward.
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
    public static async Task<string> SaveAsync(string tracesDirectory, RecordedTrace trace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracesDirectory);
        ArgumentNullException.ThrowIfNull(trace);

        Directory.CreateDirectory(tracesDirectory);

        var finalPath = Path.Combine(tracesDirectory, $"{trace.TraceId:N}.json");

        if (File.Exists(finalPath))
        {
            throw new InvalidOperationException(
                $"A trace file already exists at '{finalPath}'. Trace files are immutable once persisted.");
        }

        // Write to a private temporary file first, then move it into place, so a reader
        // never observes a partially written trace file.
        var tempPath = Path.Combine(tracesDirectory, $"{trace.TraceId:N}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, trace, TraceJson.FileOptions).ConfigureAwait(false);
            }

            // File.Move without the overwrite flag throws if finalPath already exists,
            // so a concurrent writer targeting the same trace ID can never silently
            // clobber an already-persisted trace.
            File.Move(tempPath, finalPath);
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
    /// <param name="filePath">The path of a trace file previously written by <see cref="SaveAsync"/>.</param>
    public static async Task<RecordedTrace> LoadAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = File.OpenRead(filePath);

        var trace = await JsonSerializer.DeserializeAsync<RecordedTrace>(stream, TraceJson.FileOptions)
            .ConfigureAwait(false);

        return trace ?? throw new InvalidDataException($"Trace file '{filePath}' did not deserialize to a trace.");
    }
}
