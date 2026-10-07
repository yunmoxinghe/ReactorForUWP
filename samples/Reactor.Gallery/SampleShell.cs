using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Gallery.Pages;
using Windows.System;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery;

/// <summary>
/// 外壳：<c>NavigationView</c>（左侧菜单 + 顶部搜索框）+ <c>Frame</c>（内容区）。
/// </summary>
/// <remarks>
/// 这一层的职责只有三件：<b>持有导航状态</b>、<b>持有应用设置</b>、
/// <b>把它俩分发下去</b>。页面长什么样不归它管。
/// <para>
/// <b>为什么页面切换一定走 <c>Frame</c>。</b>直接换 Content 也能用，但官方的页面过渡
/// 只由 <c>Frame.Navigate</c> / <c>GoBack</c> 驱动——更重要的是它们本来就是两条路径：
/// 返回有独立的过渡方向和一个专属的返回音效。之前一律用 Navigate 模拟返回，
/// 等于把"返回"做成"进入下一页"，观感和声音都对不上。
/// 所以这里给 <c>Frame</c> 传 <c>stackDepth</c>，由它来决定这一次该走哪条路。
/// </para>
/// <para>
/// <b>搜索框挂在 <c>NavigationView.AutoSuggestBox</c> 上</b>，而不是自己拼一个 header：
/// 官方对这个属性有专门处理（包括但不限于折叠成图标条时自动收起），
/// 复刻它意味着要自己去盯 <c>DisplayMode</c> 的变化。
/// </para>
/// </remarks>
public sealed class SampleShell : Component
{
    /// <summary>当前落到哪一页。</summary>
    private enum View
    {
        Home,
        Browse,
        Item,
        Search,
        Settings,
    }

    /// <summary>一次导航要去哪儿。<b>record 是为了能值相等</b>：同值不引发子页面重渲染。</summary>
    private sealed record Route(View View = View.Home, string? CategoryId = null, string? ItemId = null, string? Query = null);

    /// <summary>
    /// 起始栈：只有首页一层。
    /// </summary>
    /// <remarks>
    /// 用 <c>static readonly</c> 而不是每帧 <c>new[]{ new Route() }</c>：后者每帧都造一个
    /// 新数组，而数组按引用比较——于是"当前栈"永远不等于上次那个，每帧都判定成变了。
    /// </remarks>
    private static readonly Route[] RootStack = { new() };

    /// <summary>左侧菜单里的一项。</summary>
    private sealed record Entry(string Label, string Icon, Route Route);

    private static readonly IReadOnlyList<Entry> Menu = BuildMenu();

    private static readonly NavigationViewItemData[] MenuItems =
        Menu.Select(e => new NavigationViewItemData(e.Label, e.Icon)).ToArray();

