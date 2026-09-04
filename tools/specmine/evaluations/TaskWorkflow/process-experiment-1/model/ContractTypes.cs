using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Accordant;

namespace TaskWorkflow.ProcessExperiment1.Model;

internal static class ContractJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    public static JsonSerializerOptions IndentedSerializerOptions { get; } = CreateIndentedSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonSerializerOptions CreateIndentedSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

internal sealed record CreateTaskCall(
    [property: JsonPropertyName("body")] CreateTaskBody? Body);

internal sealed record CreateTaskBody(
    [property: JsonPropertyName("title")] string? Title);

internal sealed record TaskByIdCall(
    [property: JsonPropertyName("path")] TaskPath? Path);

internal sealed record TaskPath(
    [property: JsonPropertyName("id")] string? Id);

internal sealed record ApiCallResponse(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("body")] JsonElement Body);

internal sealed record TaskDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status);

internal sealed record ErrorDocument(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

internal sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status);

internal sealed record TaskSnapshot(string Id, string Title, string Status);

internal static class TaskStatuses
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Canceled = "canceled";
}

internal static class ResponseReaders
{
    public static TaskSnapshot ReadTask(JsonElement body)
    {
        var document = body.Deserialize<TaskDocument>(ContractJson.SerializerOptions)
            ?? throw new InvalidOperationException("Response body did not deserialize into a task.");

        return new TaskSnapshot(document.Id, document.Title, document.Status);
    }

    public static ErrorDocument ReadError(JsonElement body) =>
        body.Deserialize<ErrorDocument>(ContractJson.SerializerOptions)
        ?? throw new InvalidOperationException("Response body did not deserialize into an error payload.");

    public static ApiCallResponse MockTaskResponse(string id, string title, string status, int responseStatus) =>
        new(
            responseStatus,
            JsonSerializer.SerializeToElement(
                new TaskDocument(id, title, status),
                ContractJson.SerializerOptions));
}

internal static class ResponseChecks
{
    private const string InvalidTransitionMessage =
        "The requested task transition conflicts with its current status.";

    public static ValidationResult ValidateCreatedTask(
        ApiCallResponse response,
        TaskWorkflowState state,
        string expectedTitle)
    {
        if (response.Status != 201)
        {
            return ValidationResult.Invalid($"Expected HTTP 201 but received {response.Status}.");
        }

        if (!TryReadTask(response.Body, out var task, out var error))
        {
            return ValidationResult.Invalid(error);
        }

        if (!Guid.TryParse(task.Id, out _))
        {
            return ValidationResult.Invalid($"Expected a GUID task id but received '{task.Id}'.");
        }

        if (!string.Equals(task.Title, expectedTitle, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected echoed title '{expectedTitle}' but received '{task.Title}'.");
        }

        if (!string.Equals(task.Status, TaskStatuses.Pending, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected new task status '{TaskStatuses.Pending}' but received '{task.Status}'.");
        }

        if (state.TryGetTask(task.Id, out _))
        {
            return ValidationResult.Invalid(
                $"Expected CreateTask to return a fresh id, but '{task.Id}' already exists in model state.");
        }

        return ValidationResult.Valid();
    }

    public static ValidationResult ValidateTaskRepresentation(
        ApiCallResponse response,
        TaskSnapshot expected,
        int expectedStatus = 200)
    {
        if (response.Status != expectedStatus)
        {
            return ValidationResult.Invalid(
                $"Expected HTTP {expectedStatus} but received {response.Status}.");
        }

        if (!TryReadTask(response.Body, out var task, out var error))
        {
            return ValidationResult.Invalid(error);
        }

        if (!string.Equals(task.Id, expected.Id, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected task id '{expected.Id}' but received '{task.Id}'.");
        }

        if (!string.Equals(task.Title, expected.Title, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected title '{expected.Title}' but received '{task.Title}'.");
        }

        if (!string.Equals(task.Status, expected.Status, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected status '{expected.Status}' but received '{task.Status}'.");
        }

        return ValidationResult.Valid();
    }

    public static ValidationResult ValidateBlankTitle(ApiCallResponse response) =>
        ValidateError(response, 400, "validation_error", "Title must not be blank.");

    public static ValidationResult ValidateNotFound(ApiCallResponse response, string id) =>
        ValidateError(response, 404, "not_found", $"Task '{id}' was not found.");

    public static ValidationResult ValidateInvalidTransition(ApiCallResponse response) =>
        ValidateError(response, 409, "invalid_transition", InvalidTransitionMessage);

    private static ValidationResult ValidateError(
        ApiCallResponse response,
        int expectedStatus,
        string expectedCode,
        string expectedMessage)
    {
        if (response.Status != expectedStatus)
        {
            return ValidationResult.Invalid(
                $"Expected HTTP {expectedStatus} but received {response.Status}.");
        }

        if (!TryReadError(response.Body, out var error, out var message))
        {
            return ValidationResult.Invalid(message);
        }

        if (!string.Equals(error.Code, expectedCode, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid($"Expected error code '{expectedCode}' but received '{error.Code}'.");
        }

        if (!string.Equals(error.Message, expectedMessage, StringComparison.Ordinal))
        {
            return ValidationResult.Invalid(
                $"Expected error message '{expectedMessage}' but received '{error.Message}'.");
        }

        return ValidationResult.Valid();
    }

    private static bool TryReadTask(JsonElement body, out TaskSnapshot task, out string error)
    {
        try
        {
            task = ResponseReaders.ReadTask(body);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            task = default!;
            error = $"Expected a task payload but failed to parse it: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadError(JsonElement body, out ErrorDocument errorDocument, out string error)
    {
        try
        {
            errorDocument = ResponseReaders.ReadError(body);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorDocument = default!;
            error = $"Expected an error payload but failed to parse it: {ex.Message}";
            return false;
        }
    }
}
