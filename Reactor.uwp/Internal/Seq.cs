using System;
using System.Collections.Generic;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 序列比较。<b>只有一个方法是有意的</b>：它必须不依赖 <c>Windows.UI.Xaml</c>，
/// 好让 <c>tests/Reactor.Core.Tests</c>（net10.0，链不到 UWP 程序集）能把它
/// <c>&lt;Compile Link&gt;</c> 进去，直接断言<b>真代码</b>而不是抄一份。
/// </summary>
/// <remarks>
/// <b>为什么要从 <c>PropWriter</c> 里挪出来。</b><c>PropWriter</c> 顶部
/// <c>using Windows.UI.Xaml;</c>，整个文件在 net10 上编译不过；而这一段是纯逻辑。
/// 于是判据被复制一份去测试 = 两份实现各自漂移，测的就不是产品代码了。
/// 现在 <c>PropWriter.SequenceEqual</c> 只剩一行转发，<b>事实来源就这一处</b>。
/// <para>
/// <b>它同时也是面包屑下发的门槛</b>（<c>BreadcrumbBarHandler.Update</c>）：
/// 内容没变就不换数据源——换引用会让内部 <c>ItemsRepeater</c> 把条目全拆重建，
/// 每轮渲染都换等于永远在重建。
/// </para>
/// </remarks>
internal static class Seq
{
    /// <summary>两个只读序列是否内容相同（元素逐个用默认比较器比）。</summary>
    public static bool SequenceEqual<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        a ??= Array.Empty<T>();
        b ??= Array.Empty<T>();

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