    public override Element Render()
    {
        // ── 导航栈 ────────────────────────────────────────────────────────
        // 存的是"整条历史"而不是"当前一页"：返回要能回到<b>你来的那一页</b>，
        // 而不是写死的某个目的地。之前 Back() 一律回 Browse，于是
        // 「首页 → 搜索 → 条目」按返回会掉到"全部示例"，而不是搜索结果页——
        // 历史里根本没记这一层，观感是"返回键在乱跳"。
        // 栈深同时就是 Frame 的 stackDepth（见类注释）：变浅 = GoBack，变深 = Navigate。
        var (stack, setStack) = UseState(RootStack);
        var route = stack[^1];

        // 这些回调被当作 props 传给页面组件。props 是 record，按结构相等比较，
        // 而 lambda 每次重渲染都是新引用——好在只有下面这几种情况会让外壳重渲染：
        // 切菜单、敲搜索字、改设置。前两种本来就要换页，第三种数据量极小，
        // 因此这里不额外做回调稳定化（为此引入一个 Made-up 的 API 不值得）。
        void OpenBrowse() => Push(new Route(View.Browse));

        void OpenCategory(string categoryId) => Push(new Route(View.Browse, categoryId));

        void OpenItem(string itemId) => Push(new Route(View.Item, null, itemId));

        void Back() => Pop();

        /// <summary>
        /// 进栈。<b>落到与当前相同的一页时不进</b>——否则连点同一个菜单项五次，
        /// 返回键就要按五次才出得去，而用户看到的是"页面没变化，返回却按不动"。
        /// </summary>
        void Push(Route target)
        {
            if (stack[^1] == target)
            {
                return;
            }

            var next = new Route[stack.Length + 1];
            Array.Copy(stack, next, stack.Length);
            next[^1] = target;
            setStack(next);
        }

        /// <summary>出栈。已在根层就没有可返回的了（此时返回按钮本来也是禁用的）。</summary>
        void Pop()
        {
            if (stack.Length <= 1)
            {
                return;
            }

            var next = new Route[stack.Length - 1];
            Array.Copy(stack, next, next.Length);
            setStack(next);
        }

        var (query, setQuery) = UseState(string.Empty);

        // 候选列表要留着<b>条目本身</b>（不只是标题）：点候选是要直接进那一项的详情页，
        // 而 <c>SuggestionChosen</c> 只把字符串送回来，靠文本去反查会撞上"两个条目同名"。
        var hits = UseMemo(() => SampleIndex.Search(query).Take(8).ToArray(), query);
        var suggestions = UseMemo(() => hits.Select(i => i.Title).ToArray(), hits);

        // 官方选中候选后会<b>再抛一次</b> QuerySubmitted（ChosenSuggestion 非空）。
        // 想做到"点候选直接详情页、回车才看结果列表"，就得认出这一发并跳过。
        // 用 Ref 而不是 State：回调里读不到当帧刚 set 的 State，Ref 是同一个箱子。
        var picked = UseRef<string?>(null);

        void OpenByTitle(string title)
        {
            var hit = hits.FirstOrDefault(i => string.Equals(i.Title, title, StringComparison.Ordinal));
            if (hit is null)
            {
                return;
            }

            picked.Current = title;
            setQuery(title);
            Push(new Route(View.Item, null, hit.Id));
        }

        // Ctrl+F 要能把焦点送到搜索框，但"焦点"是控件状态、不是可写属性——
        // 所以拿一个令牌当边沿信号：按一次 +1，下一帧送到焦点，之后不再抢。
        // （WinUI 3 Gallery 的 Ctrl+F 就是这么一回事，只是它写在代码后置里。）
        var (searchFocus, setSearchFocus) = UseState(0);

        // ── 应用设置（主题 / 音效 / 自动更新）────────────────────────────────
        // 初值走 UseMemo → UseState(seed)，而不是直接 UseState(LoadTheme())：
        // 后者的实参每帧都会求值，而状态只在挂载那一帧认它——于是每帧白读三次
        // LocalSettings（跨 COM 调用），产物当场丢弃。
        var themeSeed = UseMemo(SettingsStore.LoadTheme);
        var autoUpdateSeed = UseMemo(SettingsStore.LoadAutoUpdate);
        var soundSeed = UseMemo(SettingsStore.LoadSound);

        var (theme, setTheme) = UseState(themeSeed);
        var (autoUpdate, setAutoUpdate) = UseState(autoUpdateSeed);
        var (sound, setSound) = UseState(soundSeed);

        UseEffect(() => SoundService.Apply(sound), sound);

        var settings = UseMemo(
            () => new AppSettings
            {
                Theme = theme,
                AutoUpdate = autoUpdate,
                Sound = sound,
                SetTheme = Persist(setTheme, SettingsStore.SaveTheme),
                SetAutoUpdate = Persist(setAutoUpdate, SettingsStore.SaveAutoUpdate),
                SetSound = Persist(setSound, SettingsStore.SaveSound),
            },
            theme, autoUpdate, sound);

        // 页面栈深度：1 = 顶层页（首页 / 浏览）；2 = 从浏览钻进去的一层（条目 / 搜索结果）。
        // 给 Frame 是为了让它知道这一次该走 Navigate 还是 GoBack（见类注释）。
        var stackDepth = stack.Length;

        return NavigationView(
                content: Frame(
                    ContentOf(route, OpenBrowse, OpenCategory, OpenItem, Back),
                    stackDepth: stackDepth),
                menuItems: MenuItems,
                paneDisplayMode: NavPaneDisplayMode.Left,
                selectedIndex: SelectedIndexOf(route),
                onSelectedIndexChanged: index =>
                {
                    if (index >= 0 && index < Menu.Count)
                    {
                        Push(Menu[index].Route);
                    }
                },
                onItemInvoked: index =>
                {
                    // -1 = 官方那个 Settings 项（IsSettingsVisible 默认开着的那个），
                    // 它不在 MenuItems 里，所以只能通过 ItemInvoked 认出来。
                    if (index < 0)
                    {
                        Push(new Route(View.Settings));
                    }
                },
                onBackRequested: Back,
                isBackButtonVisible: true,
                isBackEnabled: stackDepth > 1,
                header: "Reactor.Uwp 示例画廊",
                searchBox: AutoSuggestBox(
                    Optional<string>.Of(query),
                    suggestions,
                    placeholderText: "搜索示例（Ctrl+F 直接过来，回车看命中）",
                    onTextChanged: setQuery,
                    onSuggestionChosen: OpenByTitle,
                    onQuerySubmitted: text =>
                    {
                        setQuery(text);

                        // 这一发是"刚点过候选"带出来的，目的地已经在 SuggestionChosen 里定过了。
                        if (picked.Current is { } justPicked &&
                            string.Equals(justPicked, text, StringComparison.Ordinal))
                        {
                            picked.Current = null;
                            return;
                        }

                        Push(new Route(View.Search, null, null, text));
                    })
                    .AutomationName("搜索示例")
                    .AutomationAcceleratorKey("Ctrl+F")
                    .FocusToken(searchFocus))
            .KeyboardAccelerator(
                VirtualKey.F, VirtualKeyModifiers.Control,
                () => setSearchFocus(searchFocus + 1))
            .KeyboardAccelerator(
                VirtualKey.Left, VirtualKeyModifiers.Menu,
                Back, isEnabled: stackDepth > 1)
            .Provide(SettingsStore.Settings, settings)
            .Theme(ToElementTheme(theme));
    }

