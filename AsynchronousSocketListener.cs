using System.Collections.Generic;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using TCPServer.Models;
using System.Collections.Immutable;
using System.Threading.Tasks;
namespace TCPServer
{
    public static class AsynchronousSocketListener
    {
        // Thread signal.  
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        public static object LockDev = new object();   
        public static List<StateObject> DeviceList;
        public static List<string> SendedTest = new List<string>();        
        public static Socket listener;
        public static void StartListening(string ip, int port)
        {
            ConsolePrint.PrintLine('*');            
            Console.WriteLine(ConsolePrint.AlignCentre($" Server by Ip {ip} on Port {port} ready for Listen"));
            ConsolePrint.PrintLine('*');
            Console.WriteLine(ConsolePrint.AlignCentre($" Server Started @ {DateTime.Now}"));
            ConsolePrint.PrintLine('*');
            Console.WriteLine(ConsolePrint.AlignCentre("Press CTRL+C For ShutDown Server"));
            ConsolePrint.PrintLine('*');
            DeviceList = new List<StateObject>();
            listener = new Server(ip, port)._listener;            
            var genTimer = new System.Timers.Timer(TcpSettings.TGenral); //after 1 minute
            genTimer.Elapsed += GenTimer_Elapsed;
            genTimer.Start();            
            while (true)
            {               
                // Set the event to nonsignaled state.  
                allDone.Reset();
                // Start an asynchronous socket to listen for connections.   
                listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
                // Wait until a connection is made before continuing.  
                allDone.WaitOne();
			}                 
        }
        private static void GenTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var a = (System.Timers.Timer)sender;
            a.Stop();
            if (DeviceList.Count > 0 )
            {    
                Console.ForegroundColor = ConsoleColor.White;
                ConsolePrint.PrintLine('*');
                Console.WriteLine(ConsolePrint.AlignCentre($"Device cnt={DeviceList.Count} @ {DateTime.Now.ToString("yyyy/M/d HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture)}"));
                ConsolePrint.PrintLine('*');
                Console.ForegroundColor = ConsoleColor.Green;               
            }
            a.Start();
        }
  
