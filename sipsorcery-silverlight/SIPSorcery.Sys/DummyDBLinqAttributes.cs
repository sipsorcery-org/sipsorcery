using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SIPSorcery.Sys {

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class Table : System.Attribute{
        public string Name;
    }

    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class Column : System.Attribute {
        public string Storage;
        public string Name;
        public string DbType;
        public bool IsPrimaryKey;
        public bool CanBeNull;
    }
}
