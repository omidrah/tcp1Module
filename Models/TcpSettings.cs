using System.Net.Http;

namespace TCPServer.Models
{
    public static class TcpSettings
    {
        public static string ip { get; set; } = "127.0.0.1";
        public static string port { get; set; } = "6070";
        public static string ConnectionString { get; set; }
        public static string VIKey { set; get; } = "BgrUEy5IbpJSnhmqI2IhKw==";
        public static double ctSecond { get; set; } = 10; //default 10
        public static double TGenral { get; set; }
        public static string Imei1Log { get; set; }
        public static string l3Host { get; set; }
        public static string Rest { get; set; }
        public static string serverPath { get; set; }
        public static int ConsoleTableWidth { get; set; }
        public static readonly HttpClientHandler _handler = new HttpClientHandler
        {
            MaxConnectionsPerServer = int.MaxValue, // default for .NET Core
            UseDefaultCredentials = true,
            UseProxy = false
        };
    }
}
