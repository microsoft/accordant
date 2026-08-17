// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.OpenApi.Tests;

using Microsoft.AspNetCore.Mvc.Testing;
using Specmine.Adapters.OpenApi;

/// <summary>
/// Starts a <see cref="WebApplicationFactory{TEntryPoint}"/> on a real, ephemeral Kestrel
/// listener rather than the in-memory <c>TestServer</c> the factory uses by default.
///
/// <see cref="OpenApiTargetAdapter"/> only ever accepts a <c>baseUrl</c> through its
/// public, adapter-owned JSON settings and always builds its own <see cref="HttpClient"/>
/// against it - there is no seam to hand it an in-memory <c>TestServer</c> handler without
/// widening that public contract. <see cref="WebApplicationFactory{TEntryPoint}"/> already
/// supports exactly this real-server scenario out of the box via <c>UseKestrel(int)</c>,
/// which starts the application under Kestrel on the given port (<c>0</c> for an
/// OS-assigned ephemeral port) and records the address it actually bound in
/// <see cref="WebApplicationFactory{TEntryPoint}.ClientOptions"/>; this helper just starts
/// the server and reads that address back out, so every test using it exercises the
/// adapter exactly as it would run against a real deployed target.
/// </summary>
internal static class RealHttpServer
{
    public static string Start<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory) where TEntryPoint : class
    {
        factory.UseKestrel(0);
        factory.StartServer();
        return factory.ClientOptions.BaseAddress!.ToString();
    }
}