# AbfScraper

针对 [abf.store](https://www.abf.store) 轴承电商网站的 .NET 采集库。按型号 / 品牌搜索，自动分页、抓取详情页，返回包含规格参数、报价、库存、等效型号、图片的结构化二维数组，可选百度英译中翻译。

---

## 环境要求

| 条件 | 说明 |
|---|---|
| 运行时 | .NET Standard 2.0+（.NET Framework 4.6.1+、.NET Core 2.0+、.NET 5/6/7/8） |
| 依赖 | `AbfScraper.dll` + `HtmlAgilityPack.dll`（放同一目录） |
| 网络 | 需能访问 `https://www.abf.store`；使用翻译还需 `https://fanyi-api.baidu.com` |

在代码顶部引入命名空间：
```vb
Imports AbfScraper
```

---

## 快速开始

```vb
Imports AbfScraper

Module QuickStart
    Sub Main()
        Try
            Using client As New AbfClient(baiduAppId:="YOUR_ID", baiduSecretKey:="YOUR_KEY")

                ' 1. 登录
                If Not client.Login("your@email.com", "your_password") Then
                    Console.WriteLine("登录失败")
                    Return
                End If

                ' 2. 搜索
                Dim arr = client.Search(
                    model:="NU324-E-TVP2", brand:="FAG",
                    maxResults:=5, enableTranslation:=True,
                    imageSavePath:="C:\img\", maxConcurrent:=3)

                ' 3. 读取结果
                If arr.GetLength(0) = 0 Then
                    Console.WriteLine("无结果")
                    Return
                End If

                Console.WriteLine($"总命中: {arr(0, 0)}，已返回: {arr.GetLength(0)} 条")
                For i = 0 To arr.GetLength(0) - 1
                    Console.WriteLine($"[{i}] {arr(i, 1)}  价格={arr(i, 3)}  库存={arr(i, 4)}")
                Next
            End Using
        Catch ex As Exception
            Console.WriteLine("错误：" & ex.Message)
        End Try
    End Sub
End Module
```

---

## API 参考

### 1. 创建客户端 `New AbfClient()`

```vb
' 不使用翻译
Dim client As New AbfClient()

' 使用百度翻译
Dim client As New AbfClient(
    baiduAppId:="YOUR_APP_ID",
    baiduSecretKey:="YOUR_SECRET_KEY")

' 指定词典文件路径
Dim client As New AbfClient(
    baiduAppId:="YOUR_APP_ID",
    baiduSecretKey:="YOUR_SECRET_KEY",
    dictPath:="C:\MyApp\trans_dict.txt")
```

| 参数 | 类型 | 默认值 | 约束 | 说明 |
|---|---|---|---|---|
| `baiduAppId` | String | `""` | 与 `baiduSecretKey` 必须同时为空或同时填写 | 百度翻译 AppId；两者均为空时不翻译 |
| `baiduSecretKey` | String | `""` | 与 `baiduAppId` 必须同时为空或同时填写 | 百度翻译 SecretKey |
| `dictPath` | String | `""` | 路径须为合法文件路径；目录不存在不会自动创建 | 词典缓存文件路径；留空则使用程序目录下的 `trans_dict.txt` |

> 翻译结果自动缓存至 `trans_dict.txt`（UTF-8 Tab 分隔），相同词汇不再联网。可手动预填充专业术语加速首次运行。
> 百度翻译申请：`https://fanyi-api.baidu.com/` 注册开发者账号，免费额度 500 万字/月。

> ⚠️ **线程安全**：`AbfClient` 实例**不是线程安全的**。不要在同一实例上并发调用 `Search()`（`_isLoggedIn`、`LastSearchInfo` 等状态字段无锁保护）。多线程并行采集时，请为每个线程创建独立的 `AbfClient` 实例并分别调用 `Login()`。

---

### 2. 登录 `Login()`

```vb
Dim ok As Boolean = client.Login("your@email.com", "your_password")
```

| 参数 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `username` | String | **不可为空或空白**，否则抛出 `ArgumentException` | 登录邮箱 |
| `password` | String | **不可为空或空白**，否则抛出 `ArgumentException` | 密码 |
| **返回值** | Boolean | — | `True` = 登录成功，`False` = 用户名或密码错误 |

- 登录固定超时 **120 秒**，超时抛出异常。
- 登录后 Cookie 会话全程维护，无需重复登录（Session 过期前）。
- **必须先登录再调用 `Search()`**，否则抛出 `InvalidOperationException`。
- 登录成功后自动将货币设为 EUR；设置失败不影响后续功能。

---

### 3. 搜索 `Search()`

```vb
Dim arr As String(,) = client.Search(
    model:="NU324-E-TVP2",
    brand:="FAG",
    matchMode:=1,
    timeoutSeconds:=120,
    maxResults:=10,
    enableTranslation:=True,
    imageSavePath:="C:\img\",
    maxConcurrent:=3,
    onProgress:=Nothing,
    page:=0)
```

| 参数 | 类型 | 默认值 | 约束 | 不填时的效果 |
|---|---|---|---|---|
| `model` | String | **必填** | 不可省略；可传 `""` 表示不限型号 | 必须传入 |
| `brand` | String | **必填** | 不可省略；可传 `""` 表示不限品牌 | 必须传入 |

> ⚠️ `model` 和 `brand` **不建议同时为空**。两者均为空时相当于搜索全站，命中数可能超过数十万条；配合 `maxResults` 或 `timeoutSeconds` 加以限制，否则程序会长时间运行并消耗大量内存。
| `matchMode` | Integer | `1` | 仅 `0`/`1`/`2` 有效；**其他值自动回退为 `1`**（开头匹配） | 按型号前缀搜索 |
| `timeoutSeconds` | Integer | `120` | `0` 或负数 = 无限等待；正整数 = 超时秒数 | 120 秒超时；数据量大时建议手动调大，如 `600` |
| `maxResults` | Integer | `0` | `0` 或负数 = 不限制；正整数 N = 最多返回 N 条 | 不限制，返回全部命中结果（受超时控制） |
| `enableTranslation` | Boolean | `False` | 设 `True` 时须先在构造函数中配置百度 AppId/SecretKey，否则中文部分为空 | 不翻译；英中字段格式为 `"英文\|"`，中文部分为空 |
| `imageSavePath` | String | `""` | 留空不下载；非空时目录不存在会自动创建 | 不下载图片；`arr(i,20)` 仍返回文件名，但文件不在磁盘上 |
| `maxConcurrent` | Integer | `1` | `≤1` = 串行；建议 `2`~`3`；超过 `5` 收益递减且可能触发服务器限速 | 串行，最稳定但最慢 |
| `onProgress` | Action(Of Integer, Integer) | `Nothing` | 可传 `Nothing`（VBA 调用时传 `Nothing`） | 无进度反馈 |
| `page` | Integer | `0` | `0` = 自动翻全部页；正整数 N = 只抓第 N 页（每页 50 条） | 自动翻全部页直到搜完或超时 |

> **计算总页数**：`arr(0, 0)` 返回网站总命中条数，`Math.Ceiling(总条数 / 50)` 即为总页数。例如总条数 137 ÷ 50 = 3 页。

#### `LastSearchInfo` 属性

调用 `Search()` 后检查此属性了解停止原因：

| 值 | 含义 |
|---|---|
| `"OK"` | 正常完成（全部结果已采集） |
| `"超时"` | 达到 `timeoutSeconds`，返回已采集的部分数据 |
| `"达到上限"` | 达到 `maxResults` 上限 |
| `"列表页失败"` | 列表页重试 3 次均失败，返回已有部分数据 |
| `"Session过期"` | 登录状态失效，需重新调用 `Login()` |

#### `Translator` 属性

```vb
Dim t As AbfTranslator = client.Translator
```

返回当前客户端使用的 `AbfTranslator` 实例，可在不单独 `New AbfTranslator` 的情况下直接调用翻译功能。

---

### 4. 返回值

`Search()` 返回 `String(,)` 二维数组。`arr.GetLength(0) = 0` 表示无结果；否则行下标 `0` 起，列 `0`~`21`：

| 列 | 字段 | 格式 | 示例 |
|:---:|---|---|---|
| 0 | 总命中条数（每行相同） | 纯数字字符串 | `"13791"` |
| 1 | 型号 | 型号+品牌 | `"NU324E.TVP2 FAG"` |
| 2 | 简述 | 英文文本 | `"Single-row cylindrical roller bearing"` |
| 3 | 价格 | `原价\|折扣价` | `"€ 82,31\|€ 75,00"` 或 `"€ 82,31\|"` |
| 4 | 库存 | 数量 + 单位 | `"16 pcs"` / `"0 pcs"` |
| 5 | 等效型号 | 每 4 字段循环：`型号\|库存\|原价\|折扣价` | `"7312B FAG\|0 pcs\|€ 50,00\|\|..."` |
| 6 | 产品描述 | `英文\|中文` | `"The NU324...\|该 NU324..."` |
| 7 | 别名 | 纯文本 | `"NU324-E-TVP2"` |
| 8 | 类别 | `英文\|中文` | `"Cylindrical Roller Bearing\|圆柱滚子轴承"` |
| 9 | 内径 mm | 数字 | `"120"` |
| 10 | 外径 mm | 数字 | `"260"` |
| 11 | 宽度 mm | 数字 | `"55"` |
| 12 | 重量 kg | 数字 | `"13.3"` |
| 13 | 孔型 | `英文\|中文` | `"C - Cylindrical Bore\|圆柱孔"` |
| 14 | 密封件 | `英文\|中文` | `"No Seal\|无密封件"` |
| 15 | 保持架 | `英文\|中文` | `"P - Plastic Molded Cage\|塑料保持架"` |
| 16 | 外部改装 | `英文\|中文` | `"No External Modification\|无外部改装"` |
| 17 | 径向游隙 | `英文\|中文` | `"Cn Normal Internal Play\|Cn 正常游隙"` |
| 18 | 精度 | `英文\|中文` | `"Standard Precision\|标准精度"` |
| 19 | 热稳定 | `英文\|中文` | `"No Heat Stabilization\|无热稳定处理"` |
| 20 | 图片文件名 | 逗号分隔 | `"NU324ETVP2FAG-1-1.png,NU324ETVP2FAG-1-2.png"` |
| 21 | 后缀描述 | `KEY\|英文\|中文` 每 3 字段循环 | `"X-Life\|Extended service life\|超长寿命"` |

**格式规则：**
- 无数据的字段统一返回 `"|"`，不返回空字符串
- 含英中文的字段固定为 `"英文|中文"`，`enableTranslation=False` 时中文部分为空（`"英文|"`）
- 图片命名规则：`型号字母数字全大写-结果序号-图片序号.扩展名`，如 `NU324ETVP2FAG-1-1.png`

---

### 5. 单独使用翻译 `AbfTranslator`

不采集数据，仅翻译时直接使用 `AbfTranslator`：

```vb
Using t As New AbfTranslator(appId:="YOUR_ID", secretKey:="YOUR_KEY")
    Dim zh = t.Translate("Deep groove ball bearing")  ' → "深沟球轴承"
End Using
```

| 参数 | 类型 | 默认值 | 约束 | 说明 |
|---|---|---|---|---|
| `appId` | String | `""` | 与 `secretKey` 必须同时为空或同时填写；均为空时 `Translate()` 返回空字符串 | 百度翻译 AppId |
| `secretKey` | String | `""` | 同上 | 百度翻译 SecretKey |
| `dictPath` | String | `""` | 路径须为合法文件路径 | 词典文件路径，留空使用程序目录下的 `trans_dict.txt` |

`Translate()` 方法：
- 传入空字符串或纯空白时返回 `""`
- 翻译失败或百度 Key 未配置时返回 `""`（不抛异常）
- 翻译结果不含中文字符时视为翻译失败，返回 `""`
- HTTP 请求超时为 30 秒
- 支持可选 `CancellationToken` 参数（Search 超时时翻译请求同步取消）

---

## 使用示例

### 示例 1：最简单的搜索

```vb
Imports AbfScraper

Module Example1
    Sub Main()
        Using client As New AbfClient()
            If Not client.Login("your@email.com", "your_password") Then Return

            ' 不翻译、不下载图片、串行抓取
            Dim arr = client.Search(model:="6205", brand:="SKF")

            For i = 0 To arr.GetLength(0) - 1
                Console.WriteLine($"{arr(i, 1)}  库存={arr(i, 4)}")
            Next
        End Using
    End Sub
End Module
```

### 示例 2：完整字段解析

```vb
Imports AbfScraper

Module Example2
    Sub Main()
        Using client As New AbfClient(baiduAppId:="YOUR_ID", baiduSecretKey:="YOUR_KEY")
            If Not client.Login("your@email.com", "your_password") Then Return

            Dim arr = client.Search(
                model:="NU324-E-TVP2",
                brand:="FAG",
                matchMode:=1,
                timeoutSeconds:=120,
                maxResults:=5,
                enableTranslation:=True,
                imageSavePath:="C:\img\",
                maxConcurrent:=3)

            If arr.GetLength(0) = 0 Then
                Console.WriteLine("无结果")
                Return
            End If

            Console.WriteLine($"搜索命中总数：{arr(0, 0)}，已返回：{arr.GetLength(0)} 条")

            For i = 0 To arr.GetLength(0) - 1
                Dim resultName  = arr(i, 1)   ' 型号
                Dim shortDesc   = arr(i, 2)   ' 简述
                Dim price       = arr(i, 3)   ' 原价|折扣价
                Dim stock       = arr(i, 4)   ' 库存
                Dim equivalents = arr(i, 5)   ' 等效型号
                Dim description = arr(i, 6)   ' 产品描述（英|中）
                Dim alsoKnown   = arr(i, 7)   ' 别名
                Dim category    = arr(i, 8)   ' 类别（英|中）
                Dim innerDiam   = arr(i, 9)   ' 内径
                Dim outerDiam   = arr(i, 10)  ' 外径
                Dim width       = arr(i, 11)  ' 宽度
                Dim weight      = arr(i, 12)  ' 重量
                Dim bore        = arr(i, 13)  ' 孔型
                Dim seal        = arr(i, 14)  ' 密封件
                Dim cage        = arr(i, 15)  ' 保持架
                Dim extMod      = arr(i, 16)  ' 外部改装
                Dim radPlay     = arr(i, 17)  ' 径向游隙
                Dim precision   = arr(i, 18)  ' 精度
                Dim heatStab    = arr(i, 19)  ' 热稳定
                Dim imageNames  = arr(i, 20)  ' 图片文件名
                Dim suffixDesc  = arr(i, 21)  ' 后缀描述

                ' —— 拆分价格 ——
                Dim priceParts  = price.Split("|"c)
                Dim formerPrice = priceParts(0)                                   ' 原价
                Dim discPrice   = If(priceParts.Length > 1, priceParts(1), "")     ' 折扣价

                ' —— 拆分产品描述英/中 ——
                Dim descParts = description.Split("|"c)
                Dim descEn    = descParts(0)
                Dim descZh    = If(descParts.Length > 1, descParts(1), "")

                ' —— 拆分等效型号（每4字段一条）——
                Dim eqParts = equivalents.Split("|"c)
                For j = 0 To eqParts.Length - 4 Step 4
                    Dim eqModel    = eqParts(j)
                    Dim eqStock    = eqParts(j + 1)
                    Dim eqPrice    = eqParts(j + 2)
                    Dim eqDiscPrice = eqParts(j + 3)
                    Console.WriteLine($"  等效: {eqModel}  库存={eqStock}  价={eqPrice}")
                Next

                ' —— 拆分图片文件名 ——
                Dim images = imageNames.Split(","c)
                For Each img In images
                    If img <> "|" Then Console.WriteLine($"  图片: {img}")
                Next

                ' —— 拆分后缀描述（每3字段一条：KEY|英文|中文）——
                Dim sfxParts = suffixDesc.Split("|"c)
                For j = 0 To sfxParts.Length - 3 Step 3
                    Dim sfxKey  = sfxParts(j)
                    Dim sfxEn   = sfxParts(j + 1)
                    Dim sfxZh   = sfxParts(j + 2)
                    Console.WriteLine($"  后缀: {sfxKey} = {sfxEn} / {sfxZh}")
                Next

                Console.WriteLine($"[{i}] {resultName}")
                Console.WriteLine($"     价格={formerPrice}  折扣={discPrice}  库存={stock}")
                Console.WriteLine($"     类别={category}")
                Console.WriteLine($"     内径={innerDiam}  外径={outerDiam}  宽={width}  重={weight}")
                Console.WriteLine($"     描述(英)={descEn}")
                Console.WriteLine($"     描述(中)={descZh}")
                Console.WriteLine()
            Next
        End Using
    End Sub
End Module
```

### 示例 3：匹配模式对比

```vb
' matchMode=0 包含：型号中任意位置含 "6305" 的结果
arr = client.Search(model:="6305", brand:="", matchMode:=0)

' matchMode=1 开头匹配（默认）：型号以 "6305" 开头
arr = client.Search(model:="6305", brand:="", matchMode:=1)

' matchMode=2 完全匹配：型号严格等于 "6305"
arr = client.Search(model:="6305", brand:="", matchMode:=2)

' matchMode=99 无效值：自动回退为 1（开头匹配）
arr = client.Search(model:="6305", brand:="", matchMode:=99)
```

### 示例 4：超时与部分返回

```vb
' 只给 30 秒，搜索条数很多时可能超时
Dim arr = client.Search(
    model:="", brand:="FAG", matchMode:=0,
    timeoutSeconds:=30, maxConcurrent:=3)

' 超时不抛异常，检查 LastSearchInfo 判断是否完整
Select Case client.LastSearchInfo
    Case "OK"
        Console.WriteLine($"全部完成，共 {arr.GetLength(0)} 条")
    Case "超时"
        Console.WriteLine($"超时，已采集 {arr.GetLength(0)} 条（部分数据）")
    Case "Session过期"
        Console.WriteLine("Session 过期，需重新登录")
        client.Login("your@email.com", "your_password")
End Select
```

### 示例 5：限制返回条数

```vb
' 最多返回 10 条
Dim arr = client.Search(model:="6308", brand:="FAG", maxResults:=10)
Console.WriteLine($"网站总命中 {arr(0, 0)} 条，实际返回 {arr.GetLength(0)} 条")
' 若命中不足 10 条则返回实际数量

' maxResults=0 不限制（默认行为）
Dim arrAll = client.Search(model:="6308", brand:="FAG", maxResults:=0)
```

### 示例 6：进度回调

```vb
Dim arr = client.Search(
    model:="6305", brand:="FAG",
    maxConcurrent:=3,
    onProgress:=Sub(done As Integer, total As Integer)
                    Console.Write($"\r进度: {done}/{total}")
                End Sub)
Console.WriteLine()  ' 换行
Console.WriteLine($"采集完成，共 {arr.GetLength(0)} 条")
```

### 示例 7：只下载图片

```vb
' imageSavePath 非空时自动下载图片到该目录（不存在时自动创建）
Dim arr = client.Search(
    model:="NU324-E-TVP2", brand:="FAG",
    maxResults:=3, imageSavePath:="C:\bearings\img\")

' arr(i, 20) 返回图片文件名（逗号分隔），文件已保存到 C:\bearings\img\
For i = 0 To arr.GetLength(0) - 1
    Console.WriteLine($"{arr(i, 1)} 的图片: {arr(i, 20)}")
Next

' imageSavePath 留空时不下载，但 arr(i, 20) 仍返回文件名
Dim arr2 = client.Search(model:="NU324-E-TVP2", brand:="FAG", maxResults:=3)
' arr2(0, 20) = "NU324ETVP2FAG-1-1.png,NU324ETVP2FAG-1-2.png"（文件不在磁盘上）
```

### 示例 8：并发控制

```vb
' 串行（默认）— 最稳定，~0.25 条/秒
arr = client.Search(model:="6", brand:="FAG", maxConcurrent:=1)

' 推荐并发 — ~0.75 条/秒
arr = client.Search(model:="6", brand:="FAG", maxConcurrent:=3)

' 高并发 — ~1.25 条/秒，可能触发限速
arr = client.Search(model:="6", brand:="FAG", maxConcurrent:=5)
```

### 示例 9：分页模式（逐页采集/断点续传）

`page` 参数每次只抓一页（50 条），配合外部循环实现断点续传：

```vb
Imports System.IO
Imports AbfScraper

Module PagedCrawl
    Const SaveDir        As String = "C:\bearings\"
    Const Brand          As String = "FAG"
    Const CheckpointFile As String = "C:\bearings\progress.txt"

    Sub Main()
        Using client As New AbfClient(baiduAppId:="YOUR_ID", baiduSecretKey:="YOUR_KEY")

            If Not client.Login("your@email.com", "your_password") Then Return

            ' 读取上次断点页码
            Dim pageNum As Integer = 1
            If File.Exists(CheckpointFile) Then
                Integer.TryParse(File.ReadAllText(CheckpointFile).Trim(), pageNum)
            End If

            Do
                Console.Write($"第 {pageNum} 页... ")
                Dim arr As String(,)
                Try
                    arr = client.Search(
                        model:="", brand:=Brand, matchMode:=0,
                        page:=pageNum,           ' 只抓第 pageNum 页
                        timeoutSeconds:=60,
                        maxConcurrent:=3,
                        enableTranslation:=True,
                        imageSavePath:=SaveDir & "img\")
                Catch ex As Exception
                    Console.WriteLine($"异常: {ex.Message}，10 秒后重试...")
                    Threading.Thread.Sleep(10000)
                    Continue Do
                End Try

                Dim n = arr.GetLength(0)
                Console.WriteLine($"{n} 条  [{client.LastSearchInfo}]")
                If n = 0 Then Exit Do

                ' 写入本页数据（Tab 分隔，避免与价格中的逗号冲突）
                Using sw As New StreamWriter(
                        Path.Combine(SaveDir, $"{Brand}_p{pageNum:D4}.csv"),
                        False, Text.Encoding.UTF8)
                    For i = 0 To n - 1
                        sw.WriteLine(String.Join(vbTab,
                            arr(i,1), arr(i,3), arr(i,4), arr(i,8), arr(i,6)))
                    Next
                End Using

                ' 保存断点
                File.WriteAllText(CheckpointFile, (pageNum + 1).ToString())

                ' 本页不足 50 条说明已到末页
                If n < 50 Then Exit Do

                ' Session 过期时重新登录
                If client.LastSearchInfo = "Session过期" Then
                    client.Login("your@email.com", "your_password")
                End If

                pageNum += 1
                Threading.Thread.Sleep(500)
            Loop
        End Using
    End Sub
End Module
```

### 示例 10：单独使用翻译

```vb
Imports AbfScraper

Module TranslateOnly
    Sub Main()
        ' 方式一：直接创建 AbfTranslator
        Using t As New AbfTranslator(appId:="YOUR_ID", secretKey:="YOUR_KEY")
            Console.WriteLine(t.Translate("Deep groove ball bearing"))       ' → 深沟球轴承
            Console.WriteLine(t.Translate("Cylindrical roller bearing"))     ' → 圆柱滚子轴承
            Console.WriteLine(t.Translate(""))                               ' → ""（空输入返回空）
        End Using

        ' 方式二：指定词典文件路径
        Using t As New AbfTranslator(
                appId:="YOUR_ID",
                secretKey:="YOUR_KEY",
                dictPath:="C:\MyApp\trans_dict.txt")
            Console.WriteLine(t.Translate("Angular contact ball bearing"))   ' → 角接触球轴承
        End Using

        ' 方式三：通过 AbfClient.Translator 属性复用
        Using client As New AbfClient(baiduAppId:="YOUR_ID", baiduSecretKey:="YOUR_KEY")
            Dim zh = client.Translator.Translate("Tapered roller bearing")   ' → 圆锥滚子轴承
            Console.WriteLine(zh)
        End Using
    End Sub
End Module
```

---

## 性能建议

| `maxConcurrent` | 实测速度 | 说明 |
|:---:|:---:|---|
| `1`（串行） | ~0.25 条/秒 | 最稳定 |
| `3`（推荐） | ~0.75 条/秒 | 约 3× 提升，性价比最高 |
| `5` | ~1.25 条/秒 | 提升明显，受服务器限速影响 |

> 瓶颈在服务器：每条详情页响应约 3.9 秒，吞吐量 ≈ `maxConcurrent ÷ 3.9`。代码侧无法进一步优化。日常推荐 `3`，追求速度用 `5`。

常用配置：
```vb
' 单条/少量查询（快进快出）
client.Search(model:="6308-2RS", brand:="FAG", maxResults:=10, timeoutSeconds:=30)

' 批量采集（推荐配置）
client.Search(model:="6", brand:="FAG", maxResults:=0, timeoutSeconds:=600, maxConcurrent:=3)

' 定时采集（限时 30 秒取尽可能多的数据）
client.Search(model:="", brand:="FAG", maxResults:=99999, timeoutSeconds:=30, maxConcurrent:=3)
```

---

## 编译指南（面向拿到源码的客户）

### 1. 安装 .NET SDK

编译源码只需安装 **[.NET SDK](https://dotnet.microsoft.com/download)**（6.0 或以上，推荐 8.0）。不需要安装 Visual Studio。

下载安装后，打开命令提示符（`cmd`）或 PowerShell 输入以下命令确认安装成功：

```
dotnet --version
```

能看到版本号（如 `8.0.100`）即可。

### 2. 源码目录结构

```
AbfScraper/
├── AbfScraper.vbproj     项目文件（一般不需要改）
├── AbfClient.vb           ★ 主文件：登录、搜索、HTML解析、分页、图片下载
├── AbfTranslator.vb         翻译功能（百度翻译 API + 本地词典缓存）
├── BearingResult.vb         返回值字段定义（21 个字段，对应 arr 列 1~21；列 0 的总条数由 Search() 组装）
└── trans_dict.txt           翻译词典缓存文件（运行时自动维护，不需要手动改）
```

### 3. 编译命令

在源码目录下打开命令行，执行：

```
dotnet build AbfScraper.vbproj -c Release
```

> 如果报 NuGet 还原错误，先执行 `dotnet restore`，再执行上面的 build 命令。

编译成功后输出在：

```
bin\Release\netstandard2.0\
├── AbfScraper.dll          ★ 主库（给使用方的文件）
├── HtmlAgilityPack.dll     ★ 依赖库（必须和 AbfScraper.dll 放一起）
├── AbfScraper.pdb            调试符号文件（不需要给使用方）
└── AbfScraper.deps.json      依赖描述文件（不需要给使用方）
```

**只需要把 `AbfScraper.dll` 和 `HtmlAgilityPack.dll` 两个文件给使用方。**

### 4. 修改源码指引

绝大多数需求只需要修改 **`AbfClient.vb`**，包括：

| 要改的东西 | 在哪改 |
|---|---|
| 调整搜索 URL 参数、分页逻辑 | `AbfClient.vb` → `Search()` 方法 |
| 调整详情页字段解析（XPath 选择器） | `AbfClient.vb` → `FetchDetail()` 等内部方法 |
| 修改登录逻辑 | `AbfClient.vb` → `Login()` 方法 |
| 调整超时、重试次数 | `AbfClient.vb` → `Search()` 方法中的常量/变量 |
| 修改图片下载逻辑 | `AbfClient.vb` → 图片下载相关代码 |

**如果需要增加或删除返回字段**（改变列数），需要同时改三个地方：

1. **`BearingResult.vb`**：增删属性
2. **`BearingResult.vb`**：修改 `ToFields()` 方法，把新属性加入/移出返回数组
3. **`AbfClient.vb`**：在详情页解析逻辑中为新字段赋值

**具体示例：新增第 22 列"供应商编码"**

**第一步** — 在 `BearingResult.vb` 的 `SuffixDescription` 属性之后插入：

```vb
''' <summary>22. 供应商编码（新增）</summary>
Public Property SupplierCode As String = "|"
```

**第二步** — 修改同文件的 `ToFields()` 方法，在末尾加入新属性：

```vb
Friend Function ToFields() As String()
    Return New String() {
        ResultName,
        ShortDescription,
        Price,
        Stock,
        EquivalentModels,
        ProductDescription,
        AlsoKnownAs,
        Category,
        InnerDiameter,
        OuterDiameter,
        Width,
        Weight,
        Bore,
        Seal,
        CageType,
        ExternalModification,
        RadialInternalPlay,
        Precision,
        HeatStabilization,
        ImageNames,
        SuffixDescription,
        SupplierCode       ' ← 新增，使用方用 arr(i, 22) 读取
    }
End Function
```

**第三步** — 在 `AbfClient.vb` 的详情页解析位置为新字段赋值（以 XPath 读取为例）：

```vb
Dim supplierNode = doc.DocumentNode.SelectSingleNode("//span[@class='supplier-code']")
result.SupplierCode = If(supplierNode IsNot Nothing, supplierNode.InnerText.Trim(), "|")
```

**第四步** — 重新编译，发给使用方。使用方用 `arr(i, 22)` 读取新字段。

**修改完后重新执行编译命令**，拿到新的 `AbfScraper.dll` 替换旧的即可。

---

## 使用指南（面向拿到 DLL 的客户）

### 1. 收到的文件

你会拿到两个文件：

```
AbfScraper.dll
HtmlAgilityPack.dll
```

把它们放在同一个文件夹里（路径随意，如 `C:\libs\`）。

### 2. 在项目中引用 DLL

#### 方式 A：Visual Studio（Windows Forms / WPF / 控制台项目）

1. 在**解决方案资源管理器**中右键点击你的项目 → **添加** → **引用…**（或 **Add Reference…**）
2. 点击左侧 **浏览**（Browse），点击右下角 **浏览…** 按钮
3. 找到 `AbfScraper.dll` 和 `HtmlAgilityPack.dll`，选中两个文件，点击**添加**
4. 确认两个 DLL 前面打了勾 ✅，点击**确定**

#### 方式 B：直接编辑 .vbproj / .csproj 文件

在项目文件中添加以下引用（路径改成你实际放 DLL 的位置）：

```xml
<ItemGroup>
  <Reference Include="AbfScraper">
    <HintPath>C:\libs\AbfScraper.dll</HintPath>
  </Reference>
  <Reference Include="HtmlAgilityPack">
    <HintPath>C:\libs\HtmlAgilityPack.dll</HintPath>
  </Reference>
</ItemGroup>
```

#### 方式 C：.NET CLI（dotnet 命令行项目）

对于使用 `dotnet new console` 等创建的新项目，编辑 `.vbproj` 文件加入上面的 `<ItemGroup>` 即可。

### 3. 引用后的基本用法

在代码文件顶部加一行引入命名空间：

```vb
Imports AbfScraper
```

然后就可以使用了。以下是一个完整的从零开始的示例。

#### 完整示例：控制台项目

**1) 创建项目（命令行方式）：**

```
dotnet new console -lang VB -n MyBearingApp
cd MyBearingApp
```

**2) 编辑 `MyBearingApp.vbproj`，添加 DLL 引用：**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MyBearingApp</RootNamespace>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="AbfScraper">
      <HintPath>C:\libs\AbfScraper.dll</HintPath>
    </Reference>
    <Reference Include="HtmlAgilityPack">
      <HintPath>C:\libs\HtmlAgilityPack.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

**3) 编辑 `Program.vb`：**

```vb
Imports AbfScraper

Module Program
    Sub Main()
        Try
            ' —— 创建客户端 ——
            ' 不需要翻译时：New AbfClient()
            ' 需要翻译时填入百度翻译 Key：
            Using client As New AbfClient(
                    baiduAppId:="你的AppId",
                    baiduSecretKey:="你的SecretKey")

                ' —— 登录（必须先登录才能搜索）——
                Console.WriteLine("正在登录...")
                If Not client.Login("你的邮箱", "你的密码") Then
                    Console.WriteLine("登录失败，请检查账号密码")
                    Return
                End If
                Console.WriteLine("登录成功")

                ' —— 搜索 ——
                Console.WriteLine("正在搜索...")
                Dim arr = client.Search(
                    model:="6205",             ' 搜索型号
                    brand:="SKF",              ' 搜索品牌
                    matchMode:=1,              ' 开头匹配
                    timeoutSeconds:=120,       ' 超时 120 秒
                    maxResults:=5,             ' 最多返回 5 条
                    enableTranslation:=True,   ' 翻译为中文
                    imageSavePath:="C:\img\",  ' 图片保存到这个目录
                    maxConcurrent:=3)          ' 3 个并发

                ' —— 检查搜索状态 ——
                Console.WriteLine($"搜索状态: {client.LastSearchInfo}")

                ' —— 读取结果 ——
                Dim count = arr.GetLength(0)
                If count = 0 Then
                    Console.WriteLine("没有搜到结果")
                    Return
                End If

                Console.WriteLine($"网站总命中: {arr(0, 0)} 条，本次返回: {count} 条")
                Console.WriteLine()

                For i = 0 To count - 1
                    Console.WriteLine($"===== 第 {i + 1} 条 =====")
                    Console.WriteLine($"型号:     {arr(i, 1)}")
                    Console.WriteLine($"简述:     {arr(i, 2)}")

                    ' 拆分价格
                    Dim priceParts = arr(i, 3).Split("|"c)
                    Console.WriteLine($"原价:     {priceParts(0)}")
                    Console.WriteLine($"折扣价:   {If(priceParts.Length > 1, priceParts(1), "无")}")

                    Console.WriteLine($"库存:     {arr(i, 4)}")

                    ' 拆分类别英/中
                    Dim catParts = arr(i, 8).Split("|"c)
                    Console.WriteLine($"类别(英): {catParts(0)}")
                    Console.WriteLine($"类别(中): {If(catParts.Length > 1, catParts(1), "")}")

                    Console.WriteLine($"内径:     {arr(i, 9)} mm")
                    Console.WriteLine($"外径:     {arr(i, 10)} mm")
                    Console.WriteLine($"宽度:     {arr(i, 11)} mm")
                    Console.WriteLine($"重量:     {arr(i, 12)} kg")
                    Console.WriteLine($"图片:     {arr(i, 20)}")
                    Console.WriteLine()
                Next

            End Using
        Catch ex As Exception
            Console.WriteLine("出错了：" & ex.Message)
        End Try

        Console.WriteLine("按任意键退出...")
        Console.ReadKey()
    End Sub
End Module
```

**4) 运行：**

```
dotnet run
```

#### 完整示例：Windows Forms 项目

假设窗体上有一个按钮 `btnSearch`、一个文本框 `txtModel`、一个 DataGridView `dgvResults`：

> ⚠️ `Login()` 最长阻塞 120 秒。下面示例将登录放在后台线程，避免 UI 冻结。

```vb
Imports AbfScraper
Imports System.Threading.Tasks

Public Class Form1

    Private _client As AbfClient

    Private Async Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        btnSearch.Enabled = False

        ' 在后台线程登录，避免阻塞 UI
        _client = New AbfClient(baiduAppId:="你的AppId", baiduSecretKey:="你的SecretKey")
        Dim ok = Await Task.Run(Function() _client.Login("你的邮箱", "你的密码"))

        If Not ok Then
            MessageBox.Show("登录失败")
        Else
            btnSearch.Enabled = True
        End If
    End Sub

    Private Async Sub btnSearch_Click(sender As Object, e As EventArgs) Handles btnSearch.Click
        Dim model = txtModel.Text.Trim()
        If model = "" Then
            MessageBox.Show("请输入型号")
            Return
        End If

        btnSearch.Enabled = False

        ' 在后台线程搜索，避免阻塞 UI
        Dim arr = Await Task.Run(Function()
            Return _client.Search(model:=model, brand:="", maxResults:=20, maxConcurrent:=3)
        End Function)

        btnSearch.Enabled = True

        If arr.GetLength(0) = 0 Then
            MessageBox.Show("无结果")
            Return
        End If

        ' 填充 DataGridView
        Dim dt As New DataTable()
        dt.Columns.Add("型号")
        dt.Columns.Add("价格")
        dt.Columns.Add("库存")
        dt.Columns.Add("内径")
        dt.Columns.Add("外径")
        dt.Columns.Add("宽度")

        For i = 0 To arr.GetLength(0) - 1
            dt.Rows.Add(
                arr(i, 1),
                arr(i, 3),
                arr(i, 4),
                arr(i, 9),
                arr(i, 10),
                arr(i, 11))
        Next

        dgvResults.DataSource = dt

        If _client.LastSearchInfo <> "OK" Then
            MessageBox.Show($"搜索状态: {_client.LastSearchInfo}")
        End If
    End Sub

    Private Sub Form1_FormClosed(sender As Object, e As FormClosedEventArgs) Handles MyBase.FormClosed
        _client?.Dispose()
    End Sub

End Class
```

#### 完整示例：C# 控制台项目

C# 语法与 VB 不同，**二维数组用 `arr[i, col]`（方括号）**，其他逻辑完全一致。

**1) 创建项目：**

```
dotnet new console -n MyBearingAppCS
cd MyBearingAppCS
```

**2) 编辑 `MyBearingAppCS.csproj`：**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="AbfScraper">
      <HintPath>C:\libs\AbfScraper.dll</HintPath>
      <Private>true</Private>
    </Reference>
    <Reference Include="HtmlAgilityPack">
      <HintPath>C:\libs\HtmlAgilityPack.dll</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>
</Project>
```

**3) 编辑 `Program.cs`：**

```csharp
using AbfScraper;

try
{
    using var client = new AbfClient(
        baiduAppId: "你的AppId",
        baiduSecretKey: "你的SecretKey");

    if (!client.Login("你的邮箱", "你的密码"))
    {
        Console.WriteLine("登录失败");
        return;
    }

    var arr = client.Search(
        model: "6205",
        brand: "SKF",
        matchMode: 1,
        timeoutSeconds: 120,
        maxResults: 5,
        enableTranslation: true,
        imageSavePath: @"C:\img\",
        maxConcurrent: 3);

    Console.WriteLine($"搜索状态: {client.LastSearchInfo}");

    int count = arr.GetLength(0);
    if (count == 0) { Console.WriteLine("无结果"); return; }

    Console.WriteLine($"总命中: {arr[0, 0]}  已返回: {count} 条");
    for (int i = 0; i < count; i++)
    {
        // C# 二维数组用 arr[行, 列]
        var price = arr[i, 3].Split('|');
        Console.WriteLine($"[{i}] {arr[i, 1]}");
        Console.WriteLine($"     原价={price[0]}  折扣={price[1]}  库存={arr[i, 4]}");
        Console.WriteLine($"     内径={arr[i, 9]} mm  外径={arr[i, 10]} mm");
    }
}
catch (Exception ex)
{
    Console.WriteLine("出错了：" + ex.Message);
}
```

**4) 运行：**

