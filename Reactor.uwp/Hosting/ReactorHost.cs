using System;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Internal;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Reactor.Uwp.Hosting;

/// <summary>
/// 承载一个根 <see cref="Component"/> 的宿主。
/// 负责：调用 Render()、把 Element 树交给 Reconciler、响应状态变化重渲染。
/// 注意：纯代码（无 XAML 编译器）场景下不能跨 ABI 继承 XAML 控件类型
/// （CsWinRT 不会生成内部接口聚合，虚方法回调会无限递归），
/// 因此这里采用组合：内部持有一个真实的 <see cref="ContentControl"/>。
/// </summary>
public sealed class ReactorHost
{
    private readonly Component _root;
    private readonly Reconciler _reconciler = new();
    private Element? _tree;
    private bool _renderQueued;

    public ReactorHost(Component root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _root.Context.RequestRerender = RequestRerender;

        Root = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        Rerender();
    }

    /// <summary>挂到窗口（或任意 XAML 容器）上的真实根控件。</summary>
    public ContentControl Root { get; }

    /// <summary>组件状态变化时请求重渲染（可在任意线程调用）。</summary>
    public void RequestRerender()
    {
        if (Root.Dispatcher.HasThreadAccess)
        {
            Rerender();
            return;
        }

        // 非 UI 线程：marshal 回 UI 线程并合并同一轮消息泵内的多次请求。
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        _ = Root.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            _renderQueued = false;
            Rerender();
        });
    }

    private void Rerender()
    {
        _root.BeginRender();
        var next = _root.Render();
        _root.EndRender();

        if (_tree is null || Root.Content is not UIElement native ||
            !Reconciler.CanPatch(_tree, next))
        {
            Root.Content = _reconciler.Build(next);
        }
        else
        {
            _reconciler.Patch(native, _tree, next);
        }

        _tree = next;
    }
}