        /// <summary>
        /// socket --shutdown and close
        /// device --remove from collection
        /// show info on screen
        /// </summary>
        /// <param name="item"></param>
        public static void clientDis(StateObject item)
        {
            try
            {
                var CurItem =  DeviceList.Find(x => x.IMEI1 == item.IMEI1);
                if (CurItem!=null)
                {   
                    if (item.workSocket != null)
                    {
                        item.workSocket.Shutdown(SocketShutdown.Both);
                        //item.workSocket.Close();                        
                    }
                    Util.UpdateMachineState(item.IMEI1, item.IMEI2, false).ConfigureAwait(false);
                    lock (LockDev)
                    {
                        DeviceList.Remove(CurItem);
                    }
                    Util.ShowMessage($"Device by IMEI1={item.IMEI1}/IP={item.IP} Disconnected", ConsoleColor.Red, ConsoleColor.Green, item.IMEI1);
                }
            }
            catch (Exception ex)
            {
                 Console.WriteLine("Socket ShutDown has error" + ex.Message);
                 Util.LogErrorAsync(ex, item.workSocket.ToString(), item.IMEI1).ConfigureAwait(false);
            }
        }
       public static async void AcceptCallback(IAsyncResult ar)
       {
            try
            {
                // Signal the main thread to continue.  
                allDone.Set();
                // Get the socket that handles the client request.  
                if (ar.IsCompleted)
                {      
                    Socket client = ((Socket)ar.AsyncState).EndAccept(ar);
                    // Create the state object.                      
                    StateObject state = new StateObject{
                        workSocket = client,
                        Timer = new System.Timers.Timer(TcpSettings.ctSecond),
                        IsConnected = true,
                        counter = 0,
                        value = string.Empty,
                        tmpValue = string.Empty
                    };
                    try
                    {
                        state.Timer.Elapsed += (sender,ElapsedEventArgs) => clientTimerElapsed(state);                        
                        state.IP = IPAddress.Parse(((IPEndPoint)client.RemoteEndPoint).Address.ToString()).ToString();                        
                        //Console.WriteLine($"\n Accept Socket Ip = {state.IP} @ {DateTime.Now.ToString("yyyy/M/d HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)} \n");
                        if (SocketConnected(client, 0))
                        {
                            client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                                                 new AsyncCallback(BeginReceiveCallback), state);
                        }
                    }
                    catch (Exception e)
                    {
                         _=Util.LogErrorAsync(e, "from AcceptCallback has Excetion ", state.IP ).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                  _=Util.LogErrorAsync(e, "84 -- Method -- AcceptCallback").ConfigureAwait(false);
            }
       }

        private static void clientTimerElapsed(StateObject state)
        {
            var curClient = DeviceList.Find(x => x.IMEI1 == state.IMEI1);
            if (curClient != null)
            {
                //if client disconnected                
                //if (!curClient.IsConnected)
                //{
                //    curClient.Timer.Stop();
                //    clientDis(curClient);
                //}
                DateTime startTime = curClient.lastDateTimeConnected;
                DateTime endTime = DateTime.Now;
                TimeSpan span = endTime.Subtract(startTime);
                if (span.Seconds >= 25 ) //بیش از بیست و پنج ثانیه است که دستگاه قطع می باشد
                {
                    //Console.WriteLine("span.seconds >= 25");
                    Task.Delay(10000);
                    curClient.Timer.Stop();
                    clientDis(curClient);
                }
                //if client connected
                else
                {
                    Send(curClient.workSocket, "BgrUEy5IbpJSnhmqI2IhKw== ,vELIiOMt9rmJvLyKkKMgFQ==");
                    Util.SendWaitingTest(curClient).ConfigureAwait(false);
                    //Group Test donot need. because for each device in group Test insert 990217
                    //Util.SendWaitingGroupTest(curClient).ConfigureAwait(false);
                    Util.CheckUssdByIMEI(curClient).ConfigureAwait(false);
                    Util.UpdateVersion(curClient).ConfigureAwait(false);
                    //Util.TerminateTest(curClient).ConfigureAwait(false);
                }
            }
        }
        public static async void BeginReceiveCallback(IAsyncResult ar)
        {   
            if (ar.IsCompleted)
            {
                var client = (StateObject)ar.AsyncState;
                if (client != null)
                {
                    var sk_client = client.workSocket;
                    // Read data from the client socket.   
                    if (sk_client != null)
                    {
                        if (SocketConnected(sk_client, 0))
                        {
                            int bytesRead = sk_client.EndReceive(ar);
                            if (bytesRead > 0)
                            {  
                                client.value = Encoding.ASCII.GetString(client.buffer, 0, bytesRead).ToString();                                
                                _=Util.LogErrorAsync(new Exception($"Socekt ReadCallback IMEI1={client.IMEI1} and Ip={client.IP}"), (client.value ?? "").ToString(), client.IMEI1).ConfigureAwait(false);
                              //  if (client.value.Length > 0)
                              //  {
                                    //await CheckPacket(client.value, client);
                                    _= CheckValue(client).ConfigureAwait(false);                                    
                               // }
                               // else
                               // {
                                    // Not all data received. Get more.                              
                                    if (SocketConnected(sk_client, 0))
                                    {
                                        Array.Clear(client.buffer, 0, client.buffer.Length);
                                        try
                                        {
                                            sk_client.BeginReceive(client.buffer, 0, StateObject.BufferSize, SocketFlags.None
                                                , new AsyncCallback(BeginReceiveCallback), client);
                                        }
                                        catch(Exception ex)
                                        {
                                        _ = Util.LogErrorAsync(new Exception($"from BeginReceiveCallback has Exception --- IMEI1={client.IMEI1} and Ip={client.IP} and value ={client.value}"),
                                                  $"state =>  {client.IsConnected}",
                                                  $" last date => {client.lastDateTimeConnected}").ConfigureAwait(false);

                                        //throw ex;   
                                    }

                                }
                              //  }
                            }
                        }
                    }
                }
            }
        }
        private static async Task CheckValue(StateObject client)        
        
        {
            if (client.value.Contains("SHORU")) //has shoru
            {
                if (client.value.Contains("PAYAN")) //has payan
                {
                    //if (string.IsNullOrEmpty(client.tmpValue)) //tmp has not value
                    //{
                    //    await ParseMsg(client.value, client).ConfigureAwait(false);
                        //client.tmpValue = client.value = string.Empty;
                    //}
                   // else //tmp has value
                   // {                        
                        client.tmpValue = string.Empty;
                        await ParseMsg(client.value, client).ConfigureAwait(false);
                   // }
                }
                else //has not payan
                {
                    
                        client.tmpValue = client.value;
                        client.value = string.Empty;                    
                }
            }
            else //has not shoru
            {
                if (client.value.Contains("PAYAN")) //has payan
                {
                    if (!string.IsNullOrEmpty(client.tmpValue)) //tmp has value
                    {
                        client.tmpValue += client.value;
                        await ParseMsg(client.tmpValue, client).ConfigureAwait(false);
                        //client.tmpValue = client.value = string.Empty;
                    }
                    else //tmp has not value
                    {
                        await ParseMsg(client.value, client).ConfigureAwait(false);
                        //client.tmpValue = client.value = string.Empty;
                    }
                }
                else //has not payan
                {
                    if (string.IsNullOrEmpty(client.tmpValue)) //tmp has not value
                    {
                        await ParseMsg(client.value, client).ConfigureAwait(false);
                        //client.tmpValue = client.value = string.Empty;
                    }
                    else //tmp has value , large message body , get body until see payan
                    {
                        client.tmpValue += client.value;
                        client.value = string.Empty;
                    }
                }
            }
        }

        private static async Task ParseMsg(string content, StateObject client)
        {
            Util.ShowMessage($"Read {content.Length} bytes from IMEI1={client.IMEI1}/IP={client.IP}", ConsoleColor.Green, ConsoleColor.Green, client.IMEI1);
            content = content.Replace("SHORU", "").Replace("PAYAN", "");//remove identifires tags
            string[] bContent = content.Split(",");
            List<string> pContent = new List<string>();
            try
            {
                for (int i = 0; i < bContent.Length; i++)
                {
                    if (i < bContent.Length - 1)
                    {
                        pContent.Add(bContent[i].Substring(bContent[i].Length - 25, 25) + "," + bContent[i + 1].Substring(0, bContent[i + 1].Length - (((i + 1) == bContent.Length - 1) ? 0 : 25)));
                    }
                }
            }
            catch (Exception ex)
            {
                  _=Util.LogErrorAsync(ex, "split content", client.IMEI1+ " "+ client.IP).ConfigureAwait(false);
            }           
            try
            {
                foreach (var item in pContent)
                {
                    _ = Util.ProcessProbeRecievedContent(client, item);
                    client.value = string.Empty;
                    client.tmpValue = string.Empty;
                    //Console.WriteLine("Read {0} bytes from IMEI1={1} Ip={2}.Process And save @ {3}\n", item.Length,client.IMEI1, client.IP,DateTime.Now);
                }
            }
            catch (Exception ex)
            {
                  _=Util.LogErrorAsync(ex, "loop in content and process ReadCallback ", client.IMEI1 + " " + client.IP).ConfigureAwait(false);
            }
        }
        public static void Send(Socket socket, String data)
        {
            // Convert the string data to byte data using ASCII encoding.  
            byte[] byteData = Encoding.ASCII.GetBytes(data);
            // Begin sending the data to the remote device.             
            if (SocketConnected(socket, 0))
                {
                    try
                    {
                        socket.BeginSend(byteData, 0, byteData.Length, SocketFlags.None, new AsyncCallback(SendCallback), socket);                        
                    }
                    catch (Exception od)
                    {
                        var im = string.Empty;
                        if (DeviceList.Exists(x => x.workSocket == socket))
                        {
                            im = DeviceList.Find(x => x.workSocket == socket).IMEI1;
                        }
                        else
                        {
                            im = IPAddress.Parse(((IPEndPoint)socket.RemoteEndPoint).Address.ToString()).ToString();
                        }
                         Util.LogErrorAsync(od, $"Send>> BeginSend - IMEI = {im} @ {DateTime.Now} ").ConfigureAwait(false);
                    }
                }
            
        }
        private static void SendCallback(IAsyncResult ar)
        {
            if (ar.IsCompleted)
            {
                // Retrieve the socket from the state object.  
                Socket socket = (Socket)ar.AsyncState;
                if (SocketConnected(socket,0))
                {
                    // Complete sending the data to the remote device.  
                    //try
                    //{
                        int bytesSent = socket.EndSend(ar);
                        StateObject stateObject = DeviceList.Find(t => t.workSocket == socket);
                        if (stateObject.counter > 2) //device donot response
                        {
                            stateObject.IsConnected = false;
                        }
                        else
                        {
                        Util.ShowMessage($"Sent {bytesSent} bytes to IMEI1={stateObject.IMEI1}/IP={stateObject.IP}", ConsoleColor.Yellow, ConsoleColor.Green, stateObject.IMEI1);

                            Array.Clear(stateObject.buffer, 0, stateObject.buffer.Length);
                            //stateObject.lastDateTimeConnected = DateTime.Now;
                            stateObject.counter += 1;//set for check connect
                            try
                            {
                                //if (SocketConnected(socket, 0)) --not need

                                    socket.BeginReceive(stateObject.buffer, 0, StateObject.BufferSize, 0,
                                            new AsyncCallback(BeginReceiveCallback), stateObject);
                            }
                            catch (Exception ex)
                            {
                                _= Util.LogErrorAsync(ex, $"SendCallback >> BeginReceive - IMEI = {stateObject.IMEI1} and Ip={stateObject.IP} @ {DateTime.Now} ").ConfigureAwait(false);
                                throw ex;
                            }
                        }
                    //}
                    //catch (Exception ex)
                    //{
                    //    var im = string.Empty;
                    //    if (DeviceList.Exists(x => x.workSocket == socket))
                    //    {
                    //        im = DeviceList.Find(x => x.workSocket == socket).IMEI1;
                    //    }
                    //    else
                    //    {
                    //        im = IPAddress.Parse(((IPEndPoint)socket.RemoteEndPoint).Address.ToString()).ToString();
                    //    }
                    //     Util.LogErrorAsync(ex, $"SendCallback>> EndSend - IMEI/Ip {im} @ {DateTime.Now} ").ConfigureAwait(false);
                    //}
                }                  
            }
           
        }
        /// <summary>
        /// Socket Connected
        /// </summary>
        /// <param name="s">socket</param>
        /// <param name="mode">0=read , 1= write, 2=error</param>
        /// <returns></returns>
        private static bool SocketConnected(Socket s, int mode)
        {
            if (s == null) return false;
            bool part1 = s.Poll(1, mode==0?  SelectMode.SelectRead: mode==1? SelectMode.SelectWrite: SelectMode.SelectError);
            
            bool part2 = (s.Available == 0);

            //Console.WriteLine("Polling 1000==>"+ part1);
            //Console.WriteLine("Available ====>"+ part2);
            //Console.WriteLine("SelectedMode==>" + (mode == 0 ? SelectMode.SelectRead.ToString() : mode == 1 ? SelectMode.SelectWrite.ToString() : SelectMode.SelectError.ToString()));
            if (part1 && part2)
            { 
                return false;
            }
            else
            {
                return true;                
            }
        }
      
    }
}
