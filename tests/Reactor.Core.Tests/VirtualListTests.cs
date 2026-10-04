using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 虚拟化列表<b>身份模型</b>的回归测试（key ≠ index）。
/// </summary>
/// <remarks>
/// 锁的是这条 bug：早先已挂载项按下标存在 <c>Dictionary&lt;int, ...&gt;</c> 里，
/// 数据源在头部插入一项时下标整体位移，已挂载的项全部拿错内容。
/// 这类错误滚一眼未必看得出（项等高、数量不变），但内容全错位。
/// <para>
/// 这里只测纯逻辑的 <see cref="VirtualKeys"/>，不需要 XAML 运行时。
/// </para>
/// </remarks>
internal static class VirtualListTests
{
    public static void Run()
    {
        Program.Section("虚拟化列表 / 身份解析");

        Func<object?, object?> byText = item => item as string;

        // 1) 没给选择器 → 退化为下标身份（旧行为）
        var plain = new List<object?> { "A", "B", "C" };
        var asIndex = VirtualKeys.Resolve(plain, 0, 2, null);
        Program.Check(
            "无选择器时身份 = 下标",
            (int)asIndex[0] == 0 && (int)asIndex[1] == 1 && (int)asIndex[2] == 2);

        // 2) 核心回归：头部插入后，已有项的身份不变，只是下标位移
        var before = new List<object?> { "A", "B", "C" };
        var after = new List<object?> { "X", "A", "B", "C" };
        var keysBefore = VirtualKeys.Resolve(before, 0, 2, byText);
        var keysAfter = VirtualKeys.Resolve(after, 0, 3, byText);

        Program.Expect("插入前 A 在下标 0，身份为 A", "A", (string)keysBefore[0]);
        Program.Expect("插入后 A 移到下标 1", "A", (string)keysAfter[1]);
        Program.Check(
            "A 的身份跨插入保持一致（已挂载控件可被复用）",
            Equals(keysBefore[0], keysAfter[1]));
        Program.Expect("新插入的 X 有自己的身份", "X", (string)keysAfter[0]);

        // 3) 重复 key 的那批退化为下标，避免互相抢占同一个挂载槽
        var duplicated = new List<object?> { "A", "A", "B" };
        var dupKeys = VirtualKeys.Resolve(duplicated, 0, 2, byText);
        Program.Check(
            "重复 key 退化为下标",
            (int)dupKeys[0] == 0 && (int)dupKeys[1] == 1);
        Program.Expect("不重复的项仍用 key", "B", (string)dupKeys[2]);

        // 4) 选择器返回 null → 该项退化为下标
        var nullSelector = new Func<object?, object?>(_ => null);
        var nullKeys = VirtualKeys.Resolve(plain, 0, 2, nullSelector);
        Program.Check("选择器返回 null 时退化为下标", (int)nullKeys[2] == 2);

        // 5) 边界：越界区间被裁剪，不抛异常
        var clamped = VirtualKeys.Resolve(plain, 0, 99, byText);
        Program.Expect("last 越界裁剪到 count-1", 3, clamped.Count);
        Program.Expect("空区间返回空表", 0, VirtualKeys.Resolve(plain, 5, 2, byText).Count);
    }
}
