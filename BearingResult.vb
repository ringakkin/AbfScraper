Imports System.Collections.Generic

''' <summary>
''' 单条轴承采集结果。
''' 字段顺序与客户需求一致；含 | 的字段均为客户指定的内部拼接格式。
''' </summary>
Public Class BearingResult

    ' ── 以下三项为程序集内部字段，外部调用方不可见 ──
    Friend Property RawData As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Friend Property DetailUrl As String = ""
    Friend Property ProductName As String = ""

    ' ═══════════════════════════════════════════════════════
    '  以下 21 个属性与客户需求字段一一对应（分号分隔顺序）
    ' ═══════════════════════════════════════════════════════

    ''' <summary>1. 结果（搜索列表中显示的产品型号全称）</summary>
    Public Property ResultName As String = "|"

    ''' <summary>2. 简述</summary>
    Public Property ShortDescription As String = "|"

    ''' <summary>3. 原价|折扣价（即使为空也保留 | ，格式固定为两个字段）</summary>
    Public Property Price As String = "|"

    ''' <summary>4. 库存</summary>
    Public Property Stock As String = "|"

    ''' <summary>
    ''' 5. 等效型号
    ''' 格式：型号1|库存1|原价1|折扣价1|型号2|库存2|原价2|折扣价2|…（4字段循环，全空则 |||）
    ''' </summary>
    Public Property EquivalentModels As String = "|"

    ''' <summary>6. 产品描述英文|中文（即使为空也保留 | ，格式固定为两个字段）</summary>
    Public Property ProductDescription As String = "|"

    ''' <summary>7. 也被称为（Also known as）</summary>
    Public Property AlsoKnownAs As String = "|"

    ''' <summary>8. 英文类别|中文（即使为空也保留 | ，格式固定为两个字段）</summary>
    Public Property Category As String = "|"

    ''' <summary>9. 内层（Inner diameter）</summary>
    Public Property InnerDiameter As String = "|"

    ''' <summary>10. 外层（Outer diameter）</summary>
    Public Property OuterDiameter As String = "|"

    ''' <summary>11. 宽度（Width (B)）</summary>
    Public Property Width As String = "|"

    ''' <summary>12. 重量（Weight (kg)）</summary>
    Public Property Weight As String = "|"

    ''' <summary>13. 钻孔（Bore）原文|中文</summary>
    Public Property Bore As String = "|"

    ''' <summary>14. 印章（Seal）原文|中文</summary>
    Public Property Seal As String = "|"

    ''' <summary>15. 笼型（Cage Type）原文|中文</summary>
    Public Property CageType As String = "|"

    ''' <summary>16. 外部改装（External Modification）原文|中文</summary>
    Public Property ExternalModification As String = "|"

    ''' <summary>17. 径向内部游隙（Radial Internal Play）原文|中文</summary>
    Public Property RadialInternalPlay As String = "|"

    ''' <summary>18. 精度（Precision）原文|中文</summary>
    Public Property Precision As String = "|"

    ''' <summary>19. 热稳定（Heat Stabilization）原文|中文</summary>
    Public Property HeatStabilization As String = "|"

    ''' <summary>
    ''' 20. 图片名（搜索内容的字母和数字）
    ''' 多个文件名：之1,之2,之3…
    ''' </summary>
    Public Property ImageNames As String = "|"

    ''' <summary>
    ''' 21. 后缀描述1
    ''' 格式：KEY|英文说明|中文|KEY|英文说明|中文…
    ''' </summary>
    Public Property SuffixDescription As String = "|"

    ' ═══════════════════════════════════════
    '  内部辅助
    ' ═══════════════════════════════════════

    Friend Function GetRaw(key As String) As String
        Dim value As String = ""
        If RawData.TryGetValue(key, value) Then Return value
        Return ""
    End Function

    ''' <summary>
    ''' 将 21 个字段按客户需求顺序输出为一维字符串数组（下标 0~20）。
    ''' 供 Search() 组装二维数组时使用。
    ''' </summary>
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
            SuffixDescription
        }
    End Function

End Class
