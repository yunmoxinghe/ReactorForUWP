using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;

namespace Reactor.Core.Tests;

/// <summary>Hook 层语义回归（RenderContext 是纯逻辑，不需要 UI 线程）。</summary>
internal static class HookTests
{
    public static void Run()
    {
        Program.Section("UseState");

        var ctx = new RenderContext();
        var rerenders = 0;
        ctx.RequestRerender = () => rerenders++;
        ctx.IsMountedCheck = () => true;

        ctx.BeginRender();
        var (value, set) = ctx.UseState(1);
        ctx.EndRender();

        Program.Expect("初始值", 1, value);

        set(2);
        Program.Expect("值变化触发重渲染", 1, rerenders);

        set(2);
        Program.Expect("同值赋值不触发重渲染", 1, rerenders);

        ctx.BeginRender();
        var (kept, _) = ctx.UseState(1);
        ctx.EndRender();
        Program.Expect("跨渲染保持状态", 2, kept);

        // 卸载后 setter 应静默失败
        ctx.IsMountedCheck = () => false;
        set(99);
        Program.Expect("卸载后 setter 不再触发重渲染", 1, rerenders);
        ctx.IsMountedCheck = () => true;

        Program.Section("Hook 顺序校验");

        var orderCtx = new RenderContext();
        orderCtx.BeginRender();
        orderCtx.UseState(1);
        orderCtx.UseState(2);
        orderCtx.EndRender();

        Program.Throws<HookOrderException>("hook 数量减少时抛 HookOrderException", () =>
        {
            orderCtx.BeginRender();
            orderCtx.UseState(1);
            orderCtx.EndRender();
        });

        var typeCtx = new RenderContext();
        typeCtx.BeginRender();
        typeCtx.UseState(1);
        typeCtx.EndRender();

        Program.Throws<HookOrderException>("hook 类型不匹配时抛 HookOrderException", () =>
        {
            typeCtx.BeginRender();
            typeCtx.UseState("字符串");
            typeCtx.EndRender();
        });

        Program.Section("UseMemo / UseCallback");

        var memoCtx = new RenderContext();
        var calls = 0;

        memoCtx.BeginRender();
        var first = memoCtx.UseMemo(() => ++calls, "dep");
        memoCtx.EndRender();
        Program.Expect("首次计算", 1, first);

        memoCtx.BeginRender();
        var second = memoCtx.UseMemo(() => ++calls, "dep");
        memoCtx.EndRender();
        Program.Expect("依赖不变则复用缓存", 1, second);
        Program.Expect("依赖不变时不重新计算", 1, calls);

        memoCtx.BeginRender();
        var third = memoCtx.UseMemo(() => ++calls, "changed");
        memoCtx.EndRender();
        Program.Expect("依赖变化则重新计算", 2, third);

        var callbackCtx = new RenderContext();
        callbackCtx.BeginRender();
        var cb1 = callbackCtx.UseCallback(() => { }, "dep");
        callbackCtx.EndRender();

        callbackCtx.BeginRender();
        var cb2 = callbackCtx.UseCallback(() => { }, "dep");
        callbackCtx.EndRender();
        Program.Check("UseCallback 依赖不变时引用稳定", ReferenceEquals(cb1, cb2));

        callbackCtx.BeginRender();
        var cb3 = callbackCtx.UseCallback(() => { }, "changed");
        callbackCtx.EndRender();
        Program.Check("UseCallback 依赖变化时返回新委托", !ReferenceEquals(cb2, cb3));

        Program.Section("UseRef");

        var refCtx = new RenderContext();
        refCtx.BeginRender();
        var ref1 = refCtx.UseRef(0);
        refCtx.EndRender();

        ref1.Current = 7;

        refCtx.BeginRender();
        var ref2 = refCtx.UseRef(0);
        refCtx.EndRender();

        Program.Check("UseRef 跨渲染同一个实例", ReferenceEquals(ref1, ref2));
        Program.Expect("UseRef 修改可见", 7, ref2.Current);

        Program.Section("UseReducer");

        var reducerCtx = new RenderContext();
        var reducerRerenders = 0;
        reducerCtx.RequestRerender = () => reducerRerenders++;

        reducerCtx.BeginRender();
        var (state, update) = reducerCtx.UseReducer(0);
        reducerCtx.EndRender();

        update(x => x + 5);
        Program.Expect("函数式更新触发重渲染", 1, reducerRerenders);

        reducerCtx.BeginRender();
        var (stateAfter, _) = reducerCtx.UseReducer(0);
        reducerCtx.EndRender();
        Program.Expect("函数式更新结果", 5, stateAfter);

        var dispatchCtx = new RenderContext();
        dispatchCtx.BeginRender();
        var (count, dispatch) = dispatchCtx.UseReducer(
            (int current, string action) => action == "inc" ? current + 1 : current,
            0);
        dispatchCtx.EndRender();

        dispatch("inc");
        dispatch("inc");
        dispatch("noop");

        dispatchCtx.BeginRender();
        var (countAfter, _) = dispatchCtx.UseReducer(
            (int current, string action) => action == "inc" ? current + 1 : current,
            0);
        dispatchCtx.EndRender();
        Program.Expect("reducer dispatch 累计", 2, countAfter);

        Program.Section("UseEffect");

        var effectCtx = new RenderContext();
        var runs = 0;
        var cleanups = 0;

        effectCtx.BeginRender();
        effectCtx.UseEffect(() => { runs++; return () => cleanups++; }, "a");
        effectCtx.EndRender();
        Program.Expect("首次渲染执行 effect", 1, runs);

        effectCtx.BeginRender();
        effectCtx.UseEffect(() => { runs++; return () => cleanups++; }, "a");
        effectCtx.EndRender();
        Program.Expect("依赖不变不重复执行", 1, runs);

        effectCtx.BeginRender();
        effectCtx.UseEffect(() => { runs++; return () => cleanups++; }, "b");
        effectCtx.EndRender();
        Program.Expect("依赖变化重新执行", 2, runs);
        Program.Expect("重新执行前先跑 cleanup", 1, cleanups);

        effectCtx.RunCleanups();
        Program.Expect("RunCleanups 执行剩余 cleanup", 2, cleanups);

        Program.Section("Optional<T>");

        Program.Check("Unset 没有值", !Optional<int>.Unset.HasValue);
        Program.Expect("Of(x) 有值", 5, Optional<int>.Of(5).Value);
        Program.Check("显式 null 也算有值", Optional<string?>.Of(null).HasValue);
        Program.Expect("Unset 取默认值", 0, Optional<int>.Unset.GetValueOrDefault());
    }
}
