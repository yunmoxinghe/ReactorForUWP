using System;
using System.Collections.Generic;

namespace Reactor.Core.Tests;

/// <summary>
/// 极简测试运行器：无 NuGet 依赖，`dotnet run --project tests/Reactor.Core.Tests` 即可执行。
/// 退出码 0 = 全部通过，1 = 有失败。
/// </summary>
internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static int Main()
    {
        Console.WriteLine("Reactor 核心逻辑回归测试");
        Console.WriteLine(new string('-', 60));

        // 顺序即分组：核心 hook → 上下文 → 受控属性回声 → 虚拟化身份 →
        // 真控件映射（面包屑 ItemTemplate）→ 弱键表 → 本地化键名 →
        // 重渲染批处理 → 面板重排。新增分组往这里加一行即可。
        HookTests.Run();
        ContextTests.Run();
        EchoGuardTests.Run();
        ReadyArmTests.Run();
        EchoLifetimeTests.Run();
        DiagnosticsCountersTests.Run();
        VirtualListTests.Run();
        BreadcrumbTemplateTests.Run();
        WeakTableTests.Run();
        LocalizationKeysTests.Run();
        RenderBatcherTests.Run();
        ReorderTests.Run();
        SelectionGateTests.Run();
        ListViewSelectionTests.Run();
        ToggleEchoTests.Run();
        BreadcrumbItemsTests.Run();
        RebindEchoTests.Run();
        MountOrderTests.Run();
        RangeCoerceTests.Run();
        SiblingWriteTests.Run();
        RebuildEchoTests.Run();
        EchoContractTests.Run();
        GalleryIndexTests.Run();

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"通过 {_passed} 项，失败 {Failures.Count} 项。");

        if (Failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("失败项：");
            foreach (var failure in Failures)
            {
                Console.WriteLine($"  - {failure}");
            }
        }

        return Failures.Count == 0 ? 0 : 1;
    }

    internal static void Section(string name) => Console.WriteLine($"\n[{name}]");

    internal static void Check(string name, bool ok, string? detail = null)
    {
        if (ok)
        {
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        else
        {
            var message = detail is null ? name : $"{name} — {detail}";
            Failures.Add(message);
            Console.WriteLine($"  FAIL {message}");
        }
    }

    internal static void Expect<T>(string name, T expected, T actual)
    {
        var ok = EqualityComparer<T>.Default.Equals(expected, actual);
        Check(name, ok, ok ? null : $"期望 {expected}，实际 {actual}");
    }

    internal static void Throws<TException>(string name, Action action)
        where TException : Exception
    {
        try
        {
            action();
            Check(name, false, $"没有抛出 {typeof(TException).Name}");
        }
        catch (TException)
        {
            Check(name, true);
        }
        catch (Exception ex)
        {
            Check(name, false, $"抛出了 {ex.GetType().Name}，期望 {typeof(TException).Name}");
        }
    }
}
