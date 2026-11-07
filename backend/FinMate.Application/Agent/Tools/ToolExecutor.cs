using System.Text.Json;

namespace FinMate.Application.Agent;

/// <summary>
/// Chạy một công cụ theo kế hoạch của LLM, NHƯNG chỉ sau khi kiểm tên (danh sách cho phép) và từng tham số (tên lạ, thiếu, sai kiểu, ngoài khoảng).
/// Một tham số như <c>user_id</c> bị từ chối như mọi tham số lạ: danh tính chỉ đến từ <see cref="ToolContext"/>.
/// </summary>
public sealed class ToolExecutor(ToolRegistry registry)
{
    public ToolRegistry Registry => registry;

    public Task<ToolResult> ExecuteAsync(ToolContext context, string toolName, JsonElement args, CancellationToken ct = default)
    {
        if (!registry.TryGet(toolName, out var tool))
        {
            return Task.FromResult(ToolResult.Rejected("unknown_tool"));
        }

        var error = Validate(tool.Spec, args);
        return error is null ? tool.RunAsync(context, args, ct) : Task.FromResult(ToolResult.Rejected(error));
    }

    private static string? Validate(ToolSpec spec, JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            return "invalid_arguments";
        }

        foreach (var property in args.EnumerateObject())
        {
            if (spec.Args.All(a => a.Name != property.Name))
            {
                return $"unknown_argument:{property.Name}";
            }
        }

        foreach (var arg in spec.Args)
        {
            if (!args.TryGetProperty(arg.Name, out var value))
            {
                if (arg.Required)
                {
                    return $"missing_argument:{arg.Name}";
                }

                continue;
            }

            var valid = arg.Type switch
            {
                "int" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n >= arg.Min && n <= arg.Max,
                "string" => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString()!.Length <= 64,
                _ => false,
            };
            if (!valid)
            {
                return $"invalid_argument:{arg.Name}";
            }
        }

        return null;
    }
}
