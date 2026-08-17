// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

/// <summary>
/// Resolves the absolute paths of the OpenAPI documents this test project ships under
/// <c>Fixtures/</c> - the three committed benchmark documents (linked in from their own
/// projects) and this project's own query/header/body/path and malformed-document
/// fixtures - relative to the test assembly's own output directory, so tests never depend
/// on the process's ambient current directory.
/// </summary>
internal static class OpenApiFixturePaths
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string TaskWorkflow => Path.Combine(FixturesDirectory, "TaskWorkflow.openapi.json");

    public static string PaymentProcessing => Path.Combine(FixturesDirectory, "PaymentProcessing.openapi.json");

    public static string InventoryReservation => Path.Combine(FixturesDirectory, "InventoryReservation.openapi.json");

    public static string Fixture => Path.Combine(FixturesDirectory, "fixture.openapi.json");

    public static string Malformed(string fileName) => Path.Combine(FixturesDirectory, "Malformed", fileName);
}