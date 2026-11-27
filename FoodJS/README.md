# FoodJS

FoodJS uses a React view with an ASP.NET Core MVC/Web API backend. Business
logic lives in C# services, repositories handle database access, Entity Framework Core maps the models, and SQLite
stores application data.

## Project folders

- `frontend/` - React, React Router, and Vite views
- `backend-csharp/FoodJS.Api/` - ASP.NET Core controllers, services, repositories, models, and EF Core data access
- `backend-csharp/FoodJS.Api.Tests/` - API and business-rule regression tests

## Run locally

Requirements: Node.js 20+, npm, and the .NET 10 SDK.

```powershell
cd "C:\Users\MY PC\Documents\foodJS\FoodJS"
npm run install:all
```

Start the C# API:

```powershell
npm run dev:backend
```

In a second terminal, start React:

```powershell
npm run dev:frontend
```

Open `http://localhost:5173`. Vite sends every `/api` request to the C# API at
`http://localhost:3002`.

Development accounts:

- Admin: `admin@foodjs.demo` / `admin123`
- Customer: `customer@foodjs.demo` / `customer123`

## Architecture

```text
React views -> ASP.NET Core controllers -> C# services -> Repositories -> EF Core / SQLite
```

The C# project uses separate `Controllers/`, `Services/`, `Repositories/`,
`Models/`, and `Contracts/` folders. Controllers handle HTTP concerns; services
own business rules; repositories own database queries and persistence; models
represent persisted entities. Contracts define request and response data.

The old Express backend and migration adapter have been removed from the active
project. Their source and local data are preserved in the ignored
`.legacy-backups/express-20261125-1fb1078e/` folder at the repository root.

See [the backend guide](backend-csharp/README.md) for the feature mapping, PR integration details, retained-record behavior, and architecture tests.

## Configuration

Development uses safe local defaults. Configure secrets with environment
variables or .NET user secrets; do not commit them.

```powershell
dotnet user-secrets set "Jwt:Secret" "replace-with-a-long-random-secret" --project backend-csharp/FoodJS.Api/FoodJS.Api.csproj
dotnet user-secrets set "Google:ClientSecret" "your-google-client-secret" --project backend-csharp/FoodJS.Api/FoodJS.Api.csproj
```

Google OAuth must authorize this redirect URI:

```text
http://localhost:5173/auth/callback
```

## Verification

```powershell
npm run build
npm run lint --prefix frontend
dotnet test backend-csharp/FoodJS.Api.Tests/FoodJS.Api.Tests.csproj -c Release
```

## Production

The included `Dockerfile` builds React, publishes ASP.NET Core, and serves the
compiled React application from the C# process. Production must provide a
strong `Jwt__Secret` and initial `BootstrapAdmin__Email` and
`BootstrapAdmin__Password` values. Use persistent storage or a managed database
before treating the deployment as durable.
