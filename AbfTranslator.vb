Imports System
Imports System.IO
Imports System.Net.Http
Imports System.Collections.Generic
Imports System.Text.RegularExpressions
Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' 支持多后端的英译中翻译器，自动维护本地词典缓存（命中缓存不发网络请求）。
'''
''' 用法示例：
'''   ' MyMemory（默认，无需 Key）
'''   Using t As New AbfTranslator()
'''       Dim zh = t.Translate("Deep groove ball bearing")
'''   End Using
'''
'''   ' 百度翻译（需在 https://fanyi-api.baidu.com/ 注册获取 AppId/SecretKey）
'''   Using t As New AbfTranslator(TranslationProvider.Baidu, "AppId", "SecretKey")
'''       Dim zh = t.Translate("Deep groove ball bearing")
'''   End Using
''' </summary>
Public Enum TranslationProvider
    ''' <summary>MyMemory 免费接口，无需 Key，每 IP 每日约 5000 字。国内可访问。</summary>
    MyMemory = 0
    ''' <summary>百度翻译开放平台，需 AppId + SecretKey，免费额度 500 万字/月。国内推荐。</summary>
    Baidu = 1
End Enum

Public Class AbfTranslator
    Implements IDisposable

    Private ReadOnly _http As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(30)}
    Private ReadOnly _cache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _dictPath As String
    Private ReadOnly _provider As TranslationProvider
    Private ReadOnly _appId As String
    Private ReadOnly _secretKey As String
    Private Shared ReadOnly _rng As New Random()

    ''' <param name="provider">翻译后端，默认 MyMemory（无需 Key）。</param>
    ''' <param name="appId">百度 AppId（仅 Baidu 模式需要）。</param>
    ''' <param name="secretKey">百度 SecretKey（仅 Baidu 模式需要）。</param>
    ''' <param name="dictPath">持久化词典文件路径，留空则自动使用程序目录下的 trans_dict.txt。</param>
    Public Sub New(Optional provider As TranslationProvider = TranslationProvider.MyMemory,
                   Optional appId As String = "",
                   Optional secretKey As String = "",
                   Optional dictPath As String = "")
        _provider  = provider
        _appId     = appId
        _secretKey = secretKey
        _dictPath  = If(String.IsNullOrWhiteSpace(dictPath),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trans_dict.txt"),
                        dictPath)
        LoadDictionary()
    End Sub

    ''' <summary>
    ''' 将英文翻译为中文。优先查本地词典；未命中时调接口并写回词典。
    ''' 失败时返回空字符串（调用方可回退原文）。
    ''' </summary>
    Public Function Translate(text As String) As String
        If String.IsNullOrWhiteSpace(text) Then Return ""
        Dim cached As String = Nothing
        If _cache.TryGetValue(text, cached) Then Return cached
        Dim zh = ""
        Select Case _provider
            Case TranslationProvider.Baidu
                zh = TranslateBaidu(text)
            Case Else
                zh = TranslateMyMemory(text)
        End Select
        If Not String.IsNullOrEmpty(zh) AndAlso ContainsChinese(zh) Then
            _cache(text) = zh
            AppendDictEntry(text, zh)
            Return zh
        End If
        Return ""
    End Function

    ' ── MyMemory 后端 ────────────────────────────────────────
    Private Function TranslateMyMemory(text As String) As String
        Try
            Dim url = "https://api.mymemory.translated.net/get?q=" &
                      Uri.EscapeDataString(text) & "&langpair=en|zh"
            Dim json = _http.GetStringAsync(url).GetAwaiter().GetResult()
            Dim m = Regex.Match(json, """translatedText""\s*:\s*""([^""]+)""")
            If m.Success Then
                Dim zh = Net.WebUtility.HtmlDecode(m.Groups(1).Value)
                If zh.Contains("QUERY LENGTH LIMIT") OrElse
                   zh.Contains("YOU USED ALL AVAILABLE FREE TRANSLATIONS") OrElse
                   zh.Contains("MYMEMORY WARNING") Then Return ""
                Return zh
            End If
        Catch
        End Try
        Return ""
    End Function

    ' ── 百度翻译后端 ─────────────────────────────────────────
    ' 文档：https://fanyi-api.baidu.com/doc/21
    Private Function TranslateBaidu(text As String) As String
        If String.IsNullOrWhiteSpace(_appId) OrElse
           String.IsNullOrWhiteSpace(_secretKey) Then Return ""
        Try
            Dim salt = _rng.Next(10000, 99999).ToString()
            Dim sign = Md5Hex(_appId & text & salt & _secretKey)
            Dim url = "https://fanyi-api.baidu.com/api/trans/vip/translate" &
                      "?q="     & Uri.EscapeDataString(text) &
                      "&from=en&to=zh" &
                      "&appid=" & Uri.EscapeDataString(_appId) &
                      "&salt="  & salt &
                      "&sign="  & sign
            Dim json = _http.GetStringAsync(url).GetAwaiter().GetResult()
            Dim matches = Regex.Matches(json, """dst""\s*:\s*""([^""]+)""")
            If matches.Count > 0 Then
                Dim parts As New List(Of String)()
                For Each mc As Match In matches
                    parts.Add(DecodeJsonUnicode(Net.WebUtility.HtmlDecode(mc.Groups(1).Value)))
                Next
                Return String.Join("", parts)
            End If
        Catch
        End Try
        Return ""
    End Function

    ' ── 工具 ────────────────────────────────────────────────
    ''' <summary>将 JSON \uXXXX 转义序列解码为实际 Unicode 字符。</summary>
    Private Shared Function DecodeJsonUnicode(s As String) As String
        Dim sb As New StringBuilder(s.Length)
        Dim i As Integer = 0
        While i < s.Length
            If i <= s.Length - 6 AndAlso
               s(i) = "\"c AndAlso s(i + 1) = "u"c AndAlso
               IsHexChar(s(i + 2)) AndAlso IsHexChar(s(i + 3)) AndAlso
               IsHexChar(s(i + 4)) AndAlso IsHexChar(s(i + 5)) Then
                sb.Append(ChrW(Convert.ToInt32(s.Substring(i + 2, 4), 16)))
                i += 6
            Else
                sb.Append(s(i))
                i += 1
            End If
        End While
        Return sb.ToString()
    End Function

    Private Shared Function IsHexChar(c As Char) As Boolean
        Return (c >= "0"c AndAlso c <= "9"c) OrElse
               (c >= "a"c AndAlso c <= "f"c) OrElse
               (c >= "A"c AndAlso c <= "F"c)
    End Function

    Private Shared Function ContainsChinese(s As String) As Boolean
        For Each c As Char In s
            Dim code = AscW(c)
            If code >= &H4E00 AndAlso code <= &H9FFF Then Return True
        Next
        Return False
    End Function

    Private Shared Function Md5Hex(s As String) As String
        Using md5 As MD5 = MD5.Create()
            Dim bytes As Byte() = md5.ComputeHash(Encoding.UTF8.GetBytes(s))
            Dim sb As New StringBuilder()
            For Each b As Byte In bytes
                sb.Append(b.ToString("x2"))
            Next
            Return sb.ToString()
        End Using
    End Function

    Private Sub LoadDictionary()
        If Not File.Exists(_dictPath) Then Exit Sub
        For Each line In File.ReadAllLines(_dictPath, Encoding.UTF8)
            Dim parts = line.Split(vbTab)
            If parts.Length >= 2 AndAlso ContainsChinese(parts(1)) Then
                _cache(parts(0)) = parts(1)
            End If
        Next
    End Sub

    Private Const MaxCacheKeyLength As Integer = 200

    Private Sub AppendDictEntry(en As String, zh As String)
        If en.Length > MaxCacheKeyLength Then Return
        If Not ContainsChinese(zh) Then Return
        Try
            File.AppendAllText(_dictPath, en & vbTab & zh & vbNewLine, Encoding.UTF8)
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _http.Dispose()
    End Sub

End Class
