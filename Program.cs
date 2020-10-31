using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace TCPServer
{
    class Program
    {
        public static IConfigurationRoot config;
        static async Task Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
               .SetBasePath(Directory.GetCurrentDirectory())
               .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);           
            IConfigurationRoot configuration = builder.Build();            
            config = configuration;
            Util.ConnectionStrings = configuration.GetConnectionString("DefaultConnection");
            ServerConfig();
            // Task.Run(async () => ServerConfig());
            //WebHost.CreateDefaultBuilder()
            //        .SuppressStatusMessages(true) //disable status message                  
            //        .UseUrls("http://*:5000")
            //        .ConfigureServices(services =>
            //        {
            //            services.AddMvc();
            //            //added by omid --981218                    
            //            services.AddHttpClient();
            //            services.AddSingleton<IConfiguration>(configuration);//set config 
            //            services.AddElmahIo(o =>
            //            {
            //                o.ApiKey = "bfd5445308a64304a0038249e19ffa80";
            //                o.LogId = new Guid("c3524eae-82e0-46ef-96bc-42c519c484ef");
            //                o.OnMessage = msg =>
            //                {
            //                    msg.Version = "06-1";
            //                    msg.Application = "TcpServer";
            //                    msg.Hostname = "185.192.112.74";
            //                };
            //            });
            //        })
            //        .Configure((app) =>
            //        {
            //            app.UseMvc(routes =>
            //            {
            //                routes.MapRoute(
            //                    name: "default",
            //                    template: "{controller}/{action}"
            //                    );
            //            });
            //            app.UseElmahIo();
            //        }).Build().Run();
            

        }

        private static void ServerConfig()
        {
            string ip = "127.0.0.1";  string port = "6070";
            Menu();
            while (true)
            {
                var line = Console.ReadKey(true);
                switch (line.Key)
                {
                    case ConsoleKey.S:
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Please Enter Server Ip:");
                        ip = Console.ReadLine();
                        Console.Clear();
                        Menu();
                        break;
                    case ConsoleKey.P:
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Please Enter Server Port:");
                        port = Console.ReadLine();
                        Console.Clear();
                        Menu();
                        break;
                    case ConsoleKey.G:
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Please Enter General Timer(second):");
                        port = Console.ReadLine();
                        Console.Clear();
                        config.GetSection("Timer:TGenral").Value = port;
                        Menu();
                        break;
                    case ConsoleKey.C:
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Please Enter Client Timer(second):");
                        port = Console.ReadLine();
                        Console.Clear();
                        config.GetSection("Timer:TClient").Value = port;
                        Menu();
                        break;
                    case ConsoleKey.D: //localhost
                        ip = "127.0.0.1";
                        port = "6070";
                        Confirm(ip, port);
                        break;
                    case ConsoleKey.M: //localhost
                        ip = "185.192.112.74";
                        port = "6070";
                        Confirm(ip, port);
                        break;
                    case ConsoleKey.A: 
                        Confirm(ip, port);
                        break;
                    case ConsoleKey.I:
                        Console.Clear();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Server is {ip} \n");
                        Console.WriteLine($"Port is {port} \n");
                        Console.ForegroundColor = ConsoleColor.White;
                        Menu();
                        break;
                   case ConsoleKey.R: //Refresh DeviceList                        
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Starting Refresh Device \n");
                        foreach (var tmp in AsynchronousSocketListener.DeviceList)
                        {
                            if (!tmp.IsConnected || tmp.counter >= 2 || (tmp.counter < 1))
                             {                             
                                AsynchronousSocketListener.clientDis(tmp);
                             }
                        }                        
                        Menu();
                        break;
                    case ConsoleKey.K:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("**************************");
                        Console.WriteLine("Starting Kill IMEI       *");
                        Console.WriteLine("Enter IMEI :             *");
                        Console.WriteLine("**************************");
                        var clientIMEI = Console.ReadLine();
                        Console.ForegroundColor = ConsoleColor.Green;
                        var item = AsynchronousSocketListener.DeviceList.Find(x => x.IMEI1 == clientIMEI);
                        if (item != null){
                            AsynchronousSocketListener.clientDis(item);
                            _ = Util.UpdateMachineState(item.IMEI1, item.IMEI2, false);                            
                        };
                        Console.WriteLine($"Press any key to continue...");
                        Console.ReadKey();
                        Console.Clear();                        
                        break;
                    case ConsoleKey.L: //Show List Of IMEI1  in DeviceList
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("**************************");
                        Console.WriteLine("*  List Of Live IMEI     *");                        
                        Console.WriteLine("**************************");                        
                        Console.ForegroundColor = ConsoleColor.Green;
                        foreach (var tmp in AsynchronousSocketListener.DeviceList)
                        {
                            Console.WriteLine($"IMEI1={tmp.IMEI1} , " +
                                $" Isconnected= {tmp.IsConnected}," +
                                $" HandShakeCounter={tmp.counter}, " +
                                $"Last Date Connected ={tmp.lastDateTimeConnected.ToString("yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)} \n");
                        }
                        Console.WriteLine($"Press any key to continue...");
                        Console.ReadKey();
                        Console.Clear();                                                
                        break;
                    default:
                        Console.WriteLine("Please Enter Correct Command.");
                        break;
                }
            }
        }
        private static void Menu()
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("*********************************************************");            
            Console.WriteLine($"                     Menu Command                       ");            
            Console.WriteLine("*********************************************************");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($" (S) Set Server ip                                      ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (P) Set Server Port                                    ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (G) Set General Timer(Default=10 second)               ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (C) Set Client Timer(Default=10 second)                ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (A) Apply Ip and Port and start                        ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (D) Default   Server Start                             ");
            Console.WriteLine("*********************************************************");            
            Console.WriteLine($" (M) KavoshKom Server Start                             ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (I) Information                                        ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (R) Refresh Device List                                ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (L) List Live IMEI                                     ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (K) Kill By   IMEI                                     ");
            Console.WriteLine("*********************************************************");
            Console.WriteLine($" (CTRL+C) ShutDown                                      ");
            Console.WriteLine("*********************************************************");
        }
        private static string Confirm(string ip, string port)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Does Server By {ip} on port {port} Starting?(y/n) ");
            var auth = Console.ReadLine();
            if (auth.ToLower().StartsWith("y"))
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Green;
                var t = Task.Run(() =>
                {
                    AsynchronousSocketListener.StartListening(ip, Convert.ToInt32(port), config);
                });               
            }
            else
            {
                //Console.WriteLine($"Please init Server Ip and Port then Press C ");
                Console.Clear();
                Menu();
            }
            return auth;
        }
    }
}
