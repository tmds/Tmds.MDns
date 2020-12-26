Option Strict On
Option Infer On
Imports System.Linq
Imports Tmds.MDns
Imports NUnit.Framework

<TestFixture, Explicit>
Public Class Test
    Private _mdnsBrowser As Tmds.MDns.ServiceBrowser

    <Test, Explicit>
    Public Sub SendMDNSDiscover()
        Try
            Const serviceType = "_googlecast._tcp"
            If _mdnsBrowser IsNot Nothing Then
                RemoveHandler _mdnsBrowser.ServiceAdded, AddressOf ServiceAdded
                _mdnsBrowser.StopBrowse()
            End If
            _mdnsBrowser = New Tmds.MDns.ServiceBrowser
            AddHandler _mdnsBrowser.ServiceAdded, AddressOf ServiceAdded
            _mdnsBrowser.StartBrowse(serviceType)

            Dim exittime = Now.AddSeconds(3)
            Do While exittime > Now

            Loop


        Catch ex As Exception
            MsgBox(ex.ToString)
        End Try

    End Sub

    Private Sub ServiceAdded(sender As Object, e As ServiceAnnouncementEventArgs)
        Stop
        Throw New NotImplementedException()
    End Sub

    Private Sub ServiceAdded2(sender As Object, e As ServiceAnnouncementEventArgs)

        Stop

        'Dim info = New Utilities.MdnsDiscoverInfo(e.Announcement.Addresses.ToString, e.Announcement.NetworkInterface, e.Announcement.LocalEndpoint)
        'RaiseEvent DataReceived(info)

        'asdf
        'Dim addressStringList As List(Of String) = New List(Of String)
        'For Each IP In e.Announcement.Addresses
        '    addressStringList.Add(Net.IPAddress.Parse(IP.Address.ToString()).ToString())
        'Next
        ''e.Announcement.Hostname
        ''e.Announcement.Type
        ''e.Announcement.Port
        'Dim display = $"add={Now()}{vbCrLf}{String.Join(",", addressStringList)}{vbCrLf}host={e.Announcement.Hostname}{vbCrLf}type={e.Announcement.Type}{vbCrLf}port={e.Announcement.Port}{vbCrLf}{vbCrLf}"

        'addToList(display)
    End Sub

End Class
