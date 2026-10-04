---
name: uwp-runtime-evidence-debugging
description: UWP/WinUI 排查律令——先拿运行时证据再下结论。用于"值不对/图不出来/控件显示异常"这类查了三轮还在猜的问题，以及任何你准备说"这是平台限制/改不了"的时刻。源自 2026-10 本仓库「应用名显示成『声音』+ 图标小到看不见」事故，AI 连续三轮误判的完整复盘。
agent_created: true
---

# 先拿证据，再下结论

> 本 skill 不是知识库，是**事故复盘**。
> 背景：本仓库模板页出现「应用标题和关于卡片都显示『声音』」「图标小于鼠标指针」，
> AI 连续三轮给出诊断，**三轮全错**，第四轮靠运行时证据才定案。
> 下面先复原那三次误判，再抽出可复用的律令。

---

## 一、事故复原

### 现象

- 窗口标题栏 / 关于卡片标题：显示 **`声音`**（应该是 `Reactor 模板`）
- 「关于」卡片的应用图标：小到几乎看不见

### 三轮误判（每轮听起来都对）

| 轮 | 我下的结论 | 错在哪 |
|---|---|---|
| 1 | 「`SettingsCardHeaderIconMaxSize` = 20，是原版硬上限，要改得偏离原版」 | **拿 GitHub `main` 源码当"已安装版本"的证据**。装的是 `SettingsControls 8.2.251219`，其包里**根本没有这个键**（二进制搜也搜不到）。main 分支有 ≠ 你装的有 |
| 1 | 「`BitmapIcon` 要显式给 `.Width(20).Height(20)`」 | 这是**自己制造的新 bug**。`BitmapIcon` 不像 `Image` 那样把位图缩放适配 —— 设尺寸等于把 106px 原图**裁**出一个角，看着就是"图标缩没了"，于是下一轮继续往尺寸方向钻 |
| 2 | 「清单 `<Logo>Assets\StoreLogo.png</Logo>` 的反斜杠混进 URI，静默加载失败」 | 方向对（URI 确实要规范化），但**具体形态猜错了**：`Package.Current.Logo` 交出来的是 `file:///<安装目录>/Assets/SmallTile.scale-150.png`，**不带反斜杠**，而且是 SmallTile 不是 StoreLogo |
| 3 | 「是 OS 层 `ms-resource` 解析串了，C# 改不动，只能清部署缓存」 | **过早投降**。真实原因是清单里**两个 DisplayName 走不同的解析链**，只有一条坏了。改坏的那条就行，好的那条（外壳/开始菜单）保留 `ms-resource:` 照旧本地化 |

### 真正的根因（两条独立的链，不是一个 bug）

```
uap:VisualElements/@DisplayName = ms-resource:AppDisplayName
   → 外壳 / 开始菜单 / 磁贴 走这条     → 实测解析正确 = "Reactor 模板"  ✅

<Properties>/<DisplayName> = ms-resource:AppDisplayName
   → Package.Current.DisplayName 走这条 → 实测解析成 SoundGroup/Text = "声音"  ❌
```

`Package.Current.DisplayName` 那条链解析到了**毫不相干的键**的值。
按名字查资源表（`ResourceLoader.GetString("AppDisplayName")`）则**永远正确**。

图标那边是两件事叠加：URI 走的是 `file:///`（非官方包内通道）+ 我上一轮加的尺寸把图裁了。

---

## 二、六条律令（照做，别再翻车）

### 1. GitHub main ≠ 你安装的版本

查"某个资源/属性/常量的真值"时，**证据必须来自装的那份**，不是源码仓库。

```bash
# 装的那份在哪
ls ~/.nuget/packages/<包名>/<版本>/
# 查资源真值（包里带 .pri 的）
makepri dump -if <包内 .pri> -of dump.xml -dt detailed
# 查常量/字符串（包里是二进制的就二进制搜）
grep -ral "SettingsCardHeaderIconMaxSize" ~/.nuget/packages/<包名>/<版本>/
```

**搜不到就是没有。** 源码仓库 main 分支里有，对你装的这个版本**不构成任何证据**。

### 2. 没有运行时证据，不许下结论

猜三轮的成本 >> 加一次诊断的成本。**一次把所有可疑值全部打出来**：

```csharp
// 临时打进 UI（比打日志快，不用找文件）
description: $"[诊断] DisplayName={Package.Current.DisplayName} | " +
             $"资源AppDisplayName={loader.GetString("AppDisplayName")} | " +
             $"Logo={Package.Current.Logo} | " +
             $"语言={string.Join(",", ApplicationLanguages.Languages)}";
```

一次跑完，四个字段同时到手，一轮定案。
**验证完立刻删**（标 `TODO(诊断)` 便于收尾 grep）。

