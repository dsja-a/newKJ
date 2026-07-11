# Security Rules

Rules enforced during C# migration:

1. No API Key, JWT secret, or database password hardcoded in source.
2. No unauthenticated user access by default.
3. No path string `StartsWith` for directory safety checks.
4. No `CSharpScript.EvaluateAsync` for executing model-generated code.
5. No SQL generation results executed without validation.
6. No interface that allows arbitrary C# code execution in the main service process.
