# {{Name}} - Accordant Tests

This project contains model-based tests for the {{Name}} API using Accordant.

## Files

| File | Purpose |
|------|---------|
| `{{Name}}State.cs` | State tracked by the spec (what the system remembers) |
| `{{Name}}Spec.cs` | Expected behavior for each operation |
| `{{Name}}Tests.cs` | Test execution with sample inputs |
| `ApiResult.cs` | Helper type for API responses |

## Getting Started

1. **Configure your API endpoint** in `{{Name}}Tests.cs`:
   ```csharp
   _httpClient = new HttpClient { BaseAddress = new Uri("https://your-api-url") };
   ```

2. **Update HTTP helpers** to match your actual API routes.

3. **Run tests**:
   ```bash
   dotnet test
   ```

## Next Steps

- **Modify State**: Add properties that match your domain
- **Add Operations**: Define more API endpoints in the Spec
- **Expand Inputs**: Add more sample inputs to explore edge cases
- **Add Constraints**: Use `StateConstraint` to focus exploration

## Learn More

- [Accordant Documentation](https://github.com/your-org/Accordant/docs)
- [Quick Start](https://github.com/your-org/Accordant/docs/quickstart.md)
- [Full Example: BankAccount Sample](https://github.com/your-org/Accordant/Samples/BankAccount)
