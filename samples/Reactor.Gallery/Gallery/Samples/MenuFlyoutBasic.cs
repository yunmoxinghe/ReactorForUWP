using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 浮出菜单：<c>MenuFlyout</c> 与它的几种项。
/// </summary>
/// <remarks>
/// 官方那页的每一种项各来一份：<b>普通项</b>（带图标）、<b>分隔线</b>、
/// <b>禁用项</b>、<b>可勾选项</b>（复选 / 单选）、<b>子菜单</b>（可再嵌一层）。
/// <list type="bullet">
///   <item><c>MenuFlyout</c> <b>不是控件</b>：它继承 <c>FlyoutBase</c>，
///         不进可视树，因此也走不了协调器那条路（见 <c>Handlers.Menus.cs</c>）——
///         它是"挂在别人身上的部件"，声明式里就是一份描述，由宿主就地物化。</item>
///   <item><b>挂在哪儿</b>是另一件事：按钮的 <c>Flyout</c> 槽位（见
///         <c>DropDownButton</c> / <c>SplitButton</c>）、或任意元素的
///         <c>.ContextMenu(...)</c>（右键 / 长按，对应 XAML 的
///         <c>&lt;UIElement.ContextFlyout&gt;</c>）。</item>
///   <item>菜单项里的键盘提示串（<c>acceleratorText</c>）只是<b>一行字</b>：
///         官方的 <c>KeyboardAcceleratorTextOverride</c> 并不真的帮你按下那个键，
///         键位要另外用 <c>.KeyboardAccelerator(...)</c> 挂上去。
///         不挂键就写提示，等于告诉用户一个按不动的快捷键。</item>
///   <item>可勾选项官方自己就分在两处：<c>ToggleMenuFlyoutItem</c> 在
///         <c>Windows.UI.Xaml.Controls</c>（UWP 原生），
///         <c>RadioMenuFlyoutItem</c> 在 <c>Microsoft.UI.Xaml.Controls</c>（WinUI 2），
///         <b>且后者不继承前者</b>（实测互相赋值编译不过）。两者都<b>只有
///         <c>Click</c> 一个回执通道</b>——没有 <c>Checked</c> / <c>Unchecked</c>，
///         值是回读出来的，所以"我们写的"与"用户点的"仍要靠回声抑制分。</item>
///   <item><c>RadioMenuFlyoutItem</c> 的 <c>GroupName</c> 只管同组互斥、<b>不管选中</b>，
///         所以会<b>逐项</b>回调过来（被取消的那一条也来一次 <c>false</c>）——
///         下面的收口只认 <c>true</c> 那一发。</item>
///   <item>子菜单 <c>MenuFlyoutSubItem</c> 官方继承的是 <c>MenuFlyoutItemBase</c>
///         <b>而不是</b> <c>MenuFlyoutItem</c>：所以它有 <c>Text</c> / <c>Icon</c> /
///         <c>IsEnabled</c>，却没有 <c>KeyboardAcceleratorTextOverride</c>
///         ——元素上也就没这个参数（给了不生效的旋钮比不给更糟）。
///         它收的是与菜单同一个类型，因此能一层层往下嵌。</item>
/// </list>
/// </remarks>
public sealed class MenuFlyoutBasic : Component
{
    public override Element Render()
    {
        var (last, setLast) = UseState("（还没点过）");
        var (wordWrap, setWordWrap) = UseState(false);
        var (spellCheck, setSpellCheck) = UseState(true);
        var (align, setAlign) = UseState("左");
        var (sort, setSort) = UseState("名称");
        var (size, setSize) = UseState("中");

        void Report(string what) => setLast(what);

        var menu = MenuFlyout(
            MenuItem("复制", FontIcon("\uE8C8"), () => Report("复制")),
            MenuItem("剪切", FontIcon("\uE8C6"), () => Report("剪切")),
            MenuItem("粘贴", FontIcon("\uE77F"), () => Report("粘贴"), acceleratorText: "Ctrl+V"),
            MenuSeparator(),
            MenuItem("重命名", FontIcon("\uE8AC"), () => Report("重命名")),
            MenuItem("删除", FontIcon("\uE74D"), () => Report("删除"), acceleratorText: "Del"),
            MenuSeparator(),

            // 可勾选项：官方 ToggleMenuFlyoutItem 没有 Checked / Unchecked，
            // 回执只有 Click —— 值是回读出来的，见元素上的注释。
            ToggleMenuItem("自动换行", isChecked: wordWrap, onIsCheckedChanged: v =>
            {
                setWordWrap(v);
                Report($"自动换行 → {v}");
            }),
            ToggleMenuItem("拼写检查", isChecked: spellCheck, onIsCheckedChanged: v =>
            {
                setSpellCheck(v);
                Report($"拼写检查 → {v}");
            }),
            MenuSeparator(),

            // 单选：官方只管"同组互斥"，不管选中——所以会逐项回调过来，
            // 这里只认 true 那一发（其余被控件改成 false 的会被忽略）。
            RadioMenuItem("左对齐", isChecked: align == "左", groupName: "align",
                onIsCheckedChanged: v => Pick(v, "左")),
            RadioMenuItem("居中", isChecked: align == "中", groupName: "align",
                onIsCheckedChanged: v => Pick(v, "中")),
            RadioMenuItem("右对齐", isChecked: align == "右", groupName: "align",
                onIsCheckedChanged: v => Pick(v, "右")),
            MenuSeparator(),

            // 第二组单选：groupName 换了，于是与上面那组各管各的——
            // 官方 RadioMenuFlyoutItem 的互斥范围就是这一个组名。
            RadioMenuItem("小图标", isChecked: size == "小", groupName: "size",
                onIsCheckedChanged: v => PickSize(v, "小")),
            RadioMenuItem("中图标", isChecked: size == "中", groupName: "size",
                onIsCheckedChanged: v => PickSize(v, "中")),
            RadioMenuItem("大图标", isChecked: size == "大", groupName: "size",
                onIsCheckedChanged: v => PickSize(v, "大")),
            MenuSeparator(),

            // 子菜单：一项，展开又是一份菜单。官方 MenuFlyoutSubItem 收的同样是
            // MenuFlyoutItemBase，所以里面能再嵌一层（下面那个「更多」就是第二层）。
            SubMenuItem("排序方式", FontIcon("\uE8CB"),
                RadioMenuItem("按名称", isChecked: sort == "名称", groupName: "sort",
                    onIsCheckedChanged: v => PickSort(v, "名称")),
                RadioMenuItem("按日期", isChecked: sort == "日期", groupName: "sort",
                    onIsCheckedChanged: v => PickSort(v, "日期")),
                MenuSeparator(),
                SubMenuItem("更多",
                    MenuItem("升序", onClick: () => Report("升序")),
                    MenuItem("降序", onClick: () => Report("降序")))),

            MenuSeparator(),

            MenuItem("这一项是禁用的", isEnabled: false));

        // 单选那三条共用一个收口：被取消的那两条也会来回调（false），只认选中的。
        void Pick(bool picked, string name)
        {
            if (!picked)
            {
                return;
            }

            setAlign(name);
            Report($"对齐 → {name}");
        }

        // 第二组单选同一个收口：同样只认 true 那一发。
        void PickSize(bool picked, string name)
        {
            if (!picked)
            {
                return;
            }

            setSize(name);
            Report($"图标 → {name}");
        }

        // 子菜单里那一组单选与外面那组同一个收口：被取消的那条也会来回调（false）。
        void PickSort(bool picked, string name)
        {
            if (!picked)
            {
                return;
            }

            setSort(name);
            Report($"排序 → {name}");
        }

        return VStack(14,
            TextBlock("菜单").Body(),

            // 挂在哪儿有两个入口：按钮的 Flyout 槽位（点开）与元素的 ContextMenu（右键）。
            // 这里是后者；前者见 DropDownButton / SplitButton 那两条。
            Border(
                    TextBlock("在这块区域上点右键（或长按）").Body())
                .Padding(16)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"))
                // 声明式右键菜单（对应 XAML 的 <UIElement.ContextFlyout><MenuFlyout>）；
                // 菜单内容能跟着 state 走，不用自己管实例生命周期。
                .ContextMenu(menu),

            TextBlock($"最后一次动作：{last}").Caption().Subtle(),
            TextBlock($"自动换行 {wordWrap} / 拼写检查 {spellCheck} / 对齐 {align} / 排序 {sort}")
                .Caption()
                .Subtle(),

            TextBlock("菜单项里的图标是 FontIcon / BitmapIcon，收到官方 MenuFlyoutItem.Icon 上；"
                      + "分隔线是 MenuSeparator。菜单本身不在可视树里，所以它是"
                      + "「挂在别人身上的部件」，不是一个能独立站位的控件。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("可勾选项有两个「官方自己就分在两处」的类型："
                      + "ToggleMenuFlyoutItem 在 Windows.UI.Xaml.Controls（UWP 原生），"
                      + "RadioMenuFlyoutItem 在 Microsoft.UI.Xaml.Controls（WinUI 2），"
                      + "而且后者并不继承前者 —— 本库照抄这个划分，不顺手统一。"
                      + "两者都只有 Click 一个回执通道，值是回读出来的。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("子菜单（MenuFlyoutSubItem）官方继承的是 MenuFlyoutItemBase "
                      + "而不是 MenuFlyoutItem —— 所以它有 Text / Icon / IsEnabled，"
                      + "却没有 KeyboardAcceleratorTextOverride（元素上也就没这个参数）。"
                      + "它收的是与菜单同一个类型，因此能一层层往下嵌。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
