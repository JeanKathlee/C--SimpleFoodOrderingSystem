FROM node:22-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend-csharp/FoodJS.Api/FoodJS.Api.csproj backend-csharp/FoodJS.Api/
RUN dotnet restore backend-csharp/FoodJS.Api/FoodJS.Api.csproj
COPY backend-csharp/FoodJS.Api/ backend-csharp/FoodJS.Api/
RUN dotnet publish backend-csharp/FoodJS.Api/FoodJS.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /app/publish ./
COPY --from=frontend /src/frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FoodJS.Api.dll"]
