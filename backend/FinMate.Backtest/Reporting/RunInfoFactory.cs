using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;

namespace FinMate.Backtest.Reporting;

/// <summary>Thông tin tái lập: commit (đọc thẳng <c>.git</c>, không cần lệnh git), SDK/runtime, băm tham số (S4).</summary>
public static class RunInfoFactory
{
    private const int CommitLength = 12;
    private const int HashLength = 16;
    private const string Unknown = "không rõ";

    public static RunInfo Create(RunOptions options, int worlds, int rows)
        => new(
            Commit(), Runtime(), GeneratorSpec.Version, options.RootSeed, options.SeedsPerPersona, options.StepDays, options.ConfigVersion,
            ConfigHash(options), Application.Forecasting.ForecastMethods.Current, worlds, rows);

    public static string ConfigHash(RunOptions options)
    {
        var json = JsonSerializer.Serialize(options.Settings);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..HashLength].ToLowerInvariant();
    }

    private static string Runtime()
    {
        var sdk = Environment.GetEnvironmentVariable("DOTNET_SDK_VERSION");
        return $"SDK {sdk ?? Unknown}, {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSArchitecture}";
    }

    private static string Commit()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git))
            {
                return Resolve(git) ?? Unknown;
            }
        }

        return Unknown;
    }

    private static string? Resolve(string git)
    {
        var head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
        {
            return Short(head);
        }

        var reference = head[5..];
        var loose = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loose))
        {
            return Short(File.ReadAllText(loose).Trim());
        }

        var packed = Path.Combine(git, "packed-refs");
        return File.Exists(packed)
            ? File.ReadLines(packed).Where(l => l.EndsWith(" " + reference, StringComparison.Ordinal)).Select(l => Short(l.Split(' ')[0])).FirstOrDefault()
            : null;
    }

    private static string Short(string sha) => sha.Length > CommitLength ? sha[..CommitLength] : sha;
}
