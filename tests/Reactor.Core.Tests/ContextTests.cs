using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;

namespace Reactor.Core.Tests;

/// <summary>
/// Context（Provide / UseContext）回归。
/// 这一组正是之前"静默失效"的那一块：Provide 会写入 ContextValues，
/// 但 RenderContext 拿不到作用域，UseContext 永远返回默认值。
/// </summary>
internal static class ContextTests
{
    private static readonly Context<int> Size = new(42, nameof(Size));
    private static readonly Context<string> Theme = new("light", nameof(Theme));

    public static void Run()
    {
        Program.Section("ContextScope 基础");

        var scope = new ContextScope();
        var ctx = new RenderContext();

        ctx.BeginRender(scope);
        Program.Expect("没有提供者时返回默认值", 42, ctx.UseContext(Size));
        ctx.EndRender();

        var pushed = scope.Push(new Dictionary<ContextBase, object?> { [Size] = 7 });
        Program.Expect("Push 返回压入条数", 1, pushed);

        ctx.BeginRender(scope);
        Program.Expect("读到提供的值", 7, ctx.UseContext(Size));
        ctx.EndRender();

        Program.Section("遮蔽与弹栈");

        scope.Push(new Dictionary<ContextBase, object?> { [Size] = 9 });
        ctx.BeginRender(scope);
        Program.Expect("最近的提供者遮蔽祖先", 9, ctx.UseContext(Size));
        ctx.EndRender();

        scope.Pop(1);
        ctx.BeginRender(scope);
        Program.Expect("弹栈后回到上一层的值", 7, ctx.UseContext(Size));
        ctx.EndRender();

        scope.Pop(1);
        ctx.BeginRender(scope);
        Program.Expect("全部弹出后回到默认值", 42, ctx.UseContext(Size));
        ctx.EndRender();

        Program.Section("多个 Context 并存");

        scope.Push(new Dictionary<ContextBase, object?>
        {
            [Size] = 100,
            [Theme] = "dark",
        });

        ctx.BeginRender(scope);
        Program.Expect("同时读第一个 context", 100, ctx.UseContext(Size));
        Program.Expect("同时读第二个 context", "dark", ctx.UseContext(Theme));
        ctx.EndRender();

        scope.Pop(2);

        Program.Section("固化到组件节点（异步重渲染仍可读）");

        var traversalScope = new ContextScope();
        traversalScope.Push(new Dictionary<ContextBase, object?> { [Size] = 55 });

        // 组件节点各自持有一个 scope，在遍历期与协调器的 scope 同步一次
        var nodeScope = new ContextScope();
        nodeScope.ReplaceWith(traversalScope);

        var nodeCtx = new RenderContext();
        nodeCtx.BeginRender(nodeScope);
        Program.Expect("组件渲染时读到祖先提供的值", 55, nodeCtx.UseContext(Size));
        nodeCtx.EndRender();

        // 遍历结束后协调器作用域被清空（模拟离开元素），组件仍持有固化值
        traversalScope.Clear();
        nodeCtx.BeginRender(nodeScope);
        Program.Expect("离开元素后组件重渲染仍读到固化值", 55, nodeCtx.UseContext(Size));
        nodeCtx.EndRender();

        // 下一次 patch 时重新同步，反映最新的 Provide
        traversalScope.Push(new Dictionary<ContextBase, object?> { [Size] = 66 });
        nodeScope.ReplaceWith(traversalScope);
        nodeCtx.BeginRender(nodeScope);
        Program.Expect("重新同步后读到新值", 66, nodeCtx.UseContext(Size));
        nodeCtx.EndRender();

        Program.Section("未注入作用域时不应抛异常");

        var bareCtx = new RenderContext();
        bareCtx.BeginRender();
        Program.Expect("脱离宿主时退化为默认值", 42, bareCtx.UseContext(Size));
        bareCtx.EndRender();

        Program.Throws<InvalidOperationException>("渲染外调用 UseContext 抛异常", () => bareCtx.UseContext(Size));
    }
}
