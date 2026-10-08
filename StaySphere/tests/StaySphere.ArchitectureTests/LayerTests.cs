using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;

namespace StaySphere.ArchitectureTests;

/// <summary>Enforces Clean Architecture boundaries so the modular monolith cannot erode over time.</summary>
public sealed class LayerTests
{
    private static readonly Assembly Domain = typeof(StaySphere.Domain.Common.Entity).Assembly;
    private static readonly Assembly Application = typeof(StaySphere.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(StaySphere.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_but_the_BCL()
    {
        var result = Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("StaySphere.Application", "StaySphere.Infrastructure", "StaySphere.Api", "StaySphere.Contracts",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Azure")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_web_or_cloud_sdks()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("StaySphere.Infrastructure", "StaySphere.Api", "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Http",
                "Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.Data.SqlClient", "Azure", "StackExchange.Redis", "OllamaSharp")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Controllers_do_not_touch_the_database_or_infrastructure()
    {
        var result = Types.InAssembly(Api).That().Inherit(typeof(ControllerBase)).ShouldNot()
            .HaveDependencyOnAny("StaySphere.Infrastructure", "Microsoft.EntityFrameworkCore", "StaySphere.Application.Abstractions.IAppDbContext")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Controllers_are_sealed_and_end_with_Controller()
    {
        var result = Types.InAssembly(Api).That().Inherit(typeof(ControllerBase)).And().AreNotAbstract()
            .Should().BeSealed().And().HaveNameEndingWith("Controller").GetResult();
        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Ai_tools_only_use_application_services_never_the_infrastructure()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace("StaySphere.Application.Ai").ShouldNot()
            .HaveDependencyOnAny("StaySphere.Infrastructure", "Microsoft.Data.SqlClient")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("StaySphere.Api").GetResult();
        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    private static string Failing(TestResult result) =>
        "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