    /// <summary>当前 route 对应的内容页。</summary>
    /// <remarks>
    /// 传进来的都是外壳的导航入口；页面只管"要往哪儿去"，不知道路由本身的样子。
    /// </remarks>
    private static Element ContentOf(
        Route route,
        Action toBrowse,
        Action<string> openCategory,
        Action<string> openItem,
        Action back) =>
        route.View switch
        {
            View.Browse => Component<BrowsePage, BrowsePageProps>(
                new BrowsePageProps(route.CategoryId, openItem)),
            View.Item => Component<ItemPage, ItemPageProps>(
                new ItemPageProps(route.ItemId ?? string.Empty, back)),
            View.Search => Component<SearchPage, SearchPageProps>(
                new SearchPageProps(route.Query ?? string.Empty, openItem, back)),
            View.Settings => Component<AppSettingsPage>(),
            _ => Component<HomePage, HomePageProps>(new HomePageProps(toBrowse, openCategory)),
        };

    /// <summary>把"更新界面"与"写盘"串成一个动作：<b>先写盘再 setState</b>。</summary>
    /// <remarks>
    /// 顺序反了的话，万一 setState 触发的重渲染里出异常，盘上还是旧值、
    /// 界面却是新的——重启之后两边对不上，而且没有任何痕迹可查。
    /// </remarks>
    private static Action<T> Persist<T>(Action<T> setState, Action<T> save) =>
        value =>
        {
            save(value);
            setState(value);
        };

    /// <summary>
    /// 左侧菜单：<b>前两项固定</b>（首页 / 全部示例），接着是按分类，
    /// 最后把最常被翻出来的「受控控件诊断」也提到这儿放着。
    /// </summary>
    /// <remarks>
    /// 「写法指南」这个分类不进菜单：它有九项，全展开会把导航拉长一倍，
    /// 而它适合从"浏览"页按主题翻（那里能看到完整的分类名与描述）。
    /// </remarks>
    private static IReadOnlyList<Entry> BuildMenu()
    {
        var entries = new List<Entry>
        {
            new("首页", "\uE80F", new Route()),
            new("全部示例", "\uE8FD", new Route(View.Browse)),
        };

        foreach (var category in SampleIndex.MenuCategories)
        {
            entries.Add(new Entry(category.Title, category.Icon, new Route(View.Browse, category.Id)));
        }

        entries.Add(new Entry("受控控件诊断", "\uE9E9", new Route(View.Item, null, "diagnostics")));
        return entries;
    }

    /// <summary>
    /// 当前 route 应该选中菜单里的哪一项。
    /// </summary>
    /// <remarks>
    /// 钻进条目页时保持所在分类的选中态（而不是跳回"首页"）—— winui gallery 也是
    /// 这么处理的：菜单告诉用户"你在哪个区"，返回键负责往回走。
    /// </remarks>
    private static int SelectedIndexOf(Route route)
    {
        switch (route.View)
        {
            case View.Home:
                return 0;

            case View.Browse:
                var index = string.IsNullOrEmpty(route.CategoryId)
                    ? 1
                    : Menu.ToList().FindIndex(e => e.Route.CategoryId == route.CategoryId);
                return index < 0 ? 1 : index;

            case View.Item:
                var hit = Menu.ToList().FindIndex(e => e.Route.ItemId == route.ItemId);
                return hit < 0 ? 1 : hit;

            case View.Search:
                return 1;

            case View.Settings:
                // 设置是官方那个 Settings 项，不在 MenuItems 里 → 菜单此时不该有选中项。
                // 传 -1（Selector 认可的"清空选中"）比沿用上一次的下标诚实。
                return -1;

            default:
                return 0;
        }
    }

    /// <summary>0 跟随系统 / 1 浅色 / 2 深色。</summary>
    private static ElementTheme ToElementTheme(int theme) => theme switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
