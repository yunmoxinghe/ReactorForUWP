using System.Collections.Generic;

using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 面板重排前置判断的回归测试。
/// </summary>
/// <remarks>
/// 锁的是两条性质：
/// <list type="number">
/// <item><b>顺序没变时整段跳过重排</b>。以前每个下标都做一次 <c>IndexOfNative</c>
/// 线性扫描，而它放在 <c>for i</c> 的内层 —— 即使顺序一模一样也要扫 n 次，
/// n 个节点就是 n² 次引用比较（500 个节点一次重排 = 25 万次）。
/// 跳过还能顺带保住焦点：控件一次都没被 Remove/Insert。</item>
/// <item><b>顺序变了要从第一个不匹配处开工</b>。返回的下标决定了循环的起点，
/// 起点错了（比如恒返回 0）会退化成全量重排；起点太靠后则会漏掉错位。</item>
/// </list>
/// 这里是纯逻辑（引用比较），所以能在没有 XAML 运行时的控制台里断言。
/// </remarks>
internal static class ReorderTests
{
    public static void Run()
    {
        Program.Section("面板重排（同序快路径）");

        var a = new object();
        var b = new object();
        var c = new object();

        // 1) 完全同序：-1（可以整段跳过）
        var same = new List<object> { a, b, c };
        Program.Check(
            "顺序完全一致时返回 -1（跳过重排）",
            Reorder.FirstMismatch(same, new List<object> { a, b, c }) == -1);
        Program.Check(
            "IsSameOrder 与 -1 一致",
            Reorder.IsSameOrder(same, new List<object> { a, b, c }));

        // 2) 空列表也是"同序"（没有要动的）
        Program.Check(
            "两个空列表视为同序",
            Reorder.FirstMismatch(new List<object>(), new List<object>()) == -1);

        // 3) 交换：第一个不匹配的位置
        Program.Check(
            "首尾交换 → 不匹配在下标 0",
            Reorder.FirstMismatch(
                new List<object> { a, b, c },
                new List<object> { c, b, a }) == 0);
        Program.Check(
            "只有末尾两个交换 → 不匹配在下标 1",
            Reorder.FirstMismatch(
                new List<object> { a, b, c },
                new List<object> { a, c, b }) == 1);
        Program.Check(
            "只有最后一位不同 → 不匹配在最后一位",
            Reorder.FirstMismatch(
                new List<object> { a, b, c },
                new List<object> { a, b, a }) == 2);

        // 4) 前缀必须被跳过：这也是"从 firstMismatch 开始找目标"的前提
        Program.Check(
            "相同的前缀不会被算作不匹配（下标 2 才动）",
            Reorder.FirstMismatch(
                new List<object> { a, b, c },
                new List<object> { a, b, c }) == -1);

        // 5) 长度不一致 → 0（无法逐位对齐，只能整体重来）
        Program.Check(
            "长度不同时返回 0（整体重排）",
            Reorder.FirstMismatch(
                new List<object> { a, b },
                new List<object> { a, b, c }) == 0);
        Program.Check(
            "目标更短时也返回 0",
            Reorder.FirstMismatch(
                new List<object> { a, b, c },
                new List<object> { a }) == 0);

        // 6) 引用比较，不是值比较：两个"相等"的不同实例算不匹配
        Program.Check(
            "按引用比较（同值的不同实例算不匹配）",
            Reorder.FirstMismatch(
                new List<object> { "x" },
                new List<object> { new string('x', 1) }) == 0);
    }
}
