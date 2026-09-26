using System;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 组件节点：维护组件实例、hook 上下文、当前 Element 树与原生控件根。
/// 层级关系固定为 ComponentElement → ComponentNode → Instance → Element tree → Native tree。
/// </summary>
internal sealed class ComponentNode
{
    /// <summary>组件类型（用于 key 匹配）。</summary>
    public required Type ComponentType { get; init; }

    /// <summary>组件 props（用于 ShouldUpdate 判断）。</summary>
    public object? Props { get; set; }

    /// <summary>组件实例（持有状态与渲染逻辑）。</summary>
    public required Component Instance { get; init; }

    /// <summary>组件的 hook 上下文。</summary>
    public RenderContext Context => Instance.Context;

    /// <summary>当前渲染产出的 Element 树（用于 diff）。</summary>
    public Element? CurrentElement { get; set; }

    /// <summary>
    /// 本组件渲染时生效的 Context 作用域（祖先 Provide 的固化快照）。
    /// 遍历期与协调器的作用域同步一次，之后即使异步重渲染也能读到祖先提供的值。
    /// </summary>
    public ContextScope ContextScope { get; } = new();

    /// <summary>原生控件根（Border 锚点）。类型为 object 以保持 Core 层平台无关。</summary>
    public object? NativeRoot { get; set; }

    /// <summary>是否仍挂载（卸载后 setter 不再触发重渲染）。</summary>
    public bool IsMounted { get; set; } = true;
}
