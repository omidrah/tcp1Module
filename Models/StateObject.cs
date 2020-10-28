using System;
using System.Net.Sockets;

namespace TCPServer.Models
{
    // State object for reading client data asynchronously  
    public class StateObject
    {
        // Client  socket.  
        public Socket workSocket = null;
        // Size of receive buffer.  
        public const int BufferSize = 100000;
        // Receive buffer.  
        public byte[] buffer = new byte[BufferSize];
        // Received data string.  
        public string value;
        // Temp Data String;
        public string tmpValue;
        public string IMEI1 { get; set; }
        public string IMEI2 { get; set; }
        public string IP { get; set; }
        public System.Timers.Timer Timer { get; set; }
        public bool IsConnected { get; set; }
        public int counter { get; set; } // if counter > 0 mean ,, each device disconnect
        public DateTime lastDateTimeConnected { get; set; }
    }
}
