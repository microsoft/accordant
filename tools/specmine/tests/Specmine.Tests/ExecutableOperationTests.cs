// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ExecutableOperationTests
{
    private sealed record EchoRequest(string Text);

    private sealed record EchoResponse(string Text);

    [Test]
    public async Task ExecuteAsync_UsesBoundExecutionAndExposesTypedDescription()
    {
        var operation = new ExecutableOperation<EchoRequest, EchoResponse>(
            "Echo",
            request => Task.FromResult(new EchoResponse(request.Text)));

        var response = await operation.ExecuteAsync(new EchoRequest("hello"));

        Assert.Multiple(() =>
        {
            Assert.That(operation.Name, Is.EqualTo("Echo"));
            Assert.That(operation.RequestType, Is.EqualTo(typeof(EchoRequest)));
            Assert.That(operation.ResponseType, Is.EqualTo(typeof(EchoResponse)));
            Assert.That(response.Text, Is.EqualTo("hello"));
        });
    }

    [Test]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() =>
                new ExecutableOperation<EchoRequest, EchoResponse>(
                    " ",
                    request => Task.FromResult(new EchoResponse(request.Text))));

            Assert.Throws<ArgumentNullException>(() =>
                new ExecutableOperation<EchoRequest, EchoResponse>("Echo", execute: null!));
        });
    }
}
