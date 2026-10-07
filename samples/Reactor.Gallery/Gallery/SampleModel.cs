using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery;

/// <summary>
/// 索引的数据形状。<b>三层：分类 → 控件条目 → 该条目的样例。</b>
/// </summary>
/// <remarks>
/// 这个层级刻意对齐 WinUI 3 Gallery 的 All → Control → Sample：
/// 一个控件条目下面可以有多个样例（例如 TextBox 分"非受控"与"受控"两种写法），
/// 而不是一个控件一个页面到底——后者会把"哪种场景下该用哪种写法"这件事
/// 从文档里抹掉。
/// </remarks>

/// <summary>一个样例：标题、说明、预览怎么建、源码在哪个文件。</summary>
/// <param name="Build">
/// 预览怎么造出来。<b>是委托而不是已经建好的元素</b>：元素树要在正确的组件上下文里
/// 构建（<c>UseState</c> 等钩子只对正在渲染的那个组件有效），
/// 预先建好的实例跨进另一个组件的渲染就不是同一棵树了。
/// </param>
/// <param name="SourcePath">
/// 仓库内的相对路径。屏幕上的代码块读的就是它，
/// 于是"看到的代码"与"跑起来的代码"天然同源。
/// </param>
internal sealed record SampleCase(
    string Title,
    string? Description,
    Func<Element> Build,
    string SourcePath);

/// <summary>
/// 一个控件条目。
/// </summary>
/// <remarks>
/// <see cref="Samples"/> 是<b>数组</b>而不是 <c>IReadOnlyList</c>：静态构造要按位回填
/// 每个样例的 <c>SourcePath</c>，数组才能索引写入。对外读取仍按只读序列用。
/// </remarks>
internal sealed record GalleryItem(
    string Id,
    string Title,
    string? Description,
    SampleCase[] Samples);

/// <summary>一个分类。</summary>
internal sealed record GalleryCategory(
    string Id,
    string Title,
    string Icon,
    string? Description,
    IReadOnlyList<GalleryItem> Items);

/// <summary>
/// 源码展示组件的 props：<c>Component&lt;TProps&gt;</c> 的传值形态。
/// </summary>
/// <remarks>类型是 public：<c>SampleCodePresenter</c> 是 public 类，
/// 出现在基类类型实参位置的 props 可访问性不能比它低。</remarks>
public sealed record CodePresenterProps(string Title, string SourcePath);