```
dotnet run
```

---

#### 完整示例：.NET Framework 4.x 项目（Visual Studio 老式项目）

使用 Visual Studio 创建的 Windows Forms / WPF / 控制台项目（`.vbproj` 非 SDK 格式）操作如下：

**引用步骤（Visual Studio 界面操作）：**

1. **解决方案资源管理器** → 右键项目 → **添加** → **引用…**
2. 切到 **浏览** 选项卡 → 点右下角 **浏览…** 按钮
3. 找到 `AbfScraper.dll` 和 `HtmlAgilityPack.dll`，同时选中两个文件 → **添加** → **确定**
4. 在解决方案资源管理器中展开 **引用** 节点，分别点击这两个引用 → 在底部**属性窗口**把 **「复制到本地」设为 True**（这样生成时 DLL 会自动复制到 `bin\Debug\`）

**目标框架要求：**

- 项目属性 → 应用程序 → 目标框架须为 **.NET Framework 4.6.1 或以上**
- 平台目标建议设为 **Any CPU**（避免 32 位进程的 TLS 限制）
- `AbfScraper.dll` 目标是 `netstandard2.0`，兼容 .NET Framework 4.6.1+，无需任何适配

代码写法与上面 VB 控制台示例完全相同，顶部加 `Imports AbfScraper` 即可。

---

#### 完整示例：VBA（Excel / Access）调用

VBA 无法直接调用 .NET DLL。推荐方案：用 .NET 控制台程序负责采集，结果写入 CSV 文件，VBA 再读取。

**流程示意：**

```
Excel VBA → Shell 启动 .NET exe（传入型号/品牌参数）
                       ↓
             .NET exe 调用 AbfScraper 采集
                       ↓
             结果写入 CSV 文件
                       ↓
              VBA 读取 CSV，写入工作表
