using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using System.Reflection;
using FoodJS.Api.Repositories;
using Microsoft.AspNetCore.Mvc;
using Xunit;
namespace FoodJS.Api.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly Assembly Application = typeof(CartController).Assembly;

    [Fact]
    public void Every_controller_depends_only_on_feature_services()
    {
        var controllers = Application.GetTypes().Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)).ToList();
        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        foreach (var dependency in controller.GetConstructors().SelectMany(c => c.GetParameters()))
        {
            Assert.StartsWith("FoodJS.Api.Services", dependency.ParameterType.Namespace ?? "");
            Assert.EndsWith("Service", dependency.ParameterType.Name);
        }
    }

    [Fact]
    public void Services_and_seeders_do_not_reference_EF_or_expose_deferred_database_queries()
    {
        // Include pure rules, seeders, and compiler-generated service helpers too.
        var services = Application.GetTypes().Where(t => t.Namespace == "FoodJS.Api.Services");
        foreach (var service in services)
        {
            var dependencies = service.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
                .Concat(service.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(f => f.FieldType));
            foreach (var dependency in dependencies)
            {
                Assert.DoesNotContain("EntityFrameworkCore", dependency.FullName ?? "");
                Assert.DoesNotContain("FoodJsDbContext", dependency.Name);
                Assert.False(typeof(IQueryable).IsAssignableFrom(dependency));
            }
        }
    }

    [Fact]
    public void Repositories_do_not_depend_on_controllers_services_or_HTTP_contracts()
    {
        var repositories = Application.GetTypes().Where(t => t.Namespace == "FoodJS.Api.Repositories");
        foreach (var repository in repositories)
        {
            var dependencies = repository.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
                .Concat(repository.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(f => f.FieldType))
                .Concat(repository.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));
            foreach (var dependency in dependencies.SelectMany(ExpandTypes))
            {
                var ns = dependency.Namespace ?? "";
                Assert.False(ns.StartsWith("FoodJS.Api.Controllers") || ns.StartsWith("FoodJS.Api.Services")
                    || ns.StartsWith("FoodJS.Api.Contracts") || ns.StartsWith("Microsoft.AspNetCore.Mvc"),
                    repository.Name + " must not depend on " + dependency.FullName);
            }
        }
    }

    [Fact]
    public void Only_persistence_components_can_receive_or_store_the_database_context()
    {
        foreach (var type in Application.GetTypes())
        {
            var dependencies = type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
                .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(f => f.FieldType));
            if (!dependencies.SelectMany(ExpandTypes).Any(t => t == typeof(FoodJS.Api.Data.FoodJsDbContext))) continue;
            Assert.True(type.Namespace is "FoodJS.Api.Repositories" or "FoodJS.Api.Data",
                type.FullName + " bypasses the repository boundary.");
        }
    }

    private static IEnumerable<Type> ExpandTypes(Type type)
    {
        yield return type;
        if (type.HasElementType)
            foreach (var element in ExpandTypes(type.GetElementType()!)) yield return element;
        foreach (var argument in type.GetGenericArguments())
            foreach (var nested in ExpandTypes(argument)) yield return nested;
    }

    [Fact]
    public void Repository_contracts_return_materialized_records_and_abstract_transactions()
    {
        var contracts = Application.GetTypes().Where(t => t.IsInterface && t.Namespace == typeof(IMenuRepository).Namespace);
        foreach (var contract in contracts)
        foreach (var method in contract.GetMethods())
        {
            Assert.False(ContainsPersistenceType(method.ReturnType), contract.Name + "." + method.Name);
            Assert.All(method.GetParameters(), p => Assert.False(ContainsPersistenceType(p.ParameterType)));
        }
    }

    private static bool ContainsPersistenceType(Type type) =>
        typeof(IQueryable).IsAssignableFrom(type) || type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true
        || type.GetGenericArguments().Any(ContainsPersistenceType);

    [Fact]
    public void Controller_and_service_source_files_contain_no_database_access()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "FoodJS.Api", "Services"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var api = Path.Combine(folder!.FullName, "FoodJS.Api");
        var paths = Directory.GetFiles(Path.Combine(api, "Services"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(api, "Controllers"), "*.cs"));
        foreach (var path in paths)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("using Microsoft.EntityFrameworkCore", source);
            Assert.DoesNotContain("FoodJsDbContext", source);
            Assert.DoesNotContain("IQueryable<", source);
            Assert.DoesNotContain("SaveChanges", source);
            Assert.DoesNotContain("FromSql", source);
        }
    }
}
