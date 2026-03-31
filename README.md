# AbfScraper

> 针对 [abf.store](https://www.abf.store) 轴承电商网站的 .NET 采集库。通过一次 `Search()` 调用即可按型号/品牌搜索，自动分页、抓取详情页，返回包含规格参数、报价、库存、等效型号、图片的结构化二维数组，并可选附带英译中翻译。

---

## 项目背景

[abf.store](https://www.abf.store) 是欧洲大型轴承经销商，经营 FAG、SKF、NSK、INA 等主流品牌数百万个 SKU，但**官方不提供任何公开 API**。

本库对其会话登录流程和搜索/分页接口进行逆向，将采集结果以 `String(,)` 二维数组形式暴露出来，可直接在 Excel VBA、VB.NET 或 C# 项目中使用，无需浏览器或任何 GUI 环境。

---

## 实现功能

- **自动登录** — 自动提取首页 CSRF Token，完成表单登录并维护 Cookie 会话，后续请求全程复用同一 `HttpClient` 实例
- **多模式搜索** — 支持"包含 / 开头匹配 / 完全匹配"三种模式，型号与品牌可单独或组合检索
- **自动分页** — 每页 50 条，自动翻页直到达到 `maxResults` 上限或服务器无更多结果为止；同时解析首页返回的"网站总命中数"写入 `arr(i,0)`
- **详情页全字段采集** — 每个产品抓取 22 个字段：型号、简述、原价/折扣价、库存、内径/外径/宽度/重量、孔型、密封件、保持架、热稳定、精度、外部改装、后缀描述等
- **等效型号** — 等效品区块由浏览器通过 AJAX 单独加载，本库识别 `data-renderurl` 属性后发起第二次请求并解析，以管道符分隔格式拼入结果（每4字段一条：型号|库存|原价|折扣价）
- **图片下载** — 按确定性命名规则（`型号全大写-结果序号-图片序号.扩展名`）下载并保存，目录不存在时自动创建
- **英译中翻译** — 内置 MyMemory（免费、无需 Key）和百度翻译（国内推荐）两个后端，翻译结果自动缓存到本地 `trans_dict.txt`，重复词汇不再发起网络请求
- **整体超时控制** — `timeoutSeconds` 通过 `CancellationTokenSource` 管控整次 `Search()` 的全部 HTTP 请求，而非单次请求
- **去重保护** — 用 `HashSet` 记录已收集的详情页 URL，服务器分页异常返回重复内容时立即退出，防止死循环

---

## 技术难点

- **无公开 API，纯 HTML 解析** — 所有数据均嵌入服务端渲染的 HTML，依赖 [HtmlAgilityPack](https://html-agility-pack.net/) 通过 XPath 定位节点；页面结构变更需相应调整选择器
- **分页参数逆向** — 搜索 URL 中 `p` 为 1-indexed 页码，`os` 为库存过滤器（固定传 `0` 表示不过滤），参数含义未在任何文档中说明，需通过抓包分析实际浏览器请求才能确认
- **搜索高亮干扰型号提取** — 列表页产品名链接内，匹配到的字符被 `<span class="highlight">` 包裹，同时附有 `<span class="product-desc">aka …</span>` 备用名。直接取 `InnerText` 会得到残缺型号（缺失高亮字符）或混入 aka 文本；正确做法是遍历直接子节点：保留文本节点和 `highlight` span，跳过 `product-desc` span
- **等效品区块懒加载** — 等效品不在初始 HTML 中，需找到占位节点上的 `data-renderurl`，再单独发一次 GET 请求才能获取数据，每个产品多一次 HTTP 往返
- **简述与重量混淆** — 列表页简介节点格式为"重量 · 简述"，当产品无简述时节点只含重量，直接截取会将重量写入简介字段；需检测 ` · ` 分隔符是否存在，不存在则置空
- **CSRF + Cookie 会话** — 登录需两步：GET 首页提取 `__RequestVerificationToken` 隐藏字段，再 POST 凭证；后续所有请求必须携带登录后的 Cookie，整个生命周期由同一 `HttpClientHandler` 持有的 `CookieContainer` 管理

---

## 优点

- **轻量，无浏览器依赖** — 仅依赖 HtmlAgilityPack，无需 Selenium、Playwright 或任何无头浏览器，纯 HTTP + HTML 解析，启动即用
- **分发极简** — 只需交付 `AbfScraper.dll` + `HtmlAgilityPack.dll` 两个文件，在 .NET Framework 4.6.1+ 到 .NET 8 的所有目标框架上均可运行
- **翻译缓存可离线维护** — `trans_dict.txt` 是人类可读的 UTF-8 Tab 分隔文件，可手动预填充专业术语，完全脱离翻译 API 使用
- **输出格式确定性强** — 所有字段统一以 `"|"` 分隔，无数据时返回 `"|"` 而非空字符串，下游 `Split("|")` 逻辑始终可靠，不需要额外判空

---

## 环境要求

- .NET Standard 2.0+（.NET Framework 4.6.1+ / .NET Core 2.0+ / .NET 5/6/7/8）
- 项目中引用 `AbfScraper.dll` 和 `HtmlAgilityPack.dll`

---

## Quick Start

```vb
Imports AbfScraper

Using client As New AbfClient()
    ' Step 1 – Login
    If Not client.Login("your@email.com", "your_password") Then
        MsgBox("Login failed") : Return
    End If

    ' Step 2 – Search
    Dim arr = client.Search(
        model:="NU324-E-TVP2",
        brand:="FAG",
        matchMode:=1,
        timeoutSeconds:=120,
        maxResults:=10,
        enableTranslation:=False)

    ' Step 3 – Read results
    Dim total = arr.GetLength(0)
    For i = 0 To total - 1
        Console.WriteLine(arr(i, 1))  ' Model name
        Console.WriteLine(arr(i, 3))  ' Price (original|discounted)
        Console.WriteLine(arr(i, 4))  ' Stock
    Next
End Using
```

---

## Search() Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `model` | String | — | Bearing model, e.g. `"NU324-E-TVP2"`; `""` = any |
| `brand` | String | — | Brand, e.g. `"FAG"`; `""` = any |
| `matchMode` | Integer | `1` | `0`=contains / `1`=starts with / `2`=exact |
| `timeoutSeconds` | Integer | `120` | Total timeout for the entire Search() call; `0` = unlimited |
| `maxResults` | Integer | `0` | Max rows to return; `0` = unlimited |
| `enableTranslation` | Boolean | `False` | Auto-translate English fields to Chinese |
| `imageSavePath` | String | `""` | Directory to save product images; `""` = skip |

---

## Return Value

`Search()` returns a `String(,)` 2D array. `arr.GetLength(0) = 0` means no results.

| Index | Field | Example |
|:---:|---|---|
| `arr(i, 0)` | Total matches on site | `"5"` |
| `arr(i, 1)` | Model | `"NU324E.TVP2 FAG"` |
| `arr(i, 2)` | Short description | `"Single-row cylindrical roller bearing"` |
| `arr(i, 3)` | Price (original\|discounted) | `"€ 82,31|€ 75,00"` |
| `arr(i, 4)` | Stock | `"16 pcs"` |
| `arr(i, 5)` | Equivalents | `"7312B FAG|0 pcs|€ 50,00||..."` (4 fields per entry) |
| `arr(i, 6)` | Description (EN\|ZH) | `"The NU324...|该NU324..."` |
| `arr(i, 7)` | Also known as | `"NU324-E-TVP2"` |
| `arr(i, 8)` | Category (EN\|ZH) | `"Cylindrical Roller Bearing|圆柱滚子轴承"` |
| `arr(i, 9)` | Bore diameter (mm) | `"120"` |
| `arr(i, 10)` | Outer diameter (mm) | `"260"` |
| `arr(i, 11)` | Width (mm) | `"55"` |
| `arr(i, 12)` | Weight (kg) | `"13.3"` |
| `arr(i, 13–19)` | Bore / Seal / Cage / … (EN\|ZH) | — |
| `arr(i, 20)` | Image filenames | `"NU324ETVP2FAG-1-1.png,..."` |
| `arr(i, 21)` | Suffix descriptions (EN\|ZH pairs) | `"X-Life|X-Life（超长寿命）|..."` |

> Fields with no data return `"|"`, never an empty string.

---

## Translation

```vb
' Standalone translator (no scraping)
Using t As New AbfTranslator()
    Dim zh = t.Translate("Deep groove ball bearing")  ' → "深沟球轴承"
End Using

' With Baidu Translate (recommended for mainland China)
Using t As New AbfTranslator(
        provider:=TranslationProvider.Baidu,
        appId:="YOUR_APP_ID",
        secretKey:="YOUR_SECRET_KEY")
    Dim zh = t.Translate("Cylindrical roller bearing")
End Using
```

Translated terms are cached in `trans_dict.txt` (UTF-8, tab-separated) and reused on subsequent calls without network requests.

---

## Build from Source

```
dotnet build AbfScraper.vbproj -c Release
```

Output: `bin\Release\netstandard2.0\AbfScraper.dll` + `HtmlAgilityPack.dll`

Distribute both DLL files together.

---

详细中文说明见 [使用说明.md](使用说明.md)
