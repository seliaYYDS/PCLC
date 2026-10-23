Imports System.Text

''' <summary>记录本次会话中的改动，用于结束时生成变更摘要。</summary>
Public Module ModAgentChanges

    Private ReadOnly Counts As New List(Of KeyValuePair(Of String, Integer))()
    Private ReadOnly Details As New Dictionary(Of String, List(Of String))

    Public Sub Reset()
        Counts.Clear()
        Details.Clear()
    End Sub

    Private Sub Bump(Kind As String, Delta As Integer, Optional Detail As String = Nothing)
        SyncLock Counts
            Dim Index As Integer = -1
            For i As Integer = 0 To Counts.Count - 1
                If Counts(i).Key = Kind Then Index = i
            Next
            If Index >= 0 Then
                Counts(Index) = New KeyValuePair(Of String, Integer)(Kind, Counts(Index).Value + Delta)
            Else
                Counts.Add(New KeyValuePair(Of String, Integer)(Kind, Delta))
            End If
            If Not String.IsNullOrWhiteSpace(Detail) Then
                If Not Details.ContainsKey(Kind) Then Details(Kind) = New List(Of String)
                If Delta > 0 Then
                    Details(Kind).Add(Detail)
                Else
                    Details(Kind).Remove(Detail)
                End If
            End If
        End SyncLock
    End Sub

    Public Sub Added(Kind As String, Optional Detail As String = Nothing)
        Bump(Kind, 1, Detail)
    End Sub

    Public Sub Removed(Kind As String, Optional Detail As String = Nothing)
        Bump(Kind, -1, Detail)
    End Sub

    Public Function Summary() As String
        SyncLock Counts
            Dim Parts As New List(Of String)
            For Each Pair In Counts
                If Pair.Value = 0 Then Continue For
                Dim Text As String = If(Pair.Value > 0, "+", "") & Pair.Value & " " & Pair.Key
                Dim Keys As List(Of String) = Nothing
                If Details.TryGetValue(Pair.Key, Keys) AndAlso Keys IsNot Nothing AndAlso Keys.Count = 1 Then
                    Text &= "（" & Keys(0) & "）"
                End If
                Parts.Add(Text)
            Next
            If Parts.Count = 0 Then Return ""
            Return "本次修改：" & String.Join("，", Parts)
        End SyncLock
    End Function

End Module