```

**.NET 控制台程序（VB，接收命令行参数，结果写 CSV）：**

```vb
Imports AbfScraper
Imports System.IO

Module Program
    ' 用法: MyBearingApp.exe <型号> <品牌> <输出CSV路径>
    Sub Main(args As String())
        If args.Length < 3 Then
            Console.Error.WriteLine("用法: MyBearingApp.exe <型号> <品牌> <CSV路径>")
            Environment.Exit(1)
        End If

        Try
            Using client As New AbfClient()
                If Not client.Login("你的邮箱", "你的密码") Then
                    Console.Error.WriteLine("登录失败")
                    Environment.Exit(1)
                End If

                Dim arr = client.Search(
                    model:=args(0), brand:=args(1),
                    maxResults:=50, timeoutSeconds:=120, maxConcurrent:=3)

                Using sw As New StreamWriter(args(2), False, Text.Encoding.UTF8)
                    sw.WriteLine("型号,原价,折扣价,库存,内径,外径,宽度")
                    For i = 0 To arr.GetLength(0) - 1
                        Dim p = arr(i, 3).Split("|"c)
                        sw.WriteLine(
                            $"""{arr(i,1)}""," &
                            $"""{p(0)}""," &
                            $"""{If(p.Length > 1, p(1), "")}""," &
                            $"""{arr(i,4)}""," &
                            $"""{arr(i,9)}""," &
                            $"""{arr(i,10)}""," &
                            $"""{arr(i,11)}""")
                    Next
                End Using
            End Using
        Catch ex As Exception
            Console.Error.WriteLine("错误: " & ex.Message)
            Environment.Exit(1)
        End Try
    End Sub
