# Code Refactor To-Do List

Complete each step independently. **Test and commit the changes after each step before proceeding to the next step.** Do not combine steps into a single commit.

## 1. Organize Classes ✅ (`faf71c1`)

Organize controllers, models, services, and other applicable classes into common category folders such as `Admin`, `AI`, `Auth`, etc.

* Keep category folders consistent across the project structure. For example, if `Controllers/Admin` exists, use corresponding `Models/Admin`, `Services/Admin`, etc. where applicable.
* When a class could reasonably belong to multiple categories, place it in the **most specific** category.

  * Example: `AiSettingsController` could be considered both `Admin` and `AI`; place it under `AI`.
* Do not create a new category for a single class unless the category represents a meaningful, reusable area of the application.
* Preserve the existing architecture and namespace conventions when moving files.
* Update all references, namespaces, and project configuration as necessary.

## 2. Review Class and Method Names

Review class, method, and function names for clarity and conciseness.

* Prefer names that communicate the purpose of the code at a glance.
* Avoid vague names that require opening the implementation to understand what they do.
* Prefer the simplest name that accurately describes the responsibility.
* Identify duplicate or inconsistent terminology and make naming consistent.
* Do not make names unnecessarily verbose simply to describe every detail of an operation.

Examples:

* `MeController` is too vague; rename it to clearly communicate what it manages.
* `AuthenticationProvidersController` and `AuthProvidersController` appear to represent the same concept; determine the appropriate terminology and use it consistently.

Do not rename something merely for stylistic preference. The new name should be objectively clearer or more consistent.

## 3. Make Endpoint Routes Explicit

Update all controller actions so that each endpoint explicitly defines its complete relative URL.

* Do not rely on controller-level `[Route]` attributes.
* Do not rely on implicit route conventions.
* Define the route directly on each controller action.
* Continue following the established `/api/{Controller}/{Action}` route convention unless there is an existing, intentional exception.
* Preserve the existing HTTP verbs and endpoint behavior.
* Update callers, tests, and documentation if a route changes.

## 4. Simplify Comments and Documentation

Review existing comments and XML documentation.

* Remove comments that merely describe what the code obviously does.
* Rewrite useful comments to be concise and focused on **why**, rather than **what**.
* Remove excessive explanations, implementation details, and unnecessary historical context.
* Keep XML documentation short, properly formatted, and useful to a human reader.
* Do not remove documentation that communicates important public API behavior or non-obvious constraints.

## 5. Apply the Coding Standards

Review the refactored code against the newly established coding standards and correct violations.

Focus particularly on:

* One type per file.
* Consistent curly-brace usage.
* Clear and concise naming.
* Simple, readable methods.
* Avoiding unnecessary abstractions.
* Appropriate separation of database access and business logic.
* Human-readable comments and documentation.
* Consistency with the existing project architecture.

Do not introduce unrelated changes or refactor working code solely for the sake of making additional changes.

## Process

For **each step**:

1. Review the relevant code and determine the required changes.
2. Make only the changes belonging to that step.
3. Build and run the appropriate tests.
4. Fix any issues caused by the changes.
5. Review the resulting diff for unintended changes.
6. Commit the completed step with a clear commit message.
7. Only then proceed to the next step.

If an ambiguity is encountered, make the smallest reasonable decision that preserves the existing architecture and conventions. Do not stop for clarification unless the decision would materially change application behavior or architecture.
