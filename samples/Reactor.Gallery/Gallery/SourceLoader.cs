using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Reactor.Gallery;

/// <summary>
/// 示例源码读取：从<b>嵌入到包里的那份 .cs 文件</b>读。
/// </summary>
/// <remarks>
/// <b>为什么不内联字符串。</b>源码展示有一条很容易被忽略的硬要求：屏幕上那段代码
/// 必须<b>就是</b>正在跑的那份。手写一份常量来做展示，头一次是对的，之后每改一次
/// 示例就得多记一件事；漏一次，画廊就开始骗人——而这恰恰是它最不该出错的地方
/// （它的全部价值就是"可以信"）。
/// <para>
/// 所以这里从 <c>EmbeddedResource</c> 读：<c>Reactor.Gallery.csproj</c> 把
/// <c>Gallery/Samples/**/*.cs</c> 嵌进了程序集，运行时按相对路径取出来。
/// 这条路在 AOT 下是安全的——<c>GetManifestResourceNames</c> 读的是程序集清单里的
/// 资源表，不需要反射类型，也不参与剪裁。
/// </para>
/// <para>
/// <b>读不到时要给退路，不能抛。</b>找不到资源返回 <c>null</c>，由调用处显示
/// "源码未随包一起构建"的提示。展示源码不是关键路径：因为它把整个示例页带崩，
/// 比缺一块代码块糟糕得多。
/// </para>
/// </remarks>
internal static class SourceLoader
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.Ordinal);

    private static string[]? _names;

    /// <summary>包里全部嵌入资源的名字（读一次，之后只用这份快照）。</summary>
    private static string[] Names => _names ??= typeof(SourceLoader).Assembly.GetManifestResourceNames();

    /// <summary>
    /// 按仓库里的相对路径读源码，例如 <c>"Gallery/Samples/ButtonBasic.cs"</c>。
    /// </summary>
    /// <returns>文件内容；读不到返回 null。</returns>
    /// <remarks>
    /// <b>匹配按后缀而不是全名。</b>资源名的前缀是 <c>RootNamespace</c>
    /// （<c>Reactor.Gallery</c>）拼出来的，且目录分隔符变成点；写死全名的话，
    /// 改一次命名空间或目录就全线失效。相对路径换成点之后做后缀匹配既能命中，
    /// 又不受前缀影响。
    /// </remarks>
    public static string? Load(string relativePath)
    {
        if (Cache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var dotted = relativePath.Replace('/', '.').Replace('\\', '.');
        string? resource = null;

        foreach (var name in Names)
        {
            if (name.EndsWith(dotted, StringComparison.Ordinal))
            {
                resource = name;
                break;
            }
        }

        string? text = null;

        if (resource is not null)
        {
            try
            {
                using var stream = typeof(SourceLoader).Assembly.GetManifestResourceStream(resource);
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    text = reader.ReadToEnd();
                }
            }
            catch (Exception)
            {
                // 读失败与"包里没有这个文件"对调用方是同一个结果，统一按取不到处理。
                text = null;
            }
        }

        Cache[relativePath] = text;
        return text;
    }
}
