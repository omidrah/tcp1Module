using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TCPServer.Models
{
    public class Server
    {
        public  Socket _listener;
        public string IpServer { get; set; }
        public int PortServer { get; set; }
        // <summary>
        // Establish the local endpoint for the socket.  
        // The DNS name of the computer  
        // running the listener is "185.192.112.74".  
        /// </summary>
        /// <param name="ip">Server Ip</param>
        /// <param name="port">Port for Socket</param>
        /// <returns></returns>
        public Server(string Ip, int port)
        {
            IpServer = Ip;
            PortServer = port;
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            //IPAddress ipAddress = System.Net.IPAddress.Parse(IpServer); 
            IPAddress ipAddress = System.Net.IPAddress.Any;//for err : The Address Request invaid
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, PortServer);
            _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                _listener.Bind(localEndPoint);
                _listener.Listen(100);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
        public void Dispose()
        {
            if (_listener != null)
            {
                _listener.Shutdown(SocketShutdown.Both);
                _listener.Close();
            }
        }

      
    }
}