End Module
```

把这个程序编译后得到 `MyBearingApp.exe`，然后在 Excel 中用以下 VBA 调用它：

**Excel VBA 调用代码：**

```vb
Sub SearchBearing()
    Dim model   As String : model   = Trim(Range("B1").Value)  ' B1 填型号
    Dim brand   As String : brand   = Trim(Range("B2").Value)  ' B2 填品牌
    Dim csvPath As String : csvPath = "C:\temp\result.csv"
    Dim exePath As String : exePath = "C:\tools\MyBearingApp.exe"

    If model = "" And brand = "" Then
        MsgBox "请在 B1 填型号，B2 填品牌"
        Exit Sub
    End If

    ' 删除旧结果
    If Dir(csvPath) <> "" Then Kill csvPath

    ' 启动 .NET 程序（同步等待）
    Dim cmd As String
    cmd = """" & exePath & """ """ & model & """ """ & brand & """ """ & csvPath & """"
    Dim ret As Long
    ret = Shell("cmd.exe /c " & cmd, vbHide)

    ' 等待 CSV 文件生成（最多等 3 分钟）
    Dim t As Date : t = Now
    Do While Dir(csvPath) = ""
        If Now - t > TimeValue("00:03:00") Then
            MsgBox "超时，未生成结果文件"
            Exit Sub
        End If
        Application.Wait Now + TimeValue("00:00:01")
    Loop
    Application.Wait Now + TimeValue("00:00:01")  ' 等文件写完

    ' 读取 CSV，写入 Sheet2
    Dim ws As Worksheet : Set ws = ThisWorkbook.Sheets("Sheet2")
    ws.Cells.Clear

    Dim fNum As Integer : fNum = FreeFile
    Open csvPath For Input As #fNum
    Dim r As Integer : r = 1
    Do While Not EOF(fNum)
        Dim line As String
        Line Input #fNum, line
        Dim cols() As String : cols = Split(line, ",")
        Dim c As Integer
        For c = 0 To UBound(cols)
            ws.Cells(r, c + 1).Value = Replace(cols(c), Chr(34), "")
        Next c
        r = r + 1
    Loop
    Close #fNum

    MsgBox "采集完成，共 " & r - 2 & " 条，数据已写入 Sheet2"
End Sub
```

---

## 常见问题

### Q1：运行时报"未能加载文件或程序集 AbfScraper / HtmlAgilityPack"

`AbfScraper.dll` 和 `HtmlAgilityPack.dll` 必须与最终运行的 `.exe` 在同一目录（或在 `bin\Debug\net8.0\` 等输出目录中）。

如果 DLL 没有被自动复制到输出目录，在 `.vbproj` / `.csproj` 的每个 `<Reference>` 节点下添加 `<Private>true</Private>`：

```xml
<Reference Include="AbfScraper">
  <HintPath>C:\libs\AbfScraper.dll</HintPath>
  <Private>true</Private>
</Reference>
<Reference Include="HtmlAgilityPack">
  <HintPath>C:\libs\HtmlAgilityPack.dll</HintPath>
  <Private>true</Private>
</Reference>
```

Visual Studio 用户也可以在引用的属性面板里直接把「复制到本地」设为 **True**，效果相同。

---

### Q2：dotnet build 报 NuGet 还原失败

网络受限时先执行：

```
dotnet restore --ignore-failed-sources
```

如果完全无法访问 nuget.org：

1. 在能联网的机器上执行 `dotnet restore`，把包缓存到本地（默认 `%USERPROFILE%\.nuget\packages`）
2. 把 `%USERPROFILE%\.nuget\packages\htmlagilitypack` 整个文件夹复制到受限机器的相同路径
3. 再执行 `dotnet build`

或者直接让开发方提供编译好的 `bin\Release\netstandard2.0\` 目录，不需要自己编译。

---

### Q3：Login() 返回 False 或抛出异常

| 现象 | 原因 | 处理方法 |
|---|---|---|
| 返回 `False` | 用户名或密码错误 | 去 abf.store 网站直接登录确认 |
| 抛"无法访问 ABF Store" | 网络不通 | 检查防火墙/代理，确认能访问 `https://www.abf.store` |
| 抛"找不到 CSRF Token" | 网站改版 | 联系开发方更新解析代码 |
| 抛 `ArgumentException` | 传入了空用户名或空密码 | 检查传参 |
| 120 秒后抛超时异常 | 服务器响应太慢 | 重试；检查网络延迟 |

---

### Q4：Search() 完成但中文字段全为空

1. 确认 `enableTranslation:=True` 已传入
2. 确认 `baiduAppId` 和 `baiduSecretKey` 在 `New AbfClient()` 时**同时传入**（两者均为空时不翻译）
3. 去 [fanyi-api.baidu.com](https://fanyi-api.baidu.com) 确认：配额未耗尽、应用状态正常、本机能访问该域名
4. 翻译失败时字段格式为 `"英文|"`（竖线后中文为空），英文部分仍有值，不影响采集结果

---

### Q5：Search() 超时，返回条数远少于预期

1. 增大 `timeoutSeconds`（结果多时建议传 `600`；批量采集可传 `0` = 无限等待）
2. 增大 `maxConcurrent`（推荐 `3`）可加速采集，降低超时概率
3. 若只是验证流程，先传 `maxResults:=5` 快速拿几条测试
4. 超时不抛异常，检查 `client.LastSearchInfo` 是否为 `"超时"`，已采集的部分数据仍会返回

---

### Q6：Session 过期（LastSearchInfo = "Session过期"）

重新调用 `Login()` 即可，不需要重建 `AbfClient`：

```vb
If client.LastSearchInfo = "Session过期" Then
    If Not client.Login("你的邮箱", "你的密码") Then
        MsgBox("重新登录失败")
        Return
    End If
    ' 继续后续操作
End If
```

---

## 已知脆弱性

| # | 位置 | 描述 | 影响 |
|:---:|---|---|:---:|
| F-1 | HTML 解析 | 所有字段依赖网站 HTML 结构（XPath 选择器）。若 abf.store 改版需同步更新 | 高 |
| F-2 | `ScanRowTexts` | 列表页折扣价依赖 DOM 顺序（划线原价须在现价之前）。详情页价格不受影响 | 低 |
| F-3 | `AbfTranslator` | 多实例同时运行且均开翻译时，`trans_dict.txt` 可能写入重复词条（无跨进程锁）。重复无害 | 极低 |

---

## 注意事项

1. 用 `Using` 语句（或手动调用 `.Dispose()`）确保 `AbfClient` 和 `AbfTranslator` 被正确释放
2. 建议用 `Try...Catch` 包裹 `Login()` 和 `Search()` 调用
3. `Login()` 传入空用户名或空密码会抛出 `ArgumentException`
4. 未调用 `Login()` 就调用 `Search()` 会抛出 `InvalidOperationException`
5. 图片命名规则：`型号字母数字全大写-结果序号-图片序号.扩展名`，如 `NU324ETVP2FAG-1-1.png`
6. `trans_dict.txt` 是翻译缓存文件，UTF-8 编码 Tab 分隔。可手动编辑添加专业术语，格式为 `英文<Tab>中文`，每行一条。文件示例：

   ```
   Deep groove ball bearing	深沟球轴承
   Cylindrical roller bearing	圆柱滚子轴承
   Angular contact ball bearing	角接触球轴承
   Tapered roller bearing	圆锥滚子轴承
   Spherical roller bearing	调心滚子轴承
   ```

   每行必须恰好含一个 Tab 字符分隔英文和中文，行尾不要有多余空格。文件由程序自动追加，手动预填充可减少首次运行时的翻译 API 调用次数
