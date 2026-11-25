using System.Reflection;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Architecture;

/// <summary>
/// Phase 91 (BR-EXP-14): nhân viên không tạo hay tải gói dữ liệu thay khách. Khoá lại bằng cách cấm mọi controller vận hành/quản trị phụ thuộc vào
/// bất kỳ kiểu nào của <c>FinMate.Application.DataExport</c>; chỉ <see cref="ExportsController"/> (của chính user) được dùng.
/// </summary>
public class DataExportStaffBoundaryTests
{
    [Fact]
    public void NoOpsOrAdminController_DependsOnTheDataExportServices()
    {
        var controllers = typeof(ExportsController).Assembly.GetTypes()
            .Where(t => t.Namespace is { } ns && (ns.Contains(".Controllers.Ops") || ns.Contains(".Controllers.Admin")) && !t.IsAbstract);

        var offenders = controllers
            .SelectMany(c => c.GetConstructors().SelectMany(ctor => ctor.GetParameters())
                .Concat(c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters()))
                .Where(p => p.ParameterType.Namespace?.StartsWith("FinMate.Application.DataExport", StringComparison.Ordinal) == true)
                .Select(p => $"{c.Name}.{p.Name}"))
            .ToList();

        controllers.Should().NotBeEmpty();
        offenders.Should().BeEmpty();
    }

    [Fact]
    public void TheExportController_IsNotGatedByASubscription()
    {
        typeof(ExportsController).GetCustomAttributes(inherit: true).Select(a => a.GetType().Name)
            .Concat(typeof(ExportsController).GetMethods().SelectMany(m => m.GetCustomAttributes(inherit: true)).Select(a => a.GetType().Name))
            .Should().NotContain(n => n.Contains("Subscription"), "BR-EXP-13: quyền theo luật, không phải tính năng bán");
    }
}