### 3. 「不可能的值」= 消费链错位，不是资源本身错

看到 A 的位置显示了 B 的值，**先证明资源本身没问题**，再去查谁在消费它。

```bash
makepri dump -if <AppX/resources.pri> -of d.xml -dt detailed
```

确认三件事：值对不对、是不是只有一条、URI 对不对。
三条都对 → 资源没问题 → 问题在**消费它的那条链**。这时要问的是「有几条链」，不是「这条链哪错了」。

### 4. 同名/同类 API 可能走不同的链

本事故里的三组分叉：

| 看起来是一回事 | 实际是两条链 |
|---|---|
| 清单的两个 `DisplayName` | `<Properties>` → `Package.Current.DisplayName`（坏）；`VisualElements/@` → 外壳（好） |
| `Image` vs `BitmapIcon` | 都吃 URI，但一个有显式尺寸撑着（图没出来也看不出），一个靠位图撑自然尺寸（直接塌成 0） |
| 按名字查资源 vs 系统解析 `ms-resource:` | 前者永远对，后者可能串 |

**改之前先问：有几条链？坏的是哪条？好的那条别动。**
（本事故只改坏的那条成字面量，好的那条保留 `ms-resource:`，所以外壳名字仍然本地化。）

### 5. 上一轮自己的"修复"，可能就是这一轮的 bug

改完回头**逐行审一遍上一轮加的每一行**。
这次的 `.Width(20).Height(20)` 就是上一轮我自己加的，它制造了"图标缩没了"这个症状，
然后我又围着这个假症状查了两轮。

### 6. 静默失败：先查加载，不是查尺寸

URI 非法 / 资源解析失败，**不抛异常、不报错**，图就是不出来。
所以"图标小到看不见"的第一嫌疑人是 **URI 没加载成功**，不是尺寸设小了。

---

## 三、取证手段（按性价比排序）

| # | 手段 | 拿到什么 |
|---|---|---|
| 1 | 运行时诊断串打进 UI | 一次拿到全部真实取值。**永远先做这个** |
| 2 | `makepri dump -dt detailed` | PRI 里键的实际值、索引、URI —— 证明"资源本身对不对" |
| 3 | `Get-StartApps` | 外壳（开始菜单）解析出的显示名，与 `Package.Current.DisplayName` 对照即可判定是哪条链坏了 |
| 4 | 二进制 grep 装的 NuGet 包 | 某个常量/资源在**已安装版本**里到底存不存在 |
| 5 | 注册表 `HKCU:\...\AppModel\Repository\Packages\<FullName>` | 值是未解析的 `ms-resource:` 原文 → 证明是**实时解析**而非注册期缓存 |

PowerShell 输出容易被吞，落文件再读：
```powershell
"结果" | Out-File D:\...\out.txt -Encoding utf8
(Get-StartApps) | ... | Out-File D:\...\out.txt -Append -Encoding utf8
```

---

## 四、两个反复踩的环境坑

### Debug 构建不刷新 AppX 目录

`bin/.../AppX/AppxManifest.xml` 和 `AppX/resources.pri` **只有部署时才刷**，`dotnet build` 不动它们。
表现：改了 resw / manifest，跑起来还是旧值 → 误判成"改了没生效 / 平台 bug"。

**用 `winapp run .` 跑，别只 build。** 打包验证加 `-p:GenerateAppxPackageOnBuild=true`。

### 示例项目可能引用的是发布包，不是源码

本仓库 `samples/Reactor.Template` 引的是 NuGet 包 `Reactor.Uwp 0.1.0-alpha.4`，
`UwpApp` 才是 `ProjectReference`。所以**框架侧的修复示例项目吃不到**，
要么就地做一份等价实现（幂等，新旧包都对），要么用包里已有的 API。
给框架加新参数前先看 csproj 的引用方式，否则会编译失败。

---

## 五、行动前自查

遇到「值不对 / 图不出来 / 控件显示异常」，在开口给出结论之前：

- [ ] 我有没有**运行时实际取值**？（没有 → 先加诊断，别猜）
- [ ] 我说"这是 XX 版本的行为"，证据来自**装的那份**还是 GitHub main？
- [ ] 我准备说"改不了"了 —— 有没有确认过是不是**只有某一条链坏了**？
- [ ] 上一轮我加的修复，有没有可能是本轮症状的来源？
- [ ] 静默失败的场景（URI/资源），我有没有先排除"根本没加载"？
- [ ] 是不是只 build 没部署？（AppX 目录可能是旧的）

---

## 六、配套

- 具体到本地化的键名规则 / `x:Uid` / resw → `.workbuddy/skills/uwp-winui-localization/SKILL.md`
- 包内资源 URI 的实现 → `Reactor.uwp/Internal/PackUri.cs`
