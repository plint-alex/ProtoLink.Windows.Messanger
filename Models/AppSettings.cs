using System;
using Newtonsoft.Json;
using System.IO;

namespace ProtoLink.Windows.Messanger.Models
{
    public class AppSettings
    {
        public string ApiBaseAddress { get; set; } = "http://localhost:5000/";
    }
}

