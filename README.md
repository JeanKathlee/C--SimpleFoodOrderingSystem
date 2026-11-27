# FoodJS

A full-stack food ordering platform with a React client and an ASP.NET Core
MVC/Web API backend.

![FoodJS entity relationship diagram](ERD_group4.png)

## Highlights

- Responsive menu, product customization, cart, checkout, and order history
- Live order tracking and in-app customer notifications
- JWT authentication with customer and administrator roles
- Admin analytics, orders, menu categories, customers, and coupons
- Google OAuth support when credentials are configured
- Entity Framework Core with SQLite persistence

## Tech stack

- **View:** React 19, React Router, Vite, Framer Motion, Leaflet
- **Backend:** ASP.NET Core controllers, C# services, and repositories
- **Model/data:** Entity Framework Core and SQLite
- **Tooling:** .NET 10 SDK, ESLint, npm, Docker

## Run locally

Requirements: Node.js 20+, npm, and the .NET 10 SDK.

```powershell
cd FoodJS
npm run install:all
```

Start the C# API:

```powershell
npm run dev:backend
```

In another terminal, start React:

```powershell
npm run dev:frontend
```

Open `http://localhost:5173`. The API runs at `http://localhost:3002`.

Development administrator: `admin@foodjs.demo` / `admin123`.

## Configuration

Configure secrets with environment variables or .NET user secrets. Never
commit real credentials.

```powershell
dotnet user-secrets set "Jwt:Secret" "replace-with-a-long-random-value" --project FoodJS/backend-csharp/FoodJS.Api/FoodJS.Api.csproj
dotnet user-secrets set "Google:ClientSecret" "your-google-client-secret" --project FoodJS/backend-csharp/FoodJS.Api/FoodJS.Api.csproj
```

Google OAuth must authorize `http://localhost:5173/auth/callback`.

## Project structure

```text
FoodJS/
├── frontend/         React views
└── backend-csharp/   ASP.NET Core API and tests
    └── FoodJS.Api/
        ├── Controllers/
        ├── Services/
        ├── Repositories/
        ├── Models/
        ├── Contracts/
        ├── Data/
        └── Migrations/
```

## Quality checks

```powershell
cd FoodJS
npm run lint --prefix frontend
npm run build
dotnet test backend-csharp/FoodJS.Api.Tests/FoodJS.Api.Tests.csproj -c Release
```

## Deployment

The included Dockerfile builds React, publishes ASP.NET Core, and serves both
from one process. The Render Blueprint uses that container. Configure a strong
JWT secret and bootstrap administrator credentials in the deployment service.
SQLite requires persistent storage for durable production data.

## License

MIT
