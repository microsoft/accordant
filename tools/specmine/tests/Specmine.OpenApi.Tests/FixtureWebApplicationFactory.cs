// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Specmine.OpenApi.Tests.Fixture;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> resolves a content root for
/// its entry point type by looking for a solution-relative directory named
/// after the entry point assembly. That lookup assumes the entry point lives in its own
/// project; here it is <see cref="FixtureProgram"/>, defined directly inside this test
/// assembly (per the ASP.NET Core "app and test in the same project" pattern), so the
/// assumed path (a top-level "Specmine.OpenApi.Tests" directory under the solution root)
/// does not exist. This factory overrides the content root to the test assembly's own
/// output directory, which is all the fixture API needs since it has no static content.
/// </summary>
internal sealed class FixtureWebApplicationFactory : WebApplicationFactory<FixtureProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);
    }
}