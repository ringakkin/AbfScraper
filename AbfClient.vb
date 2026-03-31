Imports System
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Collections.Generic
Imports System.Text.RegularExpressions
Imports HtmlAgilityPack

''' <summary>
    ''' ABF Store 轴承数据采集客户端。
    '''
    ''' 典型用法：
    '''   Using client As New AbfClient()
    '''       Dim ok = client.Login("user@example.com", "password")
    '''       Dim results = client.Search("NU324-E-TVP2", "FAG", 0)
    '''   End Using
    ''' </summary>
    Public Class AbfClient
        Implements IDisposable

        Private Const BaseUrl As String = "https://www.abf.store"

        Private ReadOnly _handler As HttpClientHandler
        Private ReadOnly _http As HttpClient
        Private _isLoggedIn As Boolean = False
        Private ReadOnly _translator As AbfTranslator
        Private _enableTranslation As Boolean = False
        Private _searchTimeoutSec As Integer = 120

        ' ────────────────────────────────────────────────────
        '  构造函数
        ' ────────────────────────────────────────────────────

        ''' <param name="dictPath">持久化词典文件路径，留空则自动使用程序目录下的 trans_dict.txt</param>
        ''' <param name="translationProvider">翻译后端，默认 MyMemory（无需 Key）；国内推荐 Baidu。</param>
        ''' <param name="baiduAppId">百度翻译 AppId（仅 Baidu 模式需要）。</param>
        ''' <param name="baiduSecretKey">百度翻译 SecretKey（仅 Baidu 模式需要）。</param>
        Public Sub New(Optional dictPath As String = "",
                       Optional translationProvider As TranslationProvider = TranslationProvider.MyMemory,
                       Optional baiduAppId As String = "",
                       Optional baiduSecretKey As String = "")
            _translator = New AbfTranslator(translationProvider, baiduAppId, baiduSecretKey, dictPath)
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            _handler = New HttpClientHandler() With {
                .CookieContainer = New CookieContainer(),
                .UseCookies = True,
                .AllowAutoRedirect = True
            }
            _http = New HttpClient(_handler) With {
                .Timeout = System.Threading.Timeout.InfiniteTimeSpan
            }
            With _http.DefaultRequestHeaders
                .Add("User-Agent",
                     "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
                     "AppleWebKit/537.36 (KHTML, like Gecko) " &
                     "Chrome/122.0.0.0 Safari/537.36")
                .Add("Accept",
                     "text/html,application/xhtml+xml,application/xml;" &
                     "q=0.9,image/webp,*/*;q=0.8")
                .Add("Accept-Language", "en-US,en;q=0.9")
            End With
        End Sub

        ' ────────────────────────────────────────────────────
        '  公开方法
        ' ────────────────────────────────────────────────────

        ''' <summary>
        ''' 登录 ABF Store，必须在 Search() 前调用。
        ''' </summary>
        ''' <param name="username">注册邮箱</param>
        ''' <param name="password">密码</param>
        ''' <returns>True = 登录成功</returns>
        Public Function Login(username As String, password As String) As Boolean
            If String.IsNullOrWhiteSpace(username) Then
                Throw New ArgumentException("username 不能为空", NameOf(username))
            End If
            If String.IsNullOrWhiteSpace(password) Then
                Throw New ArgumentException("password 不能为空", NameOf(password))
            End If

            ' ── Step 1：GET 首页，取 CSRF Token ──
            Dim homeHtml As String
            Try
                homeHtml = FetchHtml(BaseUrl & "/s/en/")
            Catch ex As Exception
                Throw New Exception("登录失败：无法访问 ABF Store。" & ex.Message, ex)
            End Try

            Dim doc As New HtmlDocument()
            doc.LoadHtml(homeHtml)

            Dim tokenNode = doc.DocumentNode.SelectSingleNode(
                "//input[@name='__RequestVerificationToken']")
            If tokenNode Is Nothing Then
                Throw New Exception(
                    "登录失败：找不到 CSRF Token，页面结构可能已变更。")
            End If
            Dim token = tokenNode.GetAttributeValue("value", "")

            ' ── Step 2：POST 登录 ──
            Dim formData As New List(Of KeyValuePair(Of String, String)) From {
                New KeyValuePair(Of String, String)(
                    "__RequestVerificationToken", token),
                New KeyValuePair(Of String, String)("username", username),
                New KeyValuePair(Of String, String)("password", password),
                New KeyValuePair(Of String, String)("remember", "true"),
                New KeyValuePair(Of String, String)("redirectUri", "")
            }

            Dim request As New HttpRequestMessage(
                HttpMethod.Post, BaseUrl & "/s/data/auth/login")
            request.Content = New FormUrlEncodedContent(formData)
            request.Headers.Referrer = New Uri(BaseUrl & "/s/en/")

            Dim response As HttpResponseMessage
            Using cts1 As New System.Threading.CancellationTokenSource(
                    If(_searchTimeoutSec <= 0, System.Threading.Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(_searchTimeoutSec)))
                response = _http.SendAsync(request, cts1.Token).GetAwaiter().GetResult()
            End Using
            Dim body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

            ' 响应 JSON 示例：{"IsValid":true,"RedirectUri":null,...}
            _isLoggedIn = body.IndexOf("""IsValid"":true",
                                       StringComparison.OrdinalIgnoreCase) >= 0

            ' ── Step 3：登录成功后确保货币为 EUR ──
            If _isLoggedIn Then
                Try
                    Dim currReq As New HttpRequestMessage(
                        HttpMethod.Post, BaseUrl & "/s/data/currency")
                    currReq.Content = New FormUrlEncodedContent(
                        New List(Of KeyValuePair(Of String, String)) From {
                            New KeyValuePair(Of String, String)("currency", "EUR")
                        })
                    currReq.Headers.Referrer = New Uri(BaseUrl & "/s/en/")
                    Using cts2 As New System.Threading.CancellationTokenSource(
                            If(_searchTimeoutSec <= 0, System.Threading.Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(_searchTimeoutSec)))
                        _http.SendAsync(currReq, cts2.Token).GetAwaiter().GetResult()
                    End Using
                Catch
                    ' 货币设置失败不阻断流程，继续应用帐号默认货币
                End Try
            End If

            Return _isLoggedIn
        End Function

        ''' <summary>
        ''' 搜索轴承，返回所有匹配结果（含详情页完整数据）。
        ''' </summary>
        ''' <param name="model">轴承型号，例如 "NU324-E-TVP2"</param>
        ''' <param name="brand">品牌，例如 "FAG"</param>
        ''' <param name="matchMode">匹配模式：0 = 包含(Contains)；1 = 开头(Starts With，默认)；2 = 完全匹配(Exact Match)</param>
        ''' <param name="timeoutSeconds">整次 Search() 调用的总超时秒数（含所有分页+详情页请求），默认 120 秒；结果多时请调大，0 或负数表示无限等待。</param>
        ''' <param name="maxResults">最多返回条数，0 表示不限制（返回全部）。实际结果少于该値时返回实际数量。</param>
        ''' <param name="enableTranslation">是否将英文字段翻译为中文，默认 False。</param>
        ''' <param name="imageSavePath">图片存储路径（可选）。不为空时自动创建目录并下载图片，留空则不下载。</param>
        ''' <returns>
        ''' 二维字符串数组 String(,)，行下标从 0 起（结果序号），列下标从 1 起（字段序号 1~21）。
        ''' arr(i,0)=总条数，arr(i,1)=结果型号，arr(0,2)=第一条简述，arr(1,1)=第二条结果…
        ''' 若无结果则返回 String(0,0){}。
        ''' </returns>
        Public Function Search(model As String,
                               brand As String,
                               Optional matchMode As Integer = 1,
                               Optional timeoutSeconds As Integer = 120,
                               Optional maxResults As Integer = 0,
                               Optional enableTranslation As Boolean = False,
                               Optional imageSavePath As String = "") As String(,)
            _searchTimeoutSec = timeoutSeconds
            _enableTranslation = enableTranslation
            If Not _isLoggedIn Then
                Throw New InvalidOperationException(
                    "尚未登录，请先调用 Login() 方法。")
            End If

            Using _cts As New System.Threading.CancellationTokenSource(
                    If(_searchTimeoutSec <= 0, System.Threading.Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(_searchTimeoutSec)))
            Dim token = _cts.Token

            ' ── Step 1：构造搜索 URL ──
            Dim matchParam = MapMatchMode(matchMode)
            Dim query = Uri.EscapeDataString(
                            (model.Trim() & " " & brand.Trim()).Trim())

            ' ── Step 2：提取搜索结果列表中所有详情页的 href（含分页循环）──
            ' p=页码（1-indexed），mx=每页条数；每次取50条，直到当页原始行数 < mx 为止
            ' os=库存过滤器：0=全部 1=有货 2=限时优惠，固定为 0（不过滤）
            Const PageSize As Integer = 50
            Dim rowMeta As New Dictionary(Of String, String())
            Dim seen As New HashSet(Of String)
            Dim detailHrefs As New List(Of String)
            Dim pageNum As Integer = 1
            Dim totalProductCount As Integer = 0

            Do
                Dim searchUrl = $"{BaseUrl}/s/en/search/" &
                                $"?st=text&t={matchParam}&q={query}" &
                                $"&ob=0&vw=basic&os=0&mx={PageSize}&p={pageNum}"

                Dim html As String
                Try
                    html = FetchHtml(searchUrl, token)
                Catch ex As OperationCanceledException
                    Exit Do
                Catch ex As Exception
                    Throw New Exception("搜索请求失败：" & ex.Message, ex)
                End Try

                Dim doc As New HtmlDocument()
                doc.LoadHtml(html)

                ' 首页时解析网站公布的总条数（如 "13,791 products found"）
                If pageNum = 1 Then
                    Dim countM = Regex.Match(html, "(\d[\d,\.]*)\s+products?\s+found", RegexOptions.IgnoreCase)
                    If countM.Success Then
                        Dim raw = Regex.Replace(countM.Groups(1).Value, "[^\d]", "")
                        Integer.TryParse(raw, totalProductCount)
                    End If
                End If

                Dim rows = doc.DocumentNode.SelectNodes(
                    "//li[contains(@class,'result-row')]")
                Dim rawRowCount As Integer = If(rows IsNot Nothing, rows.Count, 0)
                Dim prevCount = detailHrefs.Count
                If rows IsNot Nothing Then
                    For Each row In rows
                    ' ── 定位产品详情页 href ──
                    Dim rowHref = ""
                    Dim listResultName = ""
                    Dim links = row.SelectNodes(".//a[@href]")
                    If links IsNot Nothing Then
                        For Each link In links
                            Dim href = link.GetAttributeValue("href", "")
                            If href.StartsWith("/s/en/bearings/",
                                               StringComparison.OrdinalIgnoreCase) AndAlso
                               Not href.Contains("?") AndAlso
                               Not href.Contains("#") Then
                                rowHref = href
                                ' 取 <a> 内文本，包含 <span class="highlight">（搜索词高亮）
                                ' 但排除 <span class="product-desc">aka ...</span>
                                Dim txtParts As New System.Text.StringBuilder()
                                For Each cn In link.ChildNodes
                                    If cn.NodeType = HtmlNodeType.Text Then
                                        txtParts.Append(cn.InnerText)
                                    ElseIf cn.NodeType = HtmlNodeType.Element AndAlso
                                           Not cn.GetAttributeValue("class", "").Contains("product-desc") Then
                                        txtParts.Append(cn.InnerText)
                                    End If
                                Next
                                Dim txt = HtmlEntity.DeEntitize(txtParts.ToString().Trim())
                                If Not String.IsNullOrEmpty(txt) Then
                                    listResultName = txt
                                    Exit For
                                End If
                            End If
                        Next
                    End If
                    If String.IsNullOrEmpty(rowHref) Then Continue For
                    If Not seen.Add(rowHref) Then Continue For
                    detailHrefs.Add(rowHref)

                    ' ── 从行内文本提取库存、价格、简述 ──
                    Dim listStock = ""
                    Dim listPrice = ""
                    Dim listDiscountPrice = ""
                    ScanRowTexts(row, listStock, listPrice, listDiscountPrice)
                    Dim shortDescNode = row.SelectSingleNode(
                        ".//p[contains(@class,'product-desc')]")
                    Dim listShortDesc = ""
                    If shortDescNode IsNot Nothing Then
                        listShortDesc = HtmlEntity.DeEntitize(
                            shortDescNode.InnerText.Trim())
                        ' 去掉 "13.3 kg · " 这样的重量前缀，只保留 · 后的描述文字
                        ' 若无 " · "，说明节点内只有重量，无描述，置空避免把重量填入简介
                        Dim dotIdx = listShortDesc.IndexOf(" · ")
                        If dotIdx >= 0 Then
                            listShortDesc = listShortDesc.Substring(dotIdx + 3).Trim()
                        Else
                            listShortDesc = ""
                        End If
                    End If
                    rowMeta(rowHref) = New String() {listStock, listPrice, listShortDesc, listResultName, listDiscountPrice}
                Next
                End If

                ' 当页原始行数不足 PageSize，说明已是最后一页
                If rawRowCount < PageSize Then Exit Do
                ' 当页无新增 href（服务器返回重复内容，分页失效），立即退出
                If detailHrefs.Count = prevCount Then Exit Do
                ' 已收集足够多的 href 时提前退出分页，避免多余请求
                If maxResults > 0 AndAlso detailHrefs.Count >= maxResults Then Exit Do
                pageNum += 1
            Loop

            ' 按 maxResults 截断结果列表
            If maxResults > 0 AndAlso detailHrefs.Count > maxResults Then
                detailHrefs = detailHrefs.GetRange(0, maxResults)
            End If

            ' ── Step 3：逐一抓取详情页 ──
            Dim results As New List(Of BearingResult) ' 内部仍用列表，最后转二维数组
            Dim resultIndex As Integer = 1
            For Each href In detailHrefs
                Dim detailUrl = BaseUrl & href
                Try
                    Dim result = FetchDetail(detailUrl, imageSavePath, resultIndex, token)
                    result.DetailUrl = detailUrl

                    ' 合并列表页预采集的库存、价格、简述
                    Dim meta() As String = Nothing
                    If rowMeta.TryGetValue(href, meta) Then
                        If result.Stock = "|" AndAlso Not String.IsNullOrEmpty(meta(0)) Then
                            result.Stock = meta(0)
                        End If
                        ' Price 格式：原价|折扣价（仅在 FetchDetail 未采集到价格时使用列表页数据，避免覆盖详情页已获取的折扣价）
                        If result.Price = "|" AndAlso Not String.IsNullOrWhiteSpace(meta(1)) Then
                            Dim discPart = If(meta.Length > 4, meta(4), "")
                            result.Price = meta(1) & "|" & discPart
                        End If
                        If meta.Length > 2 AndAlso Not String.IsNullOrEmpty(meta(2)) Then
                            result.ShortDescription = meta(2)
                        End If
                        If meta.Length > 3 AndAlso Not String.IsNullOrEmpty(meta(3)) Then
                            result.ResultName = meta(3)
                        End If
                    End If

                    results.Add(result)
                Catch ex As OperationCanceledException
                    Exit For
                Catch ex As Exception
                    ' 单条失败不影响整体，返回含错误信息的占位对象
                    Dim errResult As New BearingResult() With {
                        .DetailUrl = detailUrl,
                        .ShortDescription = "[Error] " & ex.Message
                    }
                    results.Add(errResult)
                End Try
                resultIndex += 1
            Next

            ' ── 转换为二维字符串数组（行 0-based，列 0-based）──
            ' arr(i,0)=总条数，arr(i,1)~arr(i,21) = 字段 1~21
            Dim n = results.Count
            If n = 0 Then Return DirectCast(Array.CreateInstance(GetType(String), 0, 22), String(,))
            Dim arr(n - 1, 21) As String
            For i = 0 To n - 1
                Dim fields = results(i).ToFields()
                For j = 1 To 21
                    arr(i, j) = fields(j - 1)
                Next
                ' arr(i,0) 存放总条目数，arr(i,1) 仅为 ResultName
                arr(i, 0) = If(totalProductCount > 0, totalProductCount.ToString(), n.ToString())
            Next
            Return arr
            End Using
        End Function

        ' ────────────────────────────────────────────────────
        '  私有方法
        ' ────────────────────────────────────────────────────

        Private Function FetchDetail(detailUrl As String,
                                     Optional imageSavePath As String = "",
                                     Optional resultIndex As Integer = 1,
                                     Optional ct As System.Threading.CancellationToken = Nothing) As BearingResult
            Dim result As New BearingResult()

            Dim html = FetchHtml(detailUrl, ct)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)

            ' 产品名称（h1）
            Dim h1 = doc.DocumentNode.SelectSingleNode("//h1")
            If h1 IsNot Nothing Then
                result.ProductName = HtmlEntity.DeEntitize(h1.InnerText.Trim())
            End If

            ' 键值对解析
            ' HTML 结构：
            '   <li class="flex border-top border-light-gray py1">
            '     <span class="col-4 pr2 italic">Label</span>   ← Specifications/Properties
            '     <span class="col-8">Value</span>
            '   </li>
            ' Suffix description 节的标签列为 col-2，值列为 col-10，同样处理。
            Dim liNodes = doc.DocumentNode.SelectNodes(
                "//li[contains(@class,'flex')" &
                " and contains(@class,'border-top')" &
                " and contains(@class,'border-light-gray')]")

            If liNodes IsNot Nothing Then
                For Each li In liNodes
                    Dim labelNode = li.SelectSingleNode(
                        "./span[contains(@class,'italic')]")
                    Dim valueNode = li.SelectSingleNode(
                        "./span[contains(@class,'col-8')" &
                        " or contains(@class,'col-10')]")

                    If labelNode IsNot Nothing AndAlso
                       valueNode IsNot Nothing Then
                        Dim label = HtmlEntity.DeEntitize(
                                        labelNode.InnerText.Trim())
                        Dim value = HtmlEntity.DeEntitize(
                                        valueNode.InnerText.Trim())
                        If Not String.IsNullOrWhiteSpace(label) Then
                            result.RawData(label) = value
                        End If
                    End If
                Next
            End If

            ' 产品描述（英文）
            ' 外层 <p id="descriptiontext"> 内嵌套 <p>，HtmlAgilityPack 会自动关闭外层 <p>，
            ' 实际内容落在 following-sibling <p> 上，需优先读兄弟节点。
            Dim descNode = doc.DocumentNode.SelectSingleNode(
                "//p[@id='descriptiontext']")
            If descNode IsNot Nothing Then
                Dim descEn = HtmlEntity.DeEntitize(descNode.InnerText.Trim())
                If String.IsNullOrEmpty(descEn) Then
                    ' 内层 <p> 被解析为兄弟节点，拼接所有 following-sibling <p>
                    Dim siblings = doc.DocumentNode.SelectNodes(
                        "//p[@id='descriptiontext']/following-sibling::p")
                    If siblings IsNot Nothing AndAlso siblings.Count > 0 Then
                        Dim parts = New List(Of String)()
                        For Each sib In siblings
                            Dim t = HtmlEntity.DeEntitize(sib.InnerText.Trim())
                            If Not String.IsNullOrEmpty(t) Then parts.Add(t)
                        Next
                        descEn = String.Join(" ", parts)
                    End If
                End If
                If Not String.IsNullOrEmpty(descEn) Then
                    result.ProductDescription = descEn & "|"
                End If
            End If

            ' ── 从 RawData 填充强类型属性 ──────────────────────
            Dim rawStock = result.GetRaw("Availability")
            If Not String.IsNullOrEmpty(rawStock) Then result.Stock = rawStock
            ' Category 格式：英文|中文（始终保留 |）
            Dim catEn = result.GetRaw("Category")
            If Not String.IsNullOrEmpty(catEn) Then result.Category = catEn & "|"  ' 有值则覆盖默认的 "|"
            Dim rawInner = result.GetRaw("Inner (d) MM")
            If Not String.IsNullOrEmpty(rawInner) Then result.InnerDiameter = rawInner
            Dim rawOuter = result.GetRaw("Outer (D) MM")
            If Not String.IsNullOrEmpty(rawOuter) Then result.OuterDiameter = rawOuter
            Dim rawWidth = result.GetRaw("Width (B) MM")
            If Not String.IsNullOrEmpty(rawWidth) Then result.Width = rawWidth
            Dim rawWeight = result.GetRaw("Weight (kg)")
            If Not String.IsNullOrEmpty(rawWeight) Then result.Weight = rawWeight
            ' 以下字段格式均为 原文|中文，有值则覆盖默认的 "|"
            Dim rawBore = result.GetRaw("Bore")
            If Not String.IsNullOrEmpty(rawBore) Then result.Bore = rawBore & "|"
            Dim rawSeal = result.GetRaw("Seal")
            If Not String.IsNullOrEmpty(rawSeal) Then result.Seal = rawSeal & "|"
            Dim rawCage = result.GetRaw("Cage Type")
            If Not String.IsNullOrEmpty(rawCage) Then result.CageType = rawCage & "|"
            Dim rawExtMod = result.GetRaw("External Modification")
            If Not String.IsNullOrEmpty(rawExtMod) Then result.ExternalModification = rawExtMod & "|"
            Dim rawRip = result.GetRaw("Radial Internal Play")
            If Not String.IsNullOrEmpty(rawRip) Then result.RadialInternalPlay = rawRip & "|"
            Dim rawPrec = result.GetRaw("Precision")
            If Not String.IsNullOrEmpty(rawPrec) Then result.Precision = rawPrec & "|"
            Dim rawHeat = result.GetRaw("Heat Stabilization")
            If Not String.IsNullOrEmpty(rawHeat) Then result.HeatStabilization = rawHeat & "|"
            ' AlsoKnownAs
            Dim rawAlso = result.GetRaw("Also known as")
            If Not String.IsNullOrEmpty(rawAlso) Then result.AlsoKnownAs = rawAlso

            ' 从详情页提取原价/折扣价
            ' 主策略：#former-price（原价）+ #detail-price（现价/折扣价）
            ' 备用策略：从 AddToBasketModel JSON 提取 ItemPriceInEuros，
            '           但只搜索 <section id="equivalents"> 之前的 HTML，
            '           避免误抓等效型号的价格
            Dim formerNode = doc.DocumentNode.SelectSingleNode("//span[@id='former-price']")
            Dim detailPriceNode = doc.DocumentNode.SelectSingleNode("//span[@id='detail-price']")
            If detailPriceNode IsNot Nothing Then
                ' data-defaultpricevalue 始终是 EUR 数值（与会话货币无关）
                Dim defaultValStr = detailPriceNode.GetAttributeValue("data-defaultpricevalue", "").Trim()
                Dim eurCurrentNum As Double
                Dim hasEurVal = Not String.IsNullOrEmpty(defaultValStr) AndAlso
                                Double.TryParse(defaultValStr, Globalization.NumberStyles.Any,
                                                Globalization.CultureInfo.InvariantCulture,
                                                eurCurrentNum) AndAlso eurCurrentNum > 0
                ' 构建当前 EUR 价格文本：优先用数值属性，否则退化到 InnerText
                Dim currentEurPrice As String
                If hasEurVal Then
                    currentEurPrice = ChrW(8364) & " " &
                        eurCurrentNum.ToString("F2", Globalization.CultureInfo.InvariantCulture) _
                        .Replace(".", ",")
                Else
                    currentEurPrice = NormalizeEuro(HtmlEntity.DeEntitize(detailPriceNode.InnerText.Trim()))
                End If
                ' 排除 "Price on request" 等非价格文本
                If currentEurPrice.Length > 0 AndAlso
                   Char.GetUnicodeCategory(currentEurPrice(0)) = Globalization.UnicodeCategory.CurrencySymbol Then
                    Dim formerEurPrice = ""
                    If formerNode IsNot Nothing Then
                        Dim formerRaw = NormalizeEuro(
                            HtmlEntity.DeEntitize(formerNode.InnerText.Trim()))
                        ' a. 已是 EUR：直接使用
                        If formerRaw.Length > 0 AndAlso formerRaw(0) = ChrW(8364) Then
                            formerEurPrice = formerRaw
                        ' b. 非 EUR 且有 EUR 数值属性：按显示比例换算 EUR 原价
                        ElseIf hasEurVal Then
                            Dim currentDisplayed = NormalizeEuro(
                                HtmlEntity.DeEntitize(detailPriceNode.InnerText.Trim()))
                            Dim formerNum, currentNum As Double
                            If ExtractNumericPrice(formerRaw, formerNum) AndAlso
                               ExtractNumericPrice(currentDisplayed, currentNum) AndAlso
                               currentNum > 0 Then
                                Dim eurFormerNum = eurCurrentNum * (formerNum / currentNum)
                                formerEurPrice = ChrW(8364) & " " &
                                    eurFormerNum.ToString("F2",
                                        Globalization.CultureInfo.InvariantCulture).Replace(".", ",")
                            End If
                        End If
                    End If
                    If Not String.IsNullOrEmpty(formerEurPrice) Then
                        result.Price = formerEurPrice & "|" & currentEurPrice
                    End If
                    ' formerEurPrice 为空时保留默认 "|"，让列表页 merge 提供原价|折扣价
                End If
            Else
                ' 备用：JSON 正则，只搜索 equivalents 区块之前的 HTML
                ' 仅在 BasketAddable:true 时使用（询价产品 BasketAddable:false，价格不应采集）
                Dim equivSectionIdx = html.IndexOf("<section id=""equivalents""",
                                                   StringComparison.OrdinalIgnoreCase)
                Dim priceHtml = If(equivSectionIdx > 0,
                                   html.Substring(0, equivSectionIdx), html)
                Dim basketM = Regex.Match(priceHtml,
                    """BasketAddable""\s*:\s*(true|false)",
                    RegexOptions.IgnoreCase)
                Dim isBasketAddable = Not basketM.Success OrElse
                                      basketM.Groups(1).Value.Equals("true",
                                          StringComparison.OrdinalIgnoreCase)
                If isBasketAddable Then
                    Dim priceM = Regex.Match(priceHtml, """ItemPriceInEuros""\s*:\s*([\d.]+)")
                    If priceM.Success Then
                        Dim priceVal As Double
                        If Double.TryParse(priceM.Groups(1).Value,
                                           Globalization.NumberStyles.Float,
                                           Globalization.CultureInfo.InvariantCulture,
                                           priceVal) AndAlso priceVal > 0 Then
                            Dim currentText = "€ " & priceVal.ToString("F2",
                                Globalization.CultureInfo.InvariantCulture).Replace(".", ",")
                            ' 只有当 #former-price 文本已是 EUR 时才拼入（无 data-defaultpricevalue 无法换算）
                            Dim jsonFormerText = If(formerNode IsNot Nothing,
                                NormalizeEuro(HtmlEntity.DeEntitize(formerNode.InnerText.Trim())), "")
                            If jsonFormerText.Length > 0 AndAlso jsonFormerText(0) = ChrW(8364) Then
                                result.Price = jsonFormerText & "|" & currentText
                            End If
                            ' 否则保留默认 "|"，让列表页 merge 提供原价|折扣价
                        End If
                    End If
                End If
            End If

            ' SuffixDescription：将所有后缀 key=value 拼接，格式：后缀1=说明1|后缀2=说明2|…
            ' 后缀条目特征：不属于常规 Specifications 字段，且 key 往往是简短大写编码
            ' 此处简化规则：不属于已知字段的 RawData 条目就当作后缀拆分拼入
            Dim knownKeys As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                "Availability", "Brand", "Item Number", "Manufacturer Part Number",
                "Also known as", "EAN", "Category", "Pairing",
                "Axial Internal Clearance / Preload", "Lubrication", "Medias description",
                "Radial Internal Play", "Precision", "Heat Stabilization",
                "Inner (d) MM", "Outer (D) MM", "Width (B) MM",
                "Inner (d) Inch", "Outer (D) Inch", "Width (B) Inch",
                "Weight (kg)", "Bore", "Seal", "Cage Type", "External Modification",
                "Basic Static Load Rating", "Basic Dynamic Load Rating",
                "Limiting Speed", "Reference Speed",
                "ECLASS", "ECLASS2"
            }
            Dim suffixParts As New List(Of String)
            For Each kv In result.RawData
                If Not knownKeys.Contains(kv.Key) Then
                    ' 格式：KEY|英文|中文
                    Dim keyVal = kv.Key
                    Dim engVal = kv.Value
                    Dim zhVal = Translate(engVal)
                    suffixParts.Add($"{keyVal}|{engVal}|{zhVal}")
                End If
            Next
            result.SuffixDescription = If(suffixParts.Count > 0, String.Join("|", suffixParts), "|")

            ' ── 等效型号区块 ──────────────────────────────────
            result.EquivalentModels = ParseEquivalents(doc, ct)

            ' ── 图片下载 ─────────────────────────────────────────
            ' 优先取 gallery-thumbs（缩略图条），没有则退回 gallery-top（主图轮播），
            ' 再没有则取页面内所有来自 content.abf.store 域名的图片。
            Dim imgNodes = doc.DocumentNode.SelectNodes(
                "//div[contains(@class,'gallery-thumbs')]//img[@src]")
            If imgNodes Is Nothing OrElse imgNodes.Count = 0 Then
                imgNodes = doc.DocumentNode.SelectNodes(
                    "//div[contains(@class,'gallery-top')]//img[@src]")
            End If
            If imgNodes Is Nothing OrElse imgNodes.Count = 0 Then
                imgNodes = doc.DocumentNode.SelectNodes(
                    "//img[contains(@src,'content.abf.store')]")
            End If
            Dim imgUrls As New List(Of String)
            If imgNodes IsNot Nothing Then
                Dim seen2 As New HashSet(Of String)
                For Each imgNode In imgNodes
                    Dim src = imgNode.GetAttributeValue("src", "")
                    If Not String.IsNullOrEmpty(src) Then
                        ' 转成绝对 URL
                        If src.StartsWith("//") Then src = "https:" & src
                        If src.StartsWith("/") Then src = BaseUrl & src
                        ' 去掉文件名前缀 thumb_ / small_，下载原图
                        src = Regex.Replace(src, "/(thumb_|small_)([^/]+)$", "/$2")
                        If seen2.Add(src) Then imgUrls.Add(src)
                    End If
                Next
            End If

            ' 搜索词中的字母数字部分，用于命名
            Dim searchSlug = Regex.Replace(
                result.ProductName, "[^A-Za-z0-9]", "").ToUpper()
            If String.IsNullOrEmpty(searchSlug) Then searchSlug = "IMG"

            Dim nameList As New List(Of String)
            For idx = 0 To imgUrls.Count - 1
                Dim ext = Path.GetExtension(
                    New Uri(imgUrls(idx)).AbsolutePath)
                If String.IsNullOrEmpty(ext) Then ext = ".jpg"

                ' 命名规则：型号-结果序号-图片序号（示例：NU324ETVP2FAG-1-1.png）
                Dim baseName = $"{searchSlug}-{resultIndex}-{idx + 1}{ext}"
                nameList.Add(baseName)

                ' 如果传入了存储路径则下载
                If Not String.IsNullOrWhiteSpace(imageSavePath) Then
                    Try
                        Directory.CreateDirectory(imageSavePath)
                        Dim saveTo = Path.Combine(imageSavePath, baseName)
                        If Not File.Exists(saveTo) Then
                            Dim bytes = FetchBytes(imgUrls(idx), ct)
                            File.WriteAllBytes(saveTo, bytes)
                        End If
                    Catch ex As Exception
                        ' 单张下载失败不阻断整体
                    End Try
                End If
            Next
            result.ImageNames = If(nameList.Count > 0, String.Join(",", nameList), "|")

            ' ── 翻译：将所有 "英文|" 字段填上中文 ──────────────────
            ' 辅助函数：取 | 前的英文部分
            Dim trans = Function(s As String) As String
                            Dim en = s.Split("|"c)(0)
                            If String.IsNullOrWhiteSpace(en) Then Return s
                            Dim zh = Translate(en)
                            Return en & "|" & zh
                        End Function

            result.ProductDescription  = trans(result.ProductDescription)
            result.Category            = trans(result.Category)
            result.Bore                = trans(result.Bore)
            result.Seal                = trans(result.Seal)
            result.CageType            = trans(result.CageType)
            result.ExternalModification = trans(result.ExternalModification)
            result.RadialInternalPlay  = trans(result.RadialInternalPlay)
            result.Precision           = trans(result.Precision)
            result.HeatStabilization   = trans(result.HeatStabilization)

            Return result
        End Function

        ''' <summary>
        ''' 解析详情页 Equivalents 区块，返回等效型号字符串。
        ''' Equivalents 区块由浏览器通过 data-renderurl AJAX 加载，需单独请求。
        ''' 格式：型号1|库存1|原价1|折扣价1|型号2|库存2|原价2|折扣价2|…
        ''' </summary>
        Private Function ParseEquivalents(doc As HtmlDocument, Optional ct As System.Threading.CancellationToken = Nothing) As String
            ' 找到带有 data-renderurl 的 AJAX 占位节点（含 "equivalents" 路径）
            Dim ajaxNode = doc.DocumentNode.SelectSingleNode(
                "//*[@data-renderurl and contains(@data-renderurl,'equivalents')]")
            If ajaxNode Is Nothing Then Return "|"

            Dim renderUrl = ajaxNode.GetAttributeValue("data-renderurl", "")
            If String.IsNullOrEmpty(renderUrl) Then Return "|"
            If renderUrl.StartsWith("/") Then renderUrl = BaseUrl & renderUrl

            ' 请求 AJAX 接口获取渲染后的 HTML 片段
            Dim equivHtml As String
            Try
                equivHtml = FetchHtml(renderUrl, ct)
            Catch
                Return "|"
            End Try

            Dim equivDoc As New HtmlDocument()
            equivDoc.LoadHtml(equivHtml)

            ' AJAX 页面中等效型号行的 class 为 "equivalent"（非 result-row）
            Dim equivRows = equivDoc.DocumentNode.SelectNodes(
                "//li[contains(@class,'equivalent')]")
            If equivRows Is Nothing Then Return "|"

            Dim list As New List(Of String)
            For Each row In equivRows
                ' 型号：行内第一个 <a> 链接文字
                Dim nameNode = row.SelectSingleNode(".//a[@href]")
                Dim modelName = ""
                If nameNode IsNot Nothing Then
                    modelName = HtmlEntity.DeEntitize(nameNode.InnerText.Trim())
                End If
                If String.IsNullOrWhiteSpace(modelName) Then Continue For

                ' 直接提取划线原价（span.strikethrough，接受任意货币）
                Dim strikeSpan = row.SelectSingleNode(".//span[contains(@class,'strikethrough')]")
                Dim formerDisplayText = ""
                If strikeSpan IsNot Nothing Then
                    Dim ft = NormalizeEuro(HtmlEntity.DeEntitize(strikeSpan.InnerText.Trim()))
                    If ft.Length > 0 AndAlso
                       Char.GetUnicodeCategory(ft(0)) = Globalization.UnicodeCategory.CurrencySymbol Then
                        formerDisplayText = ft
                    End If
                End If

                ' 提取当前显示价（价格区 div 内与 strikethrough 相邻的纯文本 span）
                Dim currentDisplayText = ""
                Dim priceDivNode = row.SelectSingleNode(
                    ".//div[contains(@style,'text-align')]")
                If priceDivNode IsNot Nothing Then
                    Dim plainSpan = priceDivNode.SelectSingleNode(
                        ".//span[not(.//span)][not(contains(@class,'strikethrough'))]")
                    If plainSpan IsNot Nothing Then
                        Dim ctText = NormalizeEuro(HtmlEntity.DeEntitize(plainSpan.InnerText.Trim()))
                        If ctText.Length > 0 AndAlso
                           Char.GetUnicodeCategory(ctText(0)) = Globalization.UnicodeCategory.CurrencySymbol Then
                            currentDisplayText = ctText
                        End If
                    End If
                End If

                ' 库存 / 价格：扫描行内文本节点（价格从 JSON 回退，始终 EUR）
                Dim stockText = ""
                Dim priceText = ""
                Dim discountText = ""
                ScanRowTexts(row, stockText, priceText, discountText)

                ' 计算 EUR 原价（htmlFormerPrice）
                Dim htmlFormerPrice = ""
                If Not String.IsNullOrEmpty(formerDisplayText) Then
                    If formerDisplayText(0) = ChrW(8364) Then
                        ' 显示价已是 EUR，直接使用
                        htmlFormerPrice = formerDisplayText
                    ElseIf Not String.IsNullOrEmpty(currentDisplayText) AndAlso
                            Not String.IsNullOrEmpty(priceText) Then
                        ' 非 EUR 显示价（如 USD）→ 按比例换算 EUR 原价
                        ' EUR原价 = EUR当前价(JSON) × (USD原价 / USD当前价)
                        Dim formerNum, currentNum, eurCurrentNum As Double
                        If ExtractNumericPrice(formerDisplayText, formerNum) AndAlso
                           ExtractNumericPrice(currentDisplayText, currentNum) AndAlso
                           ExtractNumericPrice(priceText, eurCurrentNum) AndAlso
                           currentNum > 0 Then
                            Dim eurFormerNum = eurCurrentNum * (formerNum / currentNum)
                            htmlFormerPrice = ChrW(8364) & " " &
                                eurFormerNum.ToString("F2",
                                    Globalization.CultureInfo.InvariantCulture).Replace(".", ",")
                        End If
                    End If
                End If

                ' 若 ScanRowTexts 只取到当前价（JSON 回退），但有计算出的 EUR 原价
                ' → 原价=htmlFormerPrice，折扣价=priceText（当前价）
                If discountText = "" AndAlso Not String.IsNullOrEmpty(htmlFormerPrice) AndAlso
                   Not String.IsNullOrEmpty(priceText) Then
                    discountText = priceText
                    priceText = htmlFormerPrice
                End If

                ' 格式：4字段平铺，最后 String.Join("|") 拼合
                list.Add(modelName)
                list.Add(stockText)
                list.Add(priceText)
                list.Add(discountText)
            Next

            Return If(list.Count > 0, String.Join("|", list), "|")
        End Function

        ''' <summary>
        ''' 扫描一个 li/row 节点内所有末端文本，提取库存（含 pc/pcs）和价格（含 €）。
        ''' </summary>
        Private Shared Sub ScanRowTexts(row As HtmlNode,
                                         ByRef stockOut As String,
                                         ByRef priceOut As String,
                                         Optional ByRef discountPriceOut As String = "")
            ' 只取没有子元素的叶节点，避免重复累加父节点文本
            Dim leaves = row.SelectNodes(
                ".//span[not(.//span)] | .//div[not(.//div) and not(.//span)]" &
                " | .//del[not(.//del)] | .//s[not(.//s)]")
            If leaves Is Nothing Then Return

            For Each leaf In leaves
                Dim t = NormalizeEuro(HtmlEntity.DeEntitize(leaf.InnerText.Trim()))
                If t = "" Then Continue For

                If stockOut = "" AndAlso
                   (t.EndsWith(" pc") OrElse t.EndsWith(" pcs")) Then
                    stockOut = t
                End If
                If t.Length > 0 AndAlso t(0) = ChrW(8364) Then
                    Dim cls = leaf.GetAttributeValue("class", "")
                    ' former-price / strikethrough / del / s 元素 → 划线原价
                    Dim tagName = leaf.Name.ToLowerInvariant()
                    If cls.Contains("former-price") OrElse cls.Contains("strikethrough") OrElse
                       tagName = "del" OrElse tagName = "s" Then
                        priceOut = t
                    ElseIf cls.Contains("price") Then
                        If priceOut = "" Then
                            priceOut = t              ' 无折扣时直接作为原价
                        Else
                            discountPriceOut = t      ' 有折扣时作为折扣价
                        End If
                    ElseIf priceOut = "" Then
                        priceOut = t                  ' 通用回退：第一个 € span
                    ElseIf discountPriceOut = "" Then
                        discountPriceOut = t          ' 通用回退：第二个 € span
                    End If
                End If
            Next

            ' 若未找到 € 价格，从行内 script 的 AddToBasketModel JSON 提取 ItemPriceInEuros
            ' 仅在 BasketAddable:true 时使用，询价产品（Price on request）不采集价格
            If priceOut = "" Then
                Dim scriptNodes = row.SelectNodes(".//script")
                If scriptNodes IsNot Nothing Then
                    For Each s In scriptNodes
                        Dim bm = Regex.Match(s.InnerText,
                            """BasketAddable""\s*:\s*(true|false)",
                            RegexOptions.IgnoreCase)
                        Dim addable = Not bm.Success OrElse
                                      bm.Groups(1).Value.Equals("true",
                                          StringComparison.OrdinalIgnoreCase)
                        If Not addable Then Continue For
                        Dim m = Regex.Match(s.InnerText,
                            """ItemPriceInEuros""\s*:\s*([\d.]+)")
                        If m.Success Then
                            Dim priceVal As Double
                            If Double.TryParse(m.Groups(1).Value,
                                               Globalization.NumberStyles.Float,
                                               Globalization.CultureInfo.InvariantCulture,
                                               priceVal) AndAlso priceVal > 0 Then
                                priceOut = "€ " & priceVal.ToString("F2",
                                    Globalization.CultureInfo.InvariantCulture).Replace(".", ",")
                                Exit For
                            End If
                        End If
                    Next
                End If
            End If
        End Sub

        ''' <summary>从价格文本（任意货币）提取数值，支持逗号/句点小数分隔符。</summary>
        Private Shared Function ExtractNumericPrice(priceText As String, ByRef value As Double) As Boolean
            Dim s = Regex.Replace(priceText, "[^\d.,]", "")
            If s = "" Then Return False
            If s.Contains(",") AndAlso s.Contains(".") Then
                s = s.Replace(".", "").Replace(",", ".")
            ElseIf s.Contains(",") Then
                s = s.Replace(",", ".")
            End If
            Return Double.TryParse(s, Globalization.NumberStyles.Any,
                                   Globalization.CultureInfo.InvariantCulture, value)
        End Function

        ''' <summary>
        ''' HtmlEntity.DeEntitize 对 &amp;euro; 不解码，手动处理所有 Euro 实体形式。
        ''' </summary>
        Private Shared Function NormalizeEuro(s As String) As String
            Return s.Replace("&euro;", ChrW(8364)) _
                    .Replace("&#8364;", ChrW(8364)) _
                    .Replace("&#x20AC;", ChrW(8364)) _
                    .Replace("&#X20AC;", ChrW(8364))
        End Function

        ''' <summary>带超时的 HTTP GET 请求，返回响应文本。</summary>
        Private Function FetchHtml(url As String, Optional ct As System.Threading.CancellationToken = Nothing) As String
            Dim resp = _http.GetAsync(url, ct).GetAwaiter().GetResult()
            resp.EnsureSuccessStatusCode()
            Dim bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            Return System.Text.Encoding.UTF8.GetString(bytes)
        End Function

        ''' <summary>带超时的 HTTP GET 请求，返回响应字节数组。</summary>
        Private Function FetchBytes(url As String, Optional ct As System.Threading.CancellationToken = Nothing) As Byte()
            Dim resp = _http.GetAsync(url, ct).GetAwaiter().GetResult()
            resp.EnsureSuccessStatusCode()
            Return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        End Function

        ''' <summary>将英文翻译为中文。enableTranslation=False 时直接返回空字符串（回退英文由调用方处理）。</summary>
        Private Function Translate(text As String) As String
            If Not _enableTranslation Then Return ""
            Return _translator.Translate(text)
        End Function


        ''' <summary>将整数匹配模式映射为 URL 查询参数值。
        ''' 0 = 包含(contains)；1 = 开头(starts_with，默认)；2 = 完全匹配(exact_match)
        ''' </summary>
        Private Shared Function MapMatchMode(matchMode As Integer) As String
            Select Case matchMode
                Case 0 : Return "contains"
                Case 1 : Return "starts_with"
                Case 2 : Return "exact_match"
                Case Else : Return "starts_with" ' 超出范围时回退默认值
            End Select
        End Function

        ' ────────────────────────────────────────────────────
        '  资源释放
        ' ────────────────────────────────────────────────────

        Public Sub Dispose() Implements IDisposable.Dispose
            _http.Dispose()
            _handler.Dispose()
            _translator.Dispose()
        End Sub

        ''' <summary>获取当前使用的翻译器实例，供调用方独立使用翻译功能。</summary>
        Public ReadOnly Property Translator As AbfTranslator
            Get
                Return _translator
            End Get
        End Property

End Class
