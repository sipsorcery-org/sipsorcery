<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="customerconfirm.aspx.cs" Inherits="SIPSorcery.CRM.Web.CustomerEmailConfirmation" %>

<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">

<html xmlns="http://www.w3.org/1999/xhtml" >
<head runat="server">
    <title></title>
</head>
<body style="background-color: Black; color: #A0F927">

    <asp:Label id="m_confirmMessage" runat="server" /><br /><br />

    <% if (m_confirmed) { %>
       Please login to your account <a href="https://www.sipsorcery.com/sipsorcery.html">here</a>
    <% } %>
</body>
</html>
