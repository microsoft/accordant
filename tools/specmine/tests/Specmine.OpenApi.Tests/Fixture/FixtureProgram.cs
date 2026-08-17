// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests.Fixture;

/// <summary>
/// A tiny, test-only minimal API matching <c>Fixtures/fixture.openapi.json</c>: exercises
/// path, query, header, and JSON request body parameter mapping end to end, none of which
/// the three committed benchmark documents happen to combine in one operation. It has no
/// behavioral rules worth hiding - it just echoes what it was sent - so, unlike the
/// benchmark APIs, it is not a black-box ground truth and its logic is fine to read
/// directly from the adapter's own tests.
/// </summary>
public sealed record AnnotateRequestBody(string? Note);

public sealed record AnnotateResponseBody(
    string Id, int Priority, bool? Verbose, string TraceId, string? OptionalTag, string Note);

public sealed record FixtureErrorResponse(string Message);

public partial class FixtureProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapPost("/widgets/{id}/annotate", (
                string id,
                int priority,
                bool? verbose,
                HttpRequest httpRequest,
                AnnotateRequestBody body) =>
            {
                if (priority < 0)
                {
                    return Results.BadRequest(new FixtureErrorResponse("priority must not be negative."));
                }

                var traceId = httpRequest.Headers["X-Trace-Id"].ToString();
                var optionalTag = httpRequest.Headers.TryGetValue("X-Optional-Tag", out var tagValues)
                    ? tagValues.ToString()
                    : null;

                return Results.Ok(new AnnotateResponseBody(
                    id, priority, verbose, traceId, optionalTag, body.Note ?? string.Empty));
            })
            .WithName("AnnotateWidget");

        app.MapPost("/widgets/{id}/touch", (string id) => Results.NoContent())
            .WithName("TouchWidget");

        app.Run();
    }
}