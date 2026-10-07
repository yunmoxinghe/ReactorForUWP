using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 命令与菜单：<c>DropDownButton</c> / <c>SplitButton</c> / <c>MenuFlyout</c> /
/// <c>MenuFlyoutSubItem</c>（子菜单）。
/// </summary>
/// <remarks>
/// 三个控件对应官方三种不同的"点一下会怎样"：
/// <list type="bullet">
///   <item><c>DropDownButton</c>：<b>没有点击回调</b>。按下去就是展开菜单，
///         动作由菜单里的项承担——所以它连 <c>Click</c> 事件都不提供。</item>
///   <item><c>SplitButton</c>：<b>左半边</b>直接执行（<c>Click</c>），
///         <b>右半边</b>展开菜单。适合"有一个默认动作、另外几个是变体"
///         （保存 / 另存为 / 导出）。</item>
///   <item><c>MenuFlyout</c>：<b>不是控件</b>，是挂在别人身上的浮出层。
///         它不进可视树，所以也走不了协调器那条路（见
///         <c>Internal/Handlers.Menus.cs</c>）。</item>
///   <item><c>MenuFlyoutSubItem</c>：一项，展开又是一份菜单（可再嵌一层）。
///         它<b>没有点击回调</b>——点它是"展开"不是"执行"。</item>
/// </list>
/// 菜单项里的键盘提示串（<c>AcceleratorText</c>）只是<b>一行字</b>：
/// 官方的 <c>KeyboardAcceleratorTextOverride</c> 并不真的帮你按下那个键，
/// 键位要另外用 <c>.KeyboardAccelerator(...)</c> 挂上去。这里只写文案，
/// 键位由外壳统一处理——不挂键就写提示，等于告诉用户一个按不动的快捷键。
/// </remarks>
public sealed class MenusBasic : Component
{
    public override Element Render()
    {
        var (last, setLast) = UseState("（还没点过）");
        var (wordWrap, setWordWrap) = UseState(false);
        var (spellCheck, setSpellCheck) = UseState(true);
        var (align, setAlign) = UseState("左");
        var (sort, setSort) = UseState("名称");

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

            HStack(12,
                DropDownButton("编辑…", menu),
                SplitButton("保存", () => Report("保存（默认动作）"), MenuFlyout(
                    MenuItem("另存为…", FontIcon("\uE792"), () => Report("另存为")),
                    MenuItem("导出为 PDF", FontIcon("\uE7B5"), () => Report("导出为 PDF")),
                    MenuSeparator(),
                    MenuItem("发布到云端", FontIcon("\uE753"), () => Report("发布到云端"))))),

            TextBlock($"最后一次动作：{last}").Caption().Subtle(),
            TextBlock($"自动换行 {wordWrap} / 拼写检查 {spellCheck} / 对齐 {align} / 排序 {sort}")
                .Caption()
                .Subtle(),

            Border(
                TextBlock("在这块区域上点右键").Body())
                .Padding(16)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"))
                // 声明式右键菜单（对应 XAML 的 <UIElement.ContextFlyout><MenuFlyout>）；
                // 菜单内容能跟着 state 走，不用自己管实例生命周期。
                .ContextMenu(MenuFlyout(
                    MenuItem("刷新", FontIcon("\uE72C"), () => Report("刷新")),
                    MenuItem("属性…", FontIcon("\uE946"), () => Report("属性")))),

            TextBlock("菜单项里的图标是 FontIcon / BitmapIcon，收到官方 MenuFlyoutItem.Icon 上；" +
                      "分隔线是 MenuSeparator。菜单本身不在可视树里，所以它是「挂在按钮身上的部件」，" +
                      "不是一个能独立站位的控件。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("可勾选项有两个「官方自己就分在两处」的类型：" +
                      "ToggleMenuFlyoutItem 在 Windows.UI.Xaml.Controls（UWP 原生），" +
                      "RadioMenuFlyoutItem 在 Microsoft.UI.Xaml.Controls（WinUI 2），" +
                      "而且后者并不继承前者 —— 本库照抄这个划分，不顺手统一。" +
                      "两者都只有 Click 一个回执通道，值是回读出来的。")
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
