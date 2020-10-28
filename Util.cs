using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TCPServer.Models;

namespace TCPServer
{
    public static class Util
    {
        public static string serverPath { get; set; }
        public static string ConnectionStrings { get; set; }
        public readonly static CultureInfo EnglishCulture = new CultureInfo("en-US");
        internal static async Task ProcessProbeRecievedContent(StateObject state, string content)
        {
            try
            {
                string text = (content ?? ",").ToString().Split(" ,")[1];
                string vikey = (content ?? ",").ToString().Split(" ,")[0];
                string body = text.Decrypt("sample_shared_secret", vikey);
                _ = Util.LogErrorAsync(new Exception("Socekt ReadCallback"), (body ?? "").ToString(), state.IP);
                string[] paramArray = body.Replace("\\", string.Empty).Replace("\"", string.Empty).Split('#');
                switch (paramArray[0])
                {
                    case "MID":  //for register device and first connection to server --omid added
                        ProcessMIDRecievedContent(state, paramArray).ConfigureAwait(false);
                        break;
                    case "SRQ":  //Device Say that want sync
                        ProcessDeviceWantSync(state, paramArray).ConfigureAwait(false);
                        break;
                    case "TSC": //for communication and run task on device between device and server --omid added
                        ProcessTSCRecievedContent(state, paramArray).ConfigureAwait(false);
                        break;
                    case "UPG": //Device Say that , its get Update message 
                        _ = ProcessUPGRecievedContent(state, paramArray).ConfigureAwait(false);
                        break;
                    case "UPR": //Device Say that , Download newUpdate file finished
                        _ = ProcessUPRRecievedContent(state, paramArray).ConfigureAwait(false);
                        break;
                    case "RPL": //Device Send this message and mean download newUpdate file and install Done Successful
                        _ = ProcessRPLRecievedContent(state, paramArray).ConfigureAwait(false);
                        break;
                    case "LST": //for show low battery device ... --omid added // در واقغ وسیله وقتی شارژش از یک مقداری کمتر شد با این پیام به سرور اطلاع داده و قطع میشود
                        ProcessLowBatteryDevice(state, paramArray);
                        break;
                    case "LOC": //for show Gps Device every 50 second ... --omid added 990220//هر پنجاه ثانیه یکبار جی پی اس از دستگاه دریافت میشود//addby omid
                        
                        _ = ProcessLoCDevice(state, paramArray);
                        break;
                    case "FLD": //اگر زمان پایان یا شروغ تست درست تعریف نشده باشد این پیام دریافت میگردد که باید تست را تمام شده در نظرگرفت//addby omid
                        _ = ProcessFLDDevice(state, paramArray);
                        break;
                    case "SFS": //فایل لاگ ارسالی برای عمل همسانسازی توسط دستگاه از طریق این پیام دریافت  میگردد//addby omid
                                //format msg from clietn : SFS#SId#IM1#TIME
                        _ = ProcessSFSDevice(state, paramArray).ConfigureAwait(false);
                        break;
                    case "SYE": //log file Upload Successfully, end Sync client by Server.Now Server Start Update Tables.
                                //format msg from clietn : SYE#SId#IM1#TIME
                        _ = ProcessSYEDevice(state, paramArray).ConfigureAwait(false);
                        break;
                    case "USG":
                        _ = ProcessUSG(state, paramArray);
                        break;
                    case "USD":
                    case "SMS":
                        ProcessUSDSMS(state, paramArray).ConfigureAwait(false);
                        break;
                    case "YesI'mThere": //addby omid
                        if(paramArray.Length > 1)
                        {
                            DateTime.TryParse(paramArray[1].Substring(5, paramArray[1].Length - 5), out DateTime fromDevice);
                            _ = CheckHandShake(state, fromDevice).ConfigureAwait(false);
                        }
                        else
                        {
                            _ = CheckHandShake(state,null).ConfigureAwait(false);
                        }                        
                        break;
                    default:
                        break;
                }
            }
            catch (Exception e)
            {
                _ = Util.LogErrorAsync(e, $"ProcessProbeRecievedContent >> Decrypt/Replace - @ {DateTime.Now}");
            }
        }
        private static async Task ProcessDeviceWantSync(StateObject state, string[] paramArray)
        {
            if (AsynchronousSocketListener.Imei1Log == "All" ||  state.IMEI1 == AsynchronousSocketListener.Imei1Log )
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"*************************************");
                Console.WriteLine($"Get SRQ From IMEI1 ={state.IMEI1} @{DateTime.Now}");
                Console.WriteLine($"*************************************");
                Console.ForegroundColor = ConsoleColor.Green;
            }
             Util.SyncDevice(state);
        }

        /// <summary>
        /// بروزرسانی وضعیت رکورد 
        /// Uss
        /// پس از دریافت پیام توسط دستگاه
        /// </summary>
        /// <param name="state"></param>
        /// <param name="paramArray"></param>
        /// <returns></returns>
        private static async Task ProcessUSG(StateObject state, string[] paramArray)
        {
            try
            {
                _ = LogErrorAsync(new Exception("update ussd"), string.Join("@",paramArray), $"Update Ussd -- 108 ").ConfigureAwait(false);
                int.TryParse(paramArray[1].Split(":")[1],out int UsId);
                DateTime.TryParse(paramArray[3].Substring(5, paramArray[3].Length - 5), out DateTime Time);
                using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
                {
                    connection.Open();
                    try
                    {
                        //Update status of Uss record..Mean Device Get USS
                        
                        string sql = "Update MachineUssd set Status=2,DateFromDevice=@fromDevice where Id=@UsId ";                            
                        using (SqlCommand command = new SqlCommand(sql, connection))
                        {
                            command.CommandTimeout = 100000;
                            command.CommandType = CommandType.Text;                            
                            command.Parameters.AddWithValue("@UsId", UsId); 
                            command.Parameters.AddWithValue("@fromDevice",Time);
                            await command.ExecuteScalarAsync().ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _ = LogErrorAsync(ex, ex.Message, $"Update Ussd -- 108 ").ConfigureAwait(false);
                    }
                    finally
                    {
                        connection.Close();
                    }                    
                }

            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "ProcessUsd", String.Join("", paramArray)).ConfigureAwait(false);
            }

        }

        private static async Task ProcessUSDSMS(StateObject state, string[] paramArray)
        {
            try
            {
                var UsdOrSms = paramArray[0];
                if (UsdOrSms == "SMS")
                {
                    LogErrorAsync(new Exception("ProcessSMS"), String.Join("", paramArray), "ProcessSMS").ConfigureAwait(false);
                    ProcessSMS(state, paramArray);
                }
                else //usdorsms="USD"
                {
                    int.TryParse(paramArray[1].Split(':')[1], out int UsId);
                    var Im1 = paramArray[2].Split(':')[1];
                    var Im2 = paramArray[3].Split(':')[1];
                    short.TryParse(paramArray[4].Split(':')[1], out short Modem);
                    short.TryParse(paramArray[5].Split(':')[1], out short Sim);
                    var Operator = paramArray[6].Split(':')[1];
                    var Iccid = paramArray[7].Split(':')[1];
                    var simbody = paramArray[8].Split(':')[1];//not Used
                    var Body = paramArray[9].Split(':')[1];
                    DateTime.TryParse(paramArray[10].Substring(5, paramArray[10].Length - 5), out DateTime Time);
                    await insertUssd(Im1, Im2, Body, simbody, Modem, Sim, Operator, Iccid, Time, 3, UsId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "ProcessUsd", String.Join("", paramArray)).ConfigureAwait(false);
            }

        }

        internal static async Task TerminateTest(StateObject state)
        {
            string TerminateTest = "";
            string VIKey = "BgrUEy5IbpJSnhmqI2IhKw==";
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                try
                {
                    string sql = $"SELECT * from TempTerminteTest ttt " +
                        $" where ttt.machineId = (select id from machine where IMEI1 = @IMEI1) for json path";                                                
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IMEI1",state.IMEI1); //state.IMEI1);
                        connection.Open();
                        TerminateTest = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, " SendTerminateTest", $"IMEI1={state.IMEI1} Ip={state.IP}");
                }
                finally
                {
                    connection.Close();
                }
            }
            if (TerminateTest != null)
            {
                if (AsynchronousSocketListener.Imei1Log =="All" || state.IMEI1 == AsynchronousSocketListener.Imei1Log)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"*************************************");
                    Console.WriteLine($"Send TRM From IMEI1 ={state.IMEI1} @{DateTime.Now}");
                    Console.WriteLine($"*************************************");
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                foreach (var item in JArray.Parse(TerminateTest))
                {
                    var content = ("TRM#" + item.ToString().Replace("}", "").Replace(",", "#").
                        Replace("{", "")).Replace(" ", "").Replace("\r", "").Replace("\n", "").ToString().
                        Encrypt("sample_shared_secret", VIKey);                    
                    AsynchronousSocketListener.Send(state.workSocket, VIKey + " ," + content);
                   // LogErrorAsync(new Exception("TRMSEND"), String.Join("", item), content).ConfigureAwait(false);
                }
            }
        }

        private static async Task ProcessSMS(StateObject state, string[] paramArray)
        {
            try
            {
                var Im1 = paramArray[1].Split(':')[1];
                var Im2 = paramArray[2].Split(':')[1];
                short.TryParse(paramArray[3].Split(':')[1], out short Modem);
                short.TryParse(paramArray[4].Split(':')[1], out short Sim);
                string opratoros = paramArray[5].Split(':')[1];
                string Iccid = paramArray[6].Split(':')[1];
                //var bdy1 = paramArray[7].Split("\r\n");
                //var Smss_body =bdy.Split("+CMGL:");
                var Body = paramArray[7].Substring(6, paramArray[7].Length - 6);//paramArray[7].Split(':')[1];
                DateTime.TryParse(paramArray[8].Substring(5, paramArray[8].Length - 5), out DateTime Time);
                await insertSms(Im1, Im2, Body, Modem, Sim, opratoros, Iccid, Time).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "ProcessSMS", String.Join("", paramArray)).ConfigureAwait(false);
            }

        }
        private static async Task ProcessSYEDevice(StateObject state, string[] paramArray)
        {
            if (AsynchronousSocketListener.Imei1Log == "All" || state.IMEI1 == AsynchronousSocketListener.Imei1Log)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"*************************************");
                Console.WriteLine($"Get Sye From IMEI1 ={state.IMEI1} @{DateTime.Now}");
                Console.WriteLine($"*************************************");
                Console.ForegroundColor = ConsoleColor.Green;
            }
            _ = LogErrorAsync(new Exception($"SYE of Sync Process on IMEI1{state.IMEI1}"), $"IMEI ={state.IMEI1} IP= {state.IP}", String.Join(",", paramArray)).ConfigureAwait(false);
            int syncMasterId; int.TryParse(paramArray[1].Split(':')[1], out syncMasterId);
            await UpdateMasterDetailSync(state, syncMasterId, "SYE", true);
        }
        private static async Task CheckHandShake(StateObject state,DateTime? fromDevice)
        {
            //state.Timer.Stop();         
                state.IsConnected = true;
                //if (fromDevice!=null)
                //{
                //    state.lastDateTimeConnected = (DateTime)fromDevice;
                //}
                //else
                //{
                //    state.lastDateTimeConnected = DateTime.Now;
                //}
                state.lastDateTimeConnected = DateTime.Now;
                state.counter = 0;
            //state.Timer.Start();
            await UpdateMachineState(state.IMEI1, state.IMEI2, true).ConfigureAwait(false);
        }
        private async static Task ProcessSFSDevice(StateObject state, string[] paramArray)
        {
            int syncMasterId; int.TryParse(paramArray[1].Split(':')[1], out syncMasterId);
            await UpdateMasterDetailSync(state, syncMasterId, "SFS").ConfigureAwait(false);
        }
        private async static Task SyncDevice(StateObject state)
        {
            if (AsynchronousSocketListener.Imei1Log=="All" || state.IMEI1 ==AsynchronousSocketListener.Imei1Log )
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"*************************************");
                Console.WriteLine($"Sync Time IMEI1 ={state.IMEI1} IP={state.IP}");
                Console.WriteLine($"*************************************");
                Console.ForegroundColor = ConsoleColor.Green;
            }           
            try
            {
                await AddSync(state).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "100 --Method-- ProcessFLDDevice-StopSendTest").ConfigureAwait(false);
            }
        }
        private async static Task<bool> UpdateMasterDetailSync(StateObject state, int syncMasterId, string step, bool? iscompeleted = null)
        {
            bool result = false;
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                connection.Open();
                var tx = connection.BeginTransaction();
                try
                {
                    var sql = $"insert into SyncDetail (PsyncId,CreateDate,Command,status) values (@PsyncId,@CreateDate,@Command,@status)";
                    using (SqlCommand command2 = new SqlCommand(sql, connection))
                    {
                        command2.CommandTimeout = 100000;
                        command2.CommandType = CommandType.Text;
                        command2.Transaction = tx;
                        command2.Parameters.AddWithValue("@PsyncId", syncMasterId);
                        command2.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                        command2.Parameters.AddWithValue("@Command", step);
                        command2.Parameters.AddWithValue("@status", 1);
                        await command2.ExecuteScalarAsync().ConfigureAwait(false);

                        if (iscompeleted != null)
                        {
                            sql = "update SyncMaster set Status=@Status , IsCompeleted = @IsCompeleted  where Id = @syncMasterid  ";
                        }
                        else
                        {
                            sql = "update SyncMaster set Status=@Status  where Id = @syncMasterid  ";
                        }

                        using (SqlCommand command = new SqlCommand(sql, connection))
                        {
                            command.CommandTimeout = 100000;
                            command.CommandType = CommandType.Text;
                            command.Transaction = tx;
                            command.Parameters.AddWithValue("@Status", 2);
                            command.Parameters.AddWithValue("@syncMasterid", syncMasterId);
                            if (iscompeleted != null)
                            {
                                command.Parameters.AddWithValue("@IsCompeleted", iscompeleted);
                            }
                            await command.ExecuteScalarAsync().ConfigureAwait(false);
                            tx.Commit();
                            if (AsynchronousSocketListener.Imei1Log == "All" || state.IMEI1 == AsynchronousSocketListener.Imei1Log)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"*************************************");
                                if (iscompeleted != null)
                                {
                                    Console.WriteLine($"Successfully Upload Sync File from IMEI1 ={state.IMEI1} IP={state.IP}");
                                    _ = LogErrorAsync(new Exception($"Successfully Upload {step} of Sync Process on IMEI1{state.IMEI1}"), $"IMEI ={state.IMEI1} IP= {state.IP}").ConfigureAwait(false);
                                }
                                else
                                {
                                    Console.WriteLine($"Success: {step} of Sync Process on IMEI1 ={state.IMEI1} IP={state.IP}");
                                    _ = LogErrorAsync(new Exception($"{step} of Sync Process on IMEI1{state.IMEI1}"), $"IMEI ={state.IMEI1} IP= {state.IP}").ConfigureAwait(false);
                                }
                                Console.WriteLine($"*************************************");
                                Console.ForegroundColor = ConsoleColor.Green;
                            }
                            result = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"*************************************");
                    Console.WriteLine($"Failed: {step} of Sync Process on IMEI1 ={state.IMEI1} IP={state.IP}");
                    Console.WriteLine($"*************************************");
                    Console.ForegroundColor = ConsoleColor.Green;
                    _ = LogErrorAsync(ex, $"Failed { step} of Sync Process on IMEI1{ state.IMEI1}", $"IMEI ={state.IMEI1} IP= {state.IP}").ConfigureAwait(false);
                    tx.Rollback();
                    result = false;
                }
                finally
                {
                    connection.Close();
                }
            }
            return result;
        }
        /// <summary>
        /// Sync  Proccess Update. because disconnectTime Don't need for client
        /// therefore machineConnectionHistory Check Remove
        /// rahimi.Aeini.Dr.VahidPour
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>       
        private async static Task AddSync(StateObject state)
        {
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                connection.Open();
                var tx = connection.BeginTransaction();
                try
                {
                    string sql = "insert into SyncMaster" +
                        "(MachineId,IMEI1,Status,CreateDate,DisconnectedDate,CntFileGet,IsCompeleted) " +
                    " values ((select id from machine where IMEI1 =@IMEI1),@IMEI1,@Status,@CreateDate,@DisconnectedDate,@CntFileGet,@IsCompeleted);" +
                    " select SCOPE_IDENTITY()";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Transaction = tx;
                        command.Parameters.AddWithValue("@IMEI1", state.IMEI1);
                        command.Parameters.AddWithValue("@Status", 1);
                        command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                        command.Parameters.AddWithValue("@DisconnectedDate", DBNull.Value);
                        command.Parameters.AddWithValue("@CntFileGet", 0);
                        command.Parameters.AddWithValue("@IsCompeleted", 0);
                        var syncMasterId = (decimal)await command.ExecuteScalarAsync().ConfigureAwait(false);
                        sql = $"insert into SyncDetail (PsyncId,CreateDate,Command,status) values (@PsyncId,@CreateDate,@Command,@status)";
                        using (SqlCommand command2 = new SqlCommand(sql, connection))
                        {
                            command2.CommandTimeout = 100000;
                            command2.CommandType = CommandType.Text;
                            command2.Transaction = tx;
                            command2.Parameters.AddWithValue("@PsyncId", syncMasterId);
                            command2.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                            command2.Parameters.AddWithValue("@Command", "SRQ");
                            command2.Parameters.AddWithValue("@status", 1);
                            await command2.ExecuteScalarAsync().ConfigureAwait(false);
                            sql = $"insert into SyncDetail (PsyncId,CreateDate,Command,status) values (@PsyncId,@CreateDate,@Command,@status)";
                            using (SqlCommand command3 = new SqlCommand(sql, connection))
                            {
                                command3.CommandTimeout = 100000;
                                command3.CommandType = CommandType.Text;
                                command3.Transaction = tx;
                                command3.Parameters.AddWithValue("@PsyncId", syncMasterId);
                                command3.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                command3.Parameters.AddWithValue("@Command", "SYN");
                                command3.Parameters.AddWithValue("@status", 1);
                                await command3.ExecuteScalarAsync().ConfigureAwait(false);
                                tx.Commit();
                                string VIKey = "BgrUEy5IbpJSnhmqI2IhKw==";
                                var msg = ($"SYN#\"SId\":{syncMasterId}#" + $"\"DisconnectTime\":\"\"");
                                var content = msg.Encrypt("sample_shared_secret", VIKey);
                                AsynchronousSocketListener.Send(state.workSocket, VIKey + " ," + content);
                                if (AsynchronousSocketListener.Imei1Log == "All" || state.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"*************************************");
                                    Console.WriteLine($"Send Sync IMEI1 ={state.IMEI1} IP={state.IP}");
                                    Console.WriteLine($"*************************************");
                                    Console.ForegroundColor = ConsoleColor.Green;
                                }
                                _ = LogErrorAsync(new Exception($"Send Sync IMEI1{state.IMEI1}"), msg, $"IMEI ={state.IMEI1} IP= {state.IP}").ConfigureAwait(false);
                            }
                        }                        
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"*************************************");
                    Console.WriteLine($"Dont Send Sync IMEI1 ={state.IMEI1} IP={state.IP}");
                    Console.WriteLine($"*************************************");
                    Console.ForegroundColor = ConsoleColor.Green;
                    _ = LogErrorAsync(ex, ex.Message, $"IMEI ={state.IMEI1} IP= {state.IP}").ConfigureAwait(false);
                    tx.Rollback();
                }
                finally
                {
                    connection.Close();
                }
            }
        }
        private async static Task<decimal> insertUssd(string im1, string im2, string body, string Simbody,
                                             short modem, short sim, string Operator, string Iccid,
                                            DateTime DateFromDevice, short status, int? ParentId)
        {
            decimal res = 0;
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                connection.Open();
                try
                {
                    //first: Update status of parent record
                    //second: insert child record
                    string sql = "Update MachineUssd set Status=2 where Id=@ParentId " +
                        "insert into MachineUssd (Machineid,Modem,Sim,body,Simbody,DateFromDevice,Status,msg,ParentId,Operator,Iccid) " +
                    " values ((select Id from machine where IMEI1=@Im1 and IMEI2=@Im2),@Modem,@Sim,@body,@Simbody,@DateFromDevice,@Status,@msg,@ParentId,@Operator,@Iccid);select SCOPE_IDENTITY()";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@Im1", im1);
                        command.Parameters.AddWithValue("@Im2", im2);
                        command.Parameters.AddWithValue("@Modem", modem);
                        command.Parameters.AddWithValue("@Sim", sim);
                        command.Parameters.AddWithValue("@body", body);
                        command.Parameters.AddWithValue("@Simbody", Simbody);
                        command.Parameters.AddWithValue("@DateFromDevice", DateFromDevice);
                        command.Parameters.AddWithValue("@Iccid", Iccid);
                        command.Parameters.AddWithValue("@operator", Operator);
                        command.Parameters.AddWithValue("@Status", status);
                        command.Parameters.AddWithValue("@msg", "USD");
                        if (ParentId == null)
                            command.Parameters.AddWithValue("@ParentId", DBNull.Value);
                        else
                            command.Parameters.AddWithValue("@ParentId", ParentId);
                        res = (decimal)await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, ex.Message, $"insertUssd -- 377 ").ConfigureAwait(false);
                }
                finally
                {
                    connection.Close();
                }
                return res;
            }
        }
        private async static Task<int> insertSms(string im1, string im2, string body,
                                              short modem, short sim, string Operator, string Iccid,
                                              DateTime DateFromDevice)
        {

            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                connection.Open();
                try
                {
                    string sql =
                        "insert into MachineUSSD (machineId,Imei1,Imei2,modem,Sim,operator,Iccid,body,msg,DateFromDevice) " +
                    " values ((select Id from machine where IMEI1=@Im1 and IMEI2=@Im2),@Im1,@Im2,@Modem,@Sim,@operator,@Iccid,@body,@msg,@DateFromDevice);select SCOPE_IDENTITY()";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        //command.Parameters.AddWithValue("@Id", Guid.NewGuid());
                        command.Parameters.AddWithValue("@Im1", im1);
                        command.Parameters.AddWithValue("@Im2", im2);
                        command.Parameters.AddWithValue("@Modem", modem);
                        command.Parameters.AddWithValue("@Sim", sim);
                        command.Parameters.AddWithValue("@body", body);
                        command.Parameters.AddWithValue("@DateFromDevice", DateFromDevice);
                        command.Parameters.AddWithValue("@Iccid", Iccid);
                        command.Parameters.AddWithValue("@operator", Operator);
                        command.Parameters.AddWithValue("@msg", "SMS");
                        var rest = (object)await command.ExecuteScalarAsync().ConfigureAwait(false);
                        if (rest != null)
                        {
                            return 1;
                        }
                        else
                        {
                            return 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, ex.Message, $"insertSms -- 406").ConfigureAwait(false);
                }
                finally
                {
                    connection.Close();
                }
            }
            return 0;
        }
        public async static Task SendUSS(StateObject state, int machineId, string ime1, string ime2)
        {
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                connection.Open();
                try
                {
                    string sql = "Select top 1 * from MachineUssd where Status=0 and Machineid=@MachineId and ParentId is NULL";
                    string msg = string.Empty;
                    int usid = 0;
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@MachineId", machineId);
                        command.Parameters.AddWithValue("@msg", "USS");
                        var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                        if (reader.Read())
                        {
                            var res = new string[8];
                            res[0] = reader["Id"].ToString();
                            int.TryParse(reader["Id"].ToString(), out usid);
                            res[1] = reader["MachineId"].ToString();
                            res[2] = reader["msg"].ToString();
                            res[3] = reader["Modem"].ToString();
                            res[4] = reader["Sim"].ToString();
                            res[5] = reader["body"].ToString();
                            res[6] = reader["CreatedDate"].ToString();
                            res[7] = reader["Status"].ToString();
                            var curDate = Convert.ToDateTime(res[6]);
                            msg = ($"USS#\"USId\":{res[0]}#" + $"\"IM1\":\"{ime1}\"" +
                            $"#\"IM2\":\"{ime2}\"#\"Modem\":{res[3]}#\"SIM\":{res[4]}#\"Body\":\"{res[5]}\"#" +
                            $"\"TIME\":\"{curDate.ToString("yyyy-M-dHH-mm-ss", System.Globalization.CultureInfo.InvariantCulture)}\"");
                        }
                        reader.Close();
                    }
                    if (!string.IsNullOrEmpty(msg))// if uss exist
                    {
                        string VIKey = "BgrUEy5IbpJSnhmqI2IhKw==";
                        var content = msg.Encrypt("sample_shared_secret", VIKey);
                        AsynchronousSocketListener.Send(state.workSocket, VIKey + " ," + content);
                        if (AsynchronousSocketListener.Imei1Log == "All" || state.IMEI1 == AsynchronousSocketListener.Imei1Log)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"*************************************");
                            Console.WriteLine($"Send USSD To IMEI1 ={state.IMEI1},IMEI2 ={state.IMEI2} IP={state.IP}");
                            Console.WriteLine($"*************************************");
                            Console.ForegroundColor = ConsoleColor.Green;
                        }
                        _ = LogErrorAsync(new Exception($"Send USSD To IMEI1={state.IMEI1},IMEI2={state.IMEI2}"), msg, $"IMEI ={state.IMEI1},IMEI2={state.IMEI2} IP= {state.IP}").ConfigureAwait(false);
                        try
                        {
                            sql = "Update MachineUssd Set Status=1 Where Id =@UsId"; //mean Send  Uss To Device
                            using (SqlCommand command = new SqlCommand(sql, connection))
                            {
                                command.CommandTimeout = 100000;
                                command.CommandType = CommandType.Text;
                                command.Parameters.AddWithValue("@UsId", usid);
                                var UpdatedUsIdStatus = await command.ExecuteReaderAsync().ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, "405 --Method-- SendUSS-Update MachineUssd").ConfigureAwait(false);
                        }
                        finally
                        {
                            connection.Close();
                        }
                    }

                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"*************************************");
                    Console.WriteLine($"Dont Send USSD IMEI1 ={state.IMEI1},IMEI2={state.IMEI2} IP={state.IP}");
                    Console.WriteLine($"*************************************");
                    Console.ForegroundColor = ConsoleColor.Green;
                    _ = LogErrorAsync(ex, ex.Message, $"IMEI ={state.IMEI1},IMEI2={state.IMEI2} IP= {state.IP}").ConfigureAwait(false);
                }
                finally
                {
                    connection.Close();
                }
            }
        }
        public async static Task CheckUssdByIMEI(StateObject state)
        {
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                connection.Open();
                try
                {
                    string sql = "Select top 1 * from Machine where IMEI1=@IMEI1 and IMEI2=@IMEI2";
                    var res = new string[8];
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IMEI1", state.IMEI1);
                        command.Parameters.AddWithValue("@IMEI2", state.IMEI2);
                        var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                        if (reader.Read())
                        {
                            res[0] = reader["Id"].ToString();
                            res[1] = reader["IMEI1"].ToString();
                            res[2] = reader["IMEI2"].ToString();
                            res[3] = reader["Name"].ToString();
                        }
                        reader.Close();
                    }
                    if (res != null)
                    {
                        _ = SendUSS(state, Convert.ToInt32(res[0]), res[1], res[2]);
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, ex.Message, $"Get MachineInfo Err --475").ConfigureAwait(false);

                }
                finally
                {
                    connection.Close();
                }
            }
        }
        private async static Task ProcessFLDDevice(StateObject state, string[] paramArray)
        {
            if (paramArray.Length > 2)
            {
                if (!string.IsNullOrEmpty(paramArray[2].Split(':')[1]))
                {
                    int.TryParse(paramArray[2].Split(':')[1], out int definedTestMachineId);
                    if (AsynchronousSocketListener.Imei1Log == "All" || state.IMEI1 == AsynchronousSocketListener.Imei1Log)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"*************************************");
                        Console.WriteLine("        Time Error In Test            ");
                        Console.WriteLine($"Test {definedTestMachineId} has Error in Date/Time");
                        Console.WriteLine($"*************************************");
                        Console.ForegroundColor = ConsoleColor.Green;
                    }
                    await StopSendTest(definedTestMachineId).ConfigureAwait(false);
                }
            }
        }
        /// <summary>
        /// تستی که بازه زمانی نامناسب دارد را
        /// دیگر برای دستگاه ارسال نمی کنیم
        /// </summary>
        /// <param name="definedTestMachineId">آی دی تست دریافتی</param>
        /// <returns></returns>
        private async static Task StopSendTest(int definedTestMachineId)
        {
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                try
                {
                    string sql = $"Update  definedTestMachine Set Status=1, FinishTime=@currentDate where Id =@Id";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@Id", definedTestMachineId);
                        command.Parameters.AddWithValue("@currentDate", DateTime.Now);
                        connection.Open();
                        await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "100 --Method-- ProcessFLDDevice-StopSendTest").ConfigureAwait(false);
                }
                finally
                {
                    connection.Close();
                }
            }
        }

        /// <summary>
        /// Get Gps From Device
        /// </summary>
        /// <param name="state"></param>
        /// <param name="paramArray"></param>
        private async static Task ProcessLoCDevice(StateObject state, string[] paramArray)
        {
            if (paramArray.Length > 4)
            {
                double lat = 0, lon = 0;
                if (!string.IsNullOrEmpty(paramArray[5]) && paramArray[5].ToLower() != "nan")
                {
                    if (AsynchronousSocketListener.Imei1Log == "All" || state.IMEI1 == AsynchronousSocketListener.Imei1Log)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"*************************************");
                        Console.WriteLine($"Get LOC From IMEI1 ={state.IMEI1} @{DateTime.Now}");                        
                        Console.WriteLine($"*************************************");
                        Console.ForegroundColor = ConsoleColor.Green;
                    }

                    if (paramArray[5].Split(':')[1] != ",,,,,,,,")
                    {
                        var gpsTmp = paramArray[5].Split(':')[1];
                        var gpsData = gpsTmp.Split(',');
                        //var timeFromGPGGA = gpsData[1];
                        var latFromGPGGA = gpsData[2];
                        var lonFromGPGGA = gpsData[4];
                        var altitudeFRomGPGGA = gpsData[9];

                        //var timeFromGPRMC = gpsData[17];
                        //var A_FromGPRMC = gpsData[18];
                        //var latFromGPRMC = gpsData[19];
                        //var lonFromGPRMC = gpsData[21];
                        //var dateFromGPRMC = gpsData[25];
                        var speedKnotFromGPVTG = gpsData[36];
                        var speedKiloFromGPVTG = gpsData[38];

                        lat = int.Parse(latFromGPGGA.Substring(0, 2)) + 
                            double.Parse(latFromGPGGA.Substring(2, 6)) / 60;
                        lon = int.Parse(lonFromGPGGA.Substring(0, 3)) + 
                            double.Parse(lonFromGPGGA.Substring(3, 6)) / 60;
                        double.TryParse(speedKiloFromGPVTG, out double Speed);
                        double.TryParse(speedKnotFromGPVTG, out double Speed2);
                        double.TryParse(altitudeFRomGPGGA, out double Altitude);
                        float.TryParse(paramArray[4].Split(':')[1], out float cpuTemp);
                        DateTime.TryParse(paramArray[3].Substring(5, paramArray[3].Length - 5), out DateTime fromDevice);
                        if (state.IMEI1 != null && state.IsConnected)
                        {
                            var imei2 = paramArray[2].Split(':')[1];
                            await UpdateMachineLocation(state.IMEI1, imei2, lat.ToString(), lon.ToString(), Speed,Speed2,Altitude, fromDevice, cpuTemp).ConfigureAwait(false);
                        }
                    }
                }
            }
        }
        private static void ProcessLowBatteryDevice(StateObject state, string[] paramArray)
        {
            Console.WriteLine($"Device by IMEI1{state.IMEI1} and Ip {state.IP} Low Battey");
        }

        /// <summary>
        /// Download newFIle  Done and Update Device Version Compelete
        /// </summary>
        /// <param name="stateobject"></param>
        /// <param name="paramArray"></param>
        /// <returns></returns>
        private static async Task ProcessRPLRecievedContent(StateObject stateobject, string[] paramArray)
        {
            try
            {
                int.TryParse(paramArray[1].Split(':')[1], out int VID);
                if (VID > 0) //Version Id Check
                {
                    try
                    {
                        if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateobject.IMEI1))
                        {
                            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
                            {
                                try
                                {
                                    string sql = $"INSERT MachineVersionDetail (VersionId,State,CreateDate,Sender,Reciever) VALUES" +
                                        $"(@VersionId,@State,@CreateDate,@Sender,@Reciever)";
                                    using (SqlCommand command = new SqlCommand(sql, connection))
                                    {
                                        command.CommandTimeout = 100000;
                                        command.CommandType = CommandType.Text;
                                        command.Parameters.AddWithValue("@VersionId", VID);
                                        command.Parameters.AddWithValue("@State", "RPL");
                                        command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                        command.Parameters.AddWithValue("@Sender", stateobject.IMEI1);
                                        command.Parameters.AddWithValue("@Reciever", "Server");
                                        connection.Open();
                                        await command.ExecuteScalarAsync().ConfigureAwait(false);
                                        if (AsynchronousSocketListener.Imei1Log == "All" || stateobject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"*************************************");
                                            Console.WriteLine($"Get RPL from IMEI1={stateobject.IMEI1} Ip={stateobject.IP}");
                                            Console.WriteLine($"*************************************");
                                            Console.ForegroundColor = ConsoleColor.Green;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _ = LogErrorAsync(ex, "100 --Method-- ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                }
                                finally
                                {
                                    connection.Close();
                                }
                                try
                                {
                                    //update IsDone in  machineVersion Table ==>  mean...Update Device Finished 
                                    string sql = $"Update MachineVersion Set IsDone = 1 , CompleteDate = @CompleteDate " +
                                                $" where Id = @VersionId";
                                    using (SqlCommand command = new SqlCommand(sql, connection))
                                    {
                                        command.CommandTimeout = 100000;
                                        command.CommandType = CommandType.Text;
                                        command.Parameters.AddWithValue("@VersionId", VID);
                                        command.Parameters.AddWithValue("@CompleteDate", DateTime.Now);
                                        connection.Open();
                                        await command.ExecuteScalarAsync().ConfigureAwait(false);
                                        try
                                        {
                                            int machineId = 0;
                                            string fileDownload = string.Empty;
                                            using (SqlCommand cc = new SqlCommand(sql, connection))
                                            {
                                                cc.CommandText = $"Select * from MachineVersion where Id={VID}";
                                                using var reader = await cc.ExecuteReaderAsync().ConfigureAwait(false);
                                                if (await reader.ReadAsync())
                                                {
                                                    fileDownload = reader["FileDownloadAddress"].ToString();
                                                    int.TryParse(reader["MachineId"].ToString(), out machineId);
                                                }
                                            }
                                            if (!string.IsNullOrEmpty(fileDownload) && machineId > 0) //Update Version In Machine 
                                            {
                                                var ar = fileDownload.Split('/');
                                                var versionNum = ar[ar[0].Length - 1]; //FileName format : nameFile-VersionNumb.zip  => config-1.0.0.zip                           

                                                command.CommandText = "Update Machine set Version=@NewVersion  where Id=@machineId";
                                                command.Parameters.Clear();
                                                try
                                                {
                                                    command.Parameters.AddWithValue("@NewVersion", versionNum.Split('-')[1].Substring(0, 5));//Second part of FileName is Version
                                                    command.Parameters.AddWithValue("@machineId", machineId);
                                                    await command.ExecuteScalarAsync().ConfigureAwait(false);
                                                    if (AsynchronousSocketListener.Imei1Log == "All" || stateobject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                                    {
                                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                                        Console.WriteLine($"*************************************");
                                                        Console.WriteLine($"Device by IMEI1={stateobject.IMEI1} IP={stateobject.IP} @ {DateTime.Now.ToString("yyyy-M-d HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture)} Updated Compelete.");
                                                        Console.WriteLine($"*************************************");
                                                        Console.ForegroundColor = ConsoleColor.Green;
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    _ = LogErrorAsync(ex, "153 --Method-- ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _ = LogErrorAsync(ex, "159 --Method-- ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _ = LogErrorAsync(ex, "165 --Method-- ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                }
                                finally
                                {
                                    connection.Close();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {

                        _ = LogErrorAsync(ex, "176 --Method-- ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "182 --Method-- ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
            }
        }
        /// <summary>
        /// Finish newUpdate File With Device
        /// </summary>
        /// <param name="stateobject"></param>
        /// <param name="paramArray"></param>
        /// <returns></returns>
        private static async Task ProcessUPRRecievedContent(StateObject stateobject, string[] paramArray)
        {
            try
            {
                int.TryParse(paramArray[1].Split(':')[1], out int VID);
                if (VID > 0) //Version Id Check
                {
                    try
                    {
                        if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateobject.IMEI1))
                        {
                            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
                            {
                                string VIKey = "BgrUEy5IbpJSnhmqI2IhKw==";
                                try
                                {
                                    string sql = $"INSERT MachineVersionDetail (VersionId,State,CreateDate,Sender,Reciever) VALUES" +
                                        $"(@VersionId,@State,@CreateDate,@Sender,@Reciever)";
                                    using (SqlCommand command = new SqlCommand(sql, connection))
                                    {
                                        command.CommandTimeout = 100000;
                                        command.CommandType = CommandType.Text;
                                        command.Parameters.AddWithValue("@VersionId", VID);
                                        command.Parameters.AddWithValue("@State", "UPR");
                                        command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                        command.Parameters.AddWithValue("@Sender", stateobject.IMEI1);
                                        command.Parameters.AddWithValue("@Reciever", "Server");
                                        connection.Open();
                                        await command.ExecuteScalarAsync().ConfigureAwait(false);
                                        if (AsynchronousSocketListener.Imei1Log == "All" || stateobject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"*************************************");
                                            Console.WriteLine($"Get UPR Device by IMEI1={stateobject.IMEI1} Ip={stateobject.IP}");
                                            Console.WriteLine($"*************************************");
                                            Console.ForegroundColor = ConsoleColor.Green;
                                        }
                                    }

                                }
                                catch (Exception ex)
                                {
                                    _ = LogErrorAsync(ex, "225 -Method-- ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                }
                                finally
                                {
                                    connection.Close();
                                }
                                if (CheckFileSizeAndFileName(VID, paramArray[2].ToString()).Result)
                                {
                                    //Send msg To Device for OK fileDownload

                                    var content = ("RPL#\"VID\":" + VID + "#").Encrypt("sample_shared_secret", VIKey);
                                    try
                                    {
                                        var sql = $"INSERT MachineVersionDetail (VersionId,State,CreateDate,Sender,Reciever) VALUES" +
                                                       $"(@VID,@State,@CreateDate,@Sender,@Reciever)";
                                        using (SqlCommand command = new SqlCommand(sql, connection))
                                        {
                                            command.CommandTimeout = 100000;
                                            command.CommandType = CommandType.Text;
                                            command.Parameters.AddWithValue("@VID", VID);
                                            command.Parameters.AddWithValue("@State", "RPL");
                                            command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                            command.Parameters.AddWithValue("@Sender", "Server");
                                            command.Parameters.AddWithValue("@Reciever", stateobject.IMEI1);
                                            connection.Open();
                                            //if (AsynchronousSocketListener.SocketConnected(stateobject))
                                            //{
                                            await command.ExecuteScalarAsync().ConfigureAwait(false);
                                            AsynchronousSocketListener.Send(stateobject.workSocket, VIKey + " ," + content);
                                            if (AsynchronousSocketListener.Imei1Log == "All" || stateobject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Yellow;
                                                Console.WriteLine($"*************************************");
                                                Console.WriteLine($"Send RPL server To  IMEI1 ={stateobject.IMEI1} IP={stateobject.IP}");
                                                Console.WriteLine($"*************************************");
                                                Console.ForegroundColor = ConsoleColor.Green;
                                            }
                                            //}
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "262 -Method-- ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        connection.Close();
                                    }
                                }
                                else //Download file Nok
                                {
                                    var content = ("FSE#\"VID\":" + VID + "#").Encrypt("sample_shared_secret", VIKey);
                                    try
                                    {
                                        var sql = $"INSERT MachineVersionDetail (VersionId,State,CreateDate,Sender,Reciever) VALUES" +
                                                        $"(@VID,@State,@CreateDate,@Sender,@Reciever)";
                                        using (SqlCommand command = new SqlCommand(sql, connection))
                                        {
                                            command.CommandTimeout = 100000;
                                            command.CommandType = CommandType.Text;
                                            command.Parameters.AddWithValue("@VID", VID);
                                            command.Parameters.AddWithValue("@State", "FSE");
                                            command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                            command.Parameters.AddWithValue("@Sender", "Server");
                                            command.Parameters.AddWithValue("@Reciever", stateobject.IMEI1);
                                            connection.Open();
                                            //if (AsynchronousSocketListener.SocketConnected(stateobject))
                                            //{
                                            await command.ExecuteScalarAsync().ConfigureAwait(false);
                                            AsynchronousSocketListener.Send(stateobject.workSocket, VIKey + " ," + content);
                                            if (AsynchronousSocketListener.Imei1Log == "All"  || stateobject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Yellow;
                                                Console.WriteLine($"*************************************");
                                                Console.WriteLine($"Send FSE server To  IMEI1={stateobject.IMEI1} Ip ={stateobject.IP}");
                                                Console.WriteLine("File Size has Error");
                                                Console.WriteLine($"*************************************");
                                                Console.ForegroundColor = ConsoleColor.Green;
                                            }
                                            //}
                                        }

                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "303 -- Method -- ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        connection.Close();
                                    }
                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        _ = LogErrorAsync(ex, "316 -- Method -- ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "322 -Method-- ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
            }
        }
        /// <summary>
        /// Check Size Client with File Size on Server
        /// </summary>
        /// <param name="VersionId">VersionId</param>        
        /// <param name="FileSize">Filesize should be Convert to byte</param>
        /// <returns></returns>
        private static async Task<bool> CheckFileSizeAndFileName(int VersionId, string FileSize)
        {
            long.TryParse(FileSize.Split(':')[1], out long sFileDownload);
            bool result = false;
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                try
                {
                    string sql = $"SELECT FileDownloadAddress   from MachineVersion where Id = @Id ";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@Id", VersionId);
                        connection.Open();
                        var SelectedVersion = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                        byte[] fileByte = await DownloadFile(SelectedVersion);
                        if (fileByte != null)
                        {
                            if (fileByte.Length == sFileDownload)
                            {
                                result = true;
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "362 -Method-- CheckFileSizeAndFileName", FileSize.ToString());
                }
                finally
                {
                    connection.Close();
                }
            }
            return result;
        }
        /// <summary>
        /// Download File from Server
        /// </summary>
        /// <param name="url">Must be Full Url ,exp. Http://185.192.112.74/share/config.zip </param>
        /// <returns></returns>
        public static async Task<byte[]> DownloadFile(string url)
        {
            using (var client = new HttpClient())
            {

                using (var result = await client.GetAsync(url))
                {
                    if (result.IsSuccessStatusCode)
                    {
                        return await result.Content.ReadAsByteArrayAsync();
                    }
                }
            }
            return null;
        }
        /// <summary>
        /// Update Get Message
        /// </summary>
        /// <param name="stateobject"></param>
        /// <param name="paramArray"></param>
        /// <returns></returns>
        private static async Task ProcessUPGRecievedContent(StateObject stateobject, string[] paramArray)
        {
            try
            {
                int.TryParse(paramArray[1].Split(':')[1], out int VID);
                if (VID > 0) //Version Id Check
                {
                    try
                    {
                        if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateobject.IMEI1))
                        {
                            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
                            {
                                try
                                {
                                    string sql = $"INSERT MachineVersionDetail (VersionId,State,CreateDate,Sender,Reciever) VALUES" +
                                        $"(@VersionId,@State,@CreateDate,@Sender,@Reciever)";
                                    using (SqlCommand command = new SqlCommand(sql, connection))
                                    {
                                        command.CommandTimeout = 100000;
                                        command.CommandType = CommandType.Text;
                                        command.Parameters.AddWithValue("@VersionId", VID);
                                        command.Parameters.AddWithValue("@State", "UPG");
                                        command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                        command.Parameters.AddWithValue("@Sender", stateobject.IMEI1);
                                        command.Parameters.AddWithValue("@Reciever", "Server");
                                        connection.Open();
                                        await command.ExecuteScalarAsync().ConfigureAwait(false);
                                        if (AsynchronousSocketListener.Imei1Log == "All"  ||stateobject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"*************************************");
                                            Console.WriteLine($"Get UPG Device by IMEI1={stateobject.IMEI1} Ip={stateobject.IP}");
                                            Console.WriteLine($"*************************************");
                                            Console.ForegroundColor = ConsoleColor.Green;
                                        }
                                    }
                                }

                                catch (Exception ex)
                                {
                                    _ = LogErrorAsync(ex, "430 -Method-- ProcessUPGRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                }
                                finally
                                {
                                    connection.Close();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _ = LogErrorAsync(ex, "412 -Method-- ProcessUPGRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "407 -Method-- ProcessUPGRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
            }
        }
        /// <summary>
        /// This Method Get TSC Command And Analyze Parameter
        /// </summary>
        /// <param name="state">device</param>
        /// <param name="paramArray">parameter</param>
        /// <returns></returns>
        private static async Task ProcessTSCRecievedContent(StateObject state, string[] paramArray)
        {
            try
            {
                if (paramArray.Length == 3)
                {
                    await UpdateMachineTestStatusToRunning(state, paramArray[1].Split(':')[1]).ConfigureAwait(false);
                }
                if (paramArray.Length > 3)
                {
                    if(!string.IsNullOrEmpty(Array.Find(paramArray, element => element.Contains("TerminateTest:TRUE")))) //finish Time for abort test
                    {
                        await UpdateMachineTestStatusToFinish(state, paramArray[1].Split(':')[1]).ConfigureAwait(false);
                    }
                    if (!string.IsNullOrEmpty(Array.Find(paramArray, element => element.Contains("EndTest:TRUE")))) //finish Time for Normal test 
                        //if (paramArray[4].Contains("EndTest:TRUE")) //finishTime for Normal finish test
                    {
                        await UpdateMachineTestStatusToFinish(state, paramArray[1].Split(':')[1]).ConfigureAwait(false);
                    }
                    else
                    {
                        string createDate = string.Empty;
                        bool cnt = false;
                        int TestId = 0; //omid added --981121                        
                        string inseretStatment = "insert into TestResult(";
                        string valueStatmenet = "Values(";
                        foreach (var param in paramArray)
                        {
                            string[] t = ProcessTSCParams(param);
                            if (t != null)
                            {
                                if (t.Length == 2)
                                {
                                    if (!t[0].Contains("TestId"))
                                    {
                                        int i;
                                        if (t[0] == "GPS")
                                        {
                                            double lon = 0;
                                            double lat = 0;
                                            //omid updated--98-11-28 , for nan value GPS
                                            if (!string.IsNullOrEmpty(t[1]) && t[1].ToLower() != "nan")
                                            {                                                
                                                //update machine location-- omid 99-01-04
                                                //10 Testresult will come together but only 1 Update Machine State Insert
                                                //if (!cnt)  //Dr.vahidPout 990230- all TestResult Update Machine State //990612--update machine location
                                                //{ 
                                                //add DateFromDevice 99-06-19.  
                                                //DateTime.TryParse(paramArray[3].Substring(5, paramArray[3].Length - 5), out DateTime fromDevice);
                                                //Util.UpdateMachineLocation(state.IMEI1, state.IMEI2, lat.ToString(), lon.ToString(), Speed, fromDevice).ConfigureAwait(false);
                                                //   cnt = true;
                                                //}
                                                //add gps new format 990728
                                                if (t[1].Split(',') !=null)
                                                {
                                                    
                                                    var gpsData = t[1].Split(',');
                                                    //var timeFromGPGGA = gpsData[1];
                                                    var latFromGPGGA = gpsData[2];
                                                    var lonFromGPGGA = gpsData[4];
                                                    var altitudeFRomGPGGA = gpsData[9];

                                                    //var timeFromGPRMC = gpsData[17];
                                                    //var A_FromGPRMC = gpsData[18];
                                                    //var latFromGPRMC = gpsData[19];
                                                    //var lonFromGPRMC = gpsData[21];
                                                    //var dateFromGPRMC = gpsData[25];
                                                    var speedKnotFromGPVTG = gpsData[36];
                                                    var speedKiloFromGPVTG = gpsData[38];

                                                    lat = int.Parse(latFromGPGGA.Substring(0, 2)) +
                                                        double.Parse(latFromGPGGA.Substring(2, 6)) / 60;
                                                    lon = int.Parse(lonFromGPGGA.Substring(0, 3)) +
                                                        double.Parse(lonFromGPGGA.Substring(3, 6)) / 60;
                                                    double.TryParse(speedKiloFromGPVTG, out double Speed);
                                                    double.TryParse(speedKnotFromGPVTG, out double Speed2);
                                                    double.TryParse(altitudeFRomGPGGA, out double Altitude);

                                                    inseretStatment = inseretStatment + " Lat , Long, ";
                                                    valueStatmenet = valueStatmenet + lat + " , " + lon.ToString() + " , ";

                                                    DateTime.TryParse(paramArray[3].Substring(5, paramArray[3].Length - 5), out DateTime fromDevice);
                                                    Util.UpdateMachineLocation(state.IMEI1, state.IMEI2, lat.ToString(), lon.ToString(),
                                                        Speed, Speed2, Altitude, fromDevice).ConfigureAwait(false);
                                                }
                                            }
                                        }
                                        else if (t[0].Equals("Ping"))
                                        {
                                            var pingResult = t[1].Split(',');
                                            inseretStatment = inseretStatment + " NumOfPacketSent , NumOfPacketReceived, NumOfPacketLost, Ping, Rtt, MinRtt,AvgRtt,MaxRtt,mdev,  ";
                                            valueStatmenet = valueStatmenet + pingResult[0].Split(' ')[0] + " , " + pingResult[1].Split(' ')[1] + " , " + pingResult[2].Split('%')[0].Split(' ')[1] + " , 'Ping' ," +
                                                pingResult[3].Split('=')[0].Split(' ')[2].Split('m')[0] + " , " + pingResult[3].Split('=')[1].Split('/')[0] + " , " +
                                                pingResult[3].Split('=')[1].Split('/')[1] + " , " + pingResult[3].Split('=')[1].Split('/')[2] + " , " + pingResult[3].Split('=')[1].Split('/')[3].Split(' ')[0] + " ,";
                                        }
                                        else if (t[0].Equals("TraceRoute"))
                                        {
                                            inseretStatment = inseretStatment + "TraceRoute,hop1,hop1_rtt,hop2,hop2_rtt,hop3,hop3_rtt,hop4,hop4_rtt,hop5,hop5_rtt,hop6,hop6_rtt,hop7,hop7_rtt,hop8,hop8_rtt,hop9,hop9_rtt,hop10,hop10_rtt, ";

                                            var traceRtResponse = t[1].Split(',');
                                            var des = traceRtResponse[0];//destination
                                            valueStatmenet = valueStatmenet + $"'{des}' , ";

                                            //var hops = traceRtResponse[2].Split(new[] { "\n", "\r", "\n\r" }, StringSplitOptions.None);
                                            for (int j = 1; j <= 10; j++)
                                            {
                                                var Jindex = traceRtResponse[2].IndexOf($" {j} ");
                                                var Xindex = traceRtResponse[2].IndexOf($" {j + 1} ");
                                                string[] Jhop;
                                                if (Xindex == -1) //end of hops
                                                {
                                                    Jhop = traceRtResponse[2].Substring(Jindex).Split(' ');
                                                }
                                                else
                                                {
                                                    Jhop = traceRtResponse[2].Substring(Jindex, Xindex - Jindex).Split(' ');
                                                }
                                                if (Jhop[3] == "*")   //hop rtt
                                                {
                                                    valueStatmenet += "'" + Jhop[3] + "', " + System.Data.SqlTypes.SqlDouble.Null + " , ";
                                                }
                                                else
                                                {
                                                    valueStatmenet += "'" + Jhop[3] + "', " + Convert.ToDouble(Jhop[6]) + " , ";
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (t[0] == "CreateDate")
                                            {
                                                createDate = t[1].ToString();
                                            }

                                            inseretStatment = inseretStatment + t[0] + " ,";
                                            valueStatmenet = valueStatmenet + (int.TryParse(t[1].Replace("dBm", ""), out i) || t[1].Contains("0x") ? i.ToString() : "'" + t[1].Replace("dBm", "") + "'") + " ,";
                                        }

                                    }
                                    else
                                    {

                                        if (t[1].Contains("-"))
                                        {
                                            inseretStatment = inseretStatment + t[0] + " , IsGroup, ";
                                            valueStatmenet = valueStatmenet + t[1].Replace("-", "") + " , 1 , ";

                                            //int.TryParse(t[1].Replace("-", ""), out TestId); ////omid added --981121
                                        }
                                        else
                                        {
                                            inseretStatment = inseretStatment + t[0] + " , IsGroup, ";
                                            valueStatmenet = valueStatmenet + t[1] + " , 0 , ";

                                            int.TryParse(t[1], out TestId);//omid added --981121
                                        }

                                    }
                                }
                                else if (t.Length == 4)
                                {
                                    int.TryParse(t[1], out int mmc);
                                    int.TryParse(t[3], out int mnc);
                                    inseretStatment = inseretStatment + "MCC ,MNC,";
                                    valueStatmenet = valueStatmenet + mmc + " , " + mnc.ToString() + " , ";
                                }
                            }
                        }
                        var ExtraParam = await _GetParameterbyTestReultId(TestId); // omid added -- 98 11 21
                        await InsertTestResult(inseretStatment + " CreateDateFa, MachineId, MachineName, DefinedTestId, DefinedTestName,SelectedSim,BeginDateTest,EndDateTest ) " + valueStatmenet +
                                             $" cast([dbo].[CalculatePersianDate]('{createDate}')as nvarchar(max)) + N' '+cast(convert(time(0),'{createDate.Split(' ')[1]}') as nvarchar(max)),"
                                                                                + ExtraParam[1] + ", '" + ExtraParam[5] + "' ," + ExtraParam[0] + ",'" + ExtraParam[6] + "'," + ExtraParam[2] + ", '" + ExtraParam[3] + "' , '" + ExtraParam[4] + "' )", state);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "593 --Method-- ProcessTSCRecievedContent", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
            }
        }
        private static async Task InsertTestResult(string testResult, StateObject state)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
                {
                    string sql = testResult;
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        connection.Open();
                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "615 --Method-- insertTestResult", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
            }
        }
        /// <summary>
        /// فراخوانی پارامترهای موردنیاز در گزارش
        /// omidAdd981121
        /// </summary>
        /// <param name="TestId">DefinedTestMachineId</param>
        /// <returns></returns>
        private static async Task<string[]> _GetParameterbyTestReultId(int TestId)
        {
            var res = new string[7];
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                try
                {
                    string sql = $"select dtm.DefinedTestId , dtm.MachineId ,dtm.SIM ,dtm.BeginDate , dtm.EndDate , m.Name, dt.Title" +
                                 $" from DefinedTestMachine as dtm" +
                                 $" left join Machine as m on dtm.MachineId = m.Id" +
                                 $" left join DefinedTest as dt on dtm.DefinedTestId = dt.id" +
                                 $" where dtm.id = {TestId}";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        connection.Open();
                        var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                        if (reader.Read())
                        {
                            //reader.Read();
                            //Console.WriteLine(reader);
                            res[0] = reader["DefinedTestId"].ToString();
                            res[1] = reader["MachineId"].ToString();
                            res[2] = reader["SIM"].ToString();
                            res[3] = reader["BeginDate"].ToString();
                            res[4] = reader["EndDate"].ToString();
                            res[5] = reader["Name"].ToString();//machine name
                            res[6] = reader["Title"].ToString(); //Test name
                        }
                        reader.Close();
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "658 - Method-- _GetParameterbyTestReultId").ConfigureAwait(false);
                }
                finally
                {
                    connection.Close();
                }
            }
            return res;
        }

        private static string[] ProcessTSCParams(string param)
        {
            if (param.Contains(":"))
            {
                float tmpVal;
                switch (param.Split(":")[0])
                {
                    case "Id":
                        return new string[] { "TestId", param.Split(":")[1] };
                    case "BER":
                        int ber; int.TryParse(param.Split(":")[1], out ber);
                        if (ber >= 99) //مقدار غیر صحیح
                            return new string[] { "BER", DBNull.Value.ToString() };
                        else
                            return new string[] { "BER", param.Split(":")[1] };
                    case "PID":
                        return new string[] { "PID", param.Split(":")[1] };
                    case "MCC-MNC":
                        var vals = param.Split(":")[1].Split("-");
                        return new string[] { "MCC", vals[0], "MNC", vals[1] };
                    case "MCC":
                        return new string[] { "MCC", param.Split(":")[1] };
                    case "MNC":
                        return new string[] { "MNC", param.Split(":")[1] };
                    case "BSIC":
                        return new string[] { "BSIC", param.Split(":")[1] };
                    case "FrequencyBand": //omid updated
                        return new string[] { "FregBand", param.Split(":")[1] };
                     //انجام شد ولی ماند برای بعد از ساماندهی دیتابیس
                    ////ToDo:omid:add OpMode 990629
                    //case "OPMode":
                    //    return new string[] { "OPMode", param.Split(":")[1] };
                    ////ToDo:omid: add SID 990629
                    //case "SID": 
                    //    return new string[] { "SID", param.Split(":")[1] };
                    ////ToDo:omid: add TechNo 990629
                    //case "TechNo":
                    //    return new string[] { "TechNo", param.Split(":")[1] };
                    case "CID":
                        int cidValue; int.TryParse(param.Split(":")[1], out cidValue);
                        if (cidValue == -1) //مقدار غیر صحیح
                            return new string[] { "CID", DBNull.Value.ToString() };
                        else
                            return new string[] { "CID", param.Split(":")[1] };
                    case "UARFCN":
                        return new string[] { "UARFCN", param.Split(":")[1] };
                    case "ARFCN":
                        return new string[] { "ARFCN", param.Split(":")[1] };
                    case "DLBW":
                        return new string[] { "DLBW", param.Split(":")[1] };
                    case "LAC":
                        int resL = 0;
                        if ((param.Split(":")[1]).Contains("0x")) //اگر هگزاست تبدیل شود به دسیمال
                        {
                            resL = Convert.ToInt32(param.Split(":")[1], 16);
                        }
                        else
                        {
                            int.TryParse(param.Split(":")[1], out resL);
                        }
                        if (resL == 0) //مقدار غیر صحیح
                            return new string[] { "LAC", DBNull.Value.ToString() };
                        return new string[] { "LAC", resL.ToString() };
                    case "ULBW":
                        return new string[] { "ULBW", param.Split(":")[1] };
                    case "BCCH":
                        return new string[] { "BCCH", param.Split(":")[1] };
                    case "RSSNR":
                        return new string[] { "RSSNR", param.Split(":")[1] };
                    case "TA":
                        return new string[] { "TA", param.Split(":")[1] };
                    case "PSC":
                        return new string[] { "PSC", param.Split(":")[1] };
                    case "EARFCN":
                        return new string[] { "EARFCN", param.Split(":")[1] };
                    case "TXPWR":
                        return new string[] { "TXPower", param.Split(":")[1] };
                    case "SSC":
                        return new string[] { "SSC", param.Split(":")[1] };
                    case "TAC":
                        int res = 0;
                        if ((param.Split(":")[1]).Contains("0x")) //اگر هگزاست تبدیل شود به دسیمال
                        {
                            res = Convert.ToInt32(param.Split(":")[1], 16);
                        }
                        else
                        {
                            int.TryParse(param.Split(":")[1], out res);
                        }
                        return new string[] { "TAC", res.ToString() };
                    case "RXLEV":
                        return new string[] { "RXLevel", param.Split(":")[1] };
                    case "ECIO":
                        float.TryParse((param.Split(":")[1]).ToString(), out tmpVal); tmpVal *= -1; //addedby-omid-981227
                        return new string[] { "ECIO", tmpVal.ToString() };
                    case "RSRQ":
                        float.TryParse(param.Split(":")[1], out tmpVal); tmpVal /= 10; //addedby-omid-981227
                        return new string[] { "RSRQ", tmpVal.ToString() };
                    case "RSCP":
                        float.TryParse(param.Split(":")[1], out tmpVal); tmpVal *= -1;//addeddby-omid-981227
                        return new string[] { "RSCP", tmpVal.ToString() };
                    case "RSRP":
                        float.TryParse(param.Split(":")[1], out tmpVal); tmpVal /= 10;//addeddby-omid-981227
                        return new string[] { "RSRP", tmpVal.ToString() };
                    case "RSSI":
                        float.TryParse(param.Split(":")[1], out tmpVal); tmpVal /= 10;//addeddby-omid-981229
                        return new string[] { "RSSI", tmpVal.ToString() };
                    case "OVFSF":
                        return new string[] { "OVFSF", param.Split(":")[1] }; //omid Edit and update
                    case "RXEQUAL":
                        return new string[] { "RXQual", param.Split(":")[1] };
                    case "SYSMODE":  //1:2G , 4:3G, 8:4G
                        return new string[] { "SystemMode", param.Split(":")[1] };
                    case "PingResault":
                        return new string[] { "Ping", param.Split(":")[1] };
                    case "OPERATOR":
                        return new string[] { "Operator", param.Split(":")[1] };//addeddby-omid-981229
                    case "Traceroute":
                        return new string[] { "TraceRoute", param.Split(":")[1] };
                    case "TIME":
                        return new string[] { "CreateDate", param.Split(":")[1] + ":" + param.Split(":")[2] + ":" + param.Split(":")[3] };
                    case "GPS":
                        return new string[] { "GPS", param.Split(":")[1] };
                    case "Layer3":
                        return new string[] { "Layer3Messages", param.Split(":")[1] };
                    case "SPEED": //HTTP-FTP-Downlink/Uplink  --during action--addedby omid 990107
                        double spd = 0;
                        double.TryParse(param.Split(":")[1], out spd);
                        return new string[] { "Speed", spd.ToString() };
                    case "ElapsedTime": //HTTP-FTP-Downlink/Uplink --compelete Action --addedby omid 990107
                        double ept = 0;
                        double.TryParse(param.Split(":")[1], out ept);
                        return new string[] { "ElapsedTime", ept.ToString() };
                    case "AvrgSpeed": //HTTP-FTP-Downlink/Uplink  --compelete Action--addedby omid 990107
                        double asp = 0;
                        double.TryParse(param.Split(":")[1], out asp);
                        return new string[] { "AvrgSpeed", asp.ToString() };
                    case "FileName": //MosCall Params , Name Of wav file                     
                        String FileName = param.Split(":")[1];
                        var ar = FileName.Split('/');
                        if (!string.IsNullOrEmpty(serverPath))
                        {
                            return new string[] { "FileName", serverPath + "/" + ar[ar.Length - 1] };
                        }
                        return new string[] { "FileName", ar[ar.Length - 1] };
                    case "FileNameL3": //Layer3" ,Name Of txt file                        
                        var arr = param.Split(":")[1].Split('/');
                        if (!string.IsNullOrEmpty(serverPath))
                        {
                            return new string[] { "FileName", serverPath + "/" + arr[arr.Length - 1] };
                        }
                        return new string[] { "FileName", arr[arr.Length - 1] };
                    case "FileSize": //MosCall Params , Size Of wav file 
                    case "FileSizeL3"://layer3 , Size of txt file
                        int FileSize = 0; int.TryParse(param.Split(":")[1], out FileSize);
                        return new string[] { "FileSize", FileSize.ToString() };
                    case "mosFile": //MosCall Params , Path oF File 
                    case "ServerL3"://layer3 params, Path of File
                        serverPath = param.Split(":")[1];
                        break;
                    default:
                        break;
                }
            }
            return null;
        }
        private static async Task UpdateMachineTestStatusToFinish(StateObject state, string testId)
        {
            AsynchronousSocketListener.SendedTest.Add(testId);
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                if (!testId.Contains("-"))//is not group
                {
                    //ToDo: پس از انجام تست و اتمام آن و دریافت پاسخ از سمت دستگاه وضعیت تست را به 2 تغییر بدهیم.990625

                    string sql = $"update DefinedTestMachine set status = 2, FinishTime = getdate() where id = @testId";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@testId", testId);
                        try
                        {
                            connection.Open();
                            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, "814 -- Method-- UpdateMachineTestStatusToFinish", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    string sql = $"update DefinedTestMachineGroup set status = 1, FinishTime = getdate() where id = @testId";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@testId", testId.Replace("-", ""));
                        try
                        {
                            connection.Open();
                            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, " 833 -- Method-- UpdateMachineTestStatusToFinish", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                        }
                    }
                }
            }
        }
        private static async Task UpdateMachineTestStatusToRunning(StateObject state, string testId)
        {
            try
            {
                AsynchronousSocketListener.SendedTest.Add(testId);
                using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
                {
                    if (!testId.Contains("-"))
                    {
                        string sql = $"update DefinedTestMachine set status = 1 where id = @testId";
                        using (SqlCommand command = new SqlCommand(sql, connection))
                        {
                            command.CommandTimeout = 100000;
                            command.CommandType = CommandType.Text;
                            command.Parameters.AddWithValue("@testId", testId);
                            try
                            {
                                connection.Open();
                                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                                UpdatePreviousTestFinishTime(state, testId).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _ = LogErrorAsync(ex, "861 -- Method-- UpdateMachineTestStatusToRunning", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        string sql = $"update DefinedTestMachineGroup set status = 1 where id = @testId";
                        using (SqlCommand command = new SqlCommand(sql, connection))
                        {
                            command.CommandTimeout = 100000;
                            command.CommandType = CommandType.Text;
                            command.Parameters.AddWithValue("@testId", testId.Replace("-", ""));
                            try
                            {
                                connection.Open();
                                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _ = LogErrorAsync(ex, "881 -- Method-- UpdateMachineTestStatusToRunning", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

                await LogErrorAsync(ex, "889 -- Method-- UpdateMachineTestStatusToRunning", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
            }
        }
        /// <summary>
        /// After Assign New Test To Device and Device Get it
        /// if device has running Test , killed its and terminate and run new Test.
        /// server after Recieved  Massage from Device (means Device get new Test )Should be set FinishTime for Previouses
        /// Test that Terminated by Device
        /// </summary>
        /// <param name="state">Socket</param>
        /// <param name="TestId">New Test Assing To Device</param>
        private static async Task UpdatePreviousTestFinishTime(StateObject state, string TestId)
        {
            //ToDo:omid: پس از تعریف تست جدید برای دستگاه درصورت هم پوشانی زمانی با تست از قبل تعریف شده و درحال اجرا نیاز است که تست قبلی بروزرشانس شود:990626
            string sql = $"declare @bdate datetime,@enddate datetime,@machineId int;" +
               $" begin" +
               $" select @bdate = BeginDate , @enddate = EndDate , @machineId=MachineId from DefinedTestMachine  where id =@Id; " +
                  $" update DefinedTestMachine " +
                        $" set Status = 1, FinishTime = @bdate " +
                        $" where status = 1 and MachineId = @machineId and FinishTime is null " +
                         $" and (EndDate >= @bdate and BeginDate <= @enddate) and id<> @Id   end";
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 100000;
                    command.CommandType = CommandType.Text;
                    command.Parameters.AddWithValue("@Id", TestId);
                    try
                    {
                        connection.Open();
                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _ = LogErrorAsync(ex, "861 -- Method-- UpdatePreviousTestFinishTime", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                    }
                }
            }
        }
        private static async Task ProcessMIDRecievedContent(StateObject state, string[] paramArray)
        {
            try
            {
                state.IMEI1 = paramArray[1].Split(':')[1];
                state.IMEI2 = paramArray[2].Split(':')[1];
                if (paramArray.Length >= 3) //addby omid 98-12-02
                {
                    //try
                    //{
                    //    if (paramArray[3].Split(':')[1] != ",,,,,,,," )
                    //    {
                    //        if ((paramArray[3].Split(':')[1] != "" || paramArray[3].Split(':')[1] != null || paramArray[3].Split(':')[1] != " ") && (paramArray[3].Split(':')[1] != "NaN" || paramArray[3].Split(':')[1] != "NAN"))
                    //        {
                    //            lat = int.Parse(paramArray[3].Split(':')[1].Split(",")[0].Substring(0, 2)) + double.Parse(paramArray[3].Split(':')[1].Split(",")[0].Substring(2, 6)) / 60;
                    //            lon = int.Parse(paramArray[3].Split(':')[1].Split(",")[2].Substring(0, 3)) + double.Parse(paramArray[3].Split(':')[1].Split(",")[2].Substring(3, 6)) / 60;
                    //        }
                    //    }
                    //}
                    //catch (Exception ex)
                    //{

                    //     _=LogErrorAsync(ex, "912 -- Method -- ProcessMIDRecievedContent", state.IMEI1).ConfigureAwait(false);
                    //}

                    if (!AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == state.IMEI1))
                    {
                        lock (AsynchronousSocketListener.LockDev)
                        {
                            AsynchronousSocketListener.DeviceList.Add(state);
                        }
                        state.Timer.AutoReset = true;
                        state.lastDateTimeConnected = DateTime.Now;
                        state.Timer.Start();
                    }
                    else
                    {
                        StateObject dd = AsynchronousSocketListener.DeviceList.Find(x => x.IMEI1 == state.IMEI1);
                        dd.Timer.Stop();
                        //dd.workSocket.Shutdown(System.Net.Sockets.SocketShutdown.Both);
                        lock (AsynchronousSocketListener.LockDev)
                        {
                            AsynchronousSocketListener.DeviceList.Remove(dd);

                            AsynchronousSocketListener.DeviceList.Add(state);
                        }
                        state.Timer.AutoReset = true;
                        state.lastDateTimeConnected = DateTime.Now;
                        state.Timer.Start();
                    }
                    //first time , register device
                    _ = Util.UpdateMachineState(state.IMEI1, state.IMEI2, true);
                    if (AsynchronousSocketListener.Imei1Log =="All" || state.IMEI1 == AsynchronousSocketListener.Imei1Log)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"*************************************");
                        Console.WriteLine($"Get MID FRom IMEI1={ state.IMEI1} IP={state.IP}");
                        Console.WriteLine($"*************************************");
                        Console.ForegroundColor = ConsoleColor.Green;    
                    }
                   // _ = Util.SyncDevice(state);
                }

            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "932 -- Method-- ProcessMIDRecievedContent", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
            }
        }
        private static string[] _CaptchaList = new string[100];
        static string SaltKey = "sample_salt";
        public static string Encrypt(this string plainText, string passwordHash, string VIKey)
        {
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);

            byte[] keyBytes = new Rfc2898DeriveBytes(passwordHash, Encoding.ASCII.GetBytes(SaltKey), 1024).GetBytes(16);
            var symmetricKey = new RijndaelManaged() { Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 };
            var encryptor = symmetricKey.CreateEncryptor(keyBytes, Convert.FromBase64String(VIKey));

            byte[] cipherTextBytes;

            using (var memoryStream = new MemoryStream())
            {
                using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                {
                    cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                    cryptoStream.FlushFinalBlock();
                    cipherTextBytes = memoryStream.ToArray();
                    cryptoStream.Close();
                }
                memoryStream.Close();
            }
            return Convert.ToBase64String(cipherTextBytes);
        }
        internal static async Task SendWaitingGroupTest(StateObject stateObject)
        {
            string definedTest = "";
            string VIKey = "BgrUEy5IbpJSnhmqI2IhKw==";
            using (SqlConnection connection = new SqlConnection(ConnectionStrings))
            {
                try
                {
                    string sql = $"SELECT -1 * DTMG.Id Id, dt.Title, dt.Layer3Messages, case when dt.Layer3Messages =1 then '185.192.112.74/Uploads/L3Files' end ServerUrlL3, dt.RepeatTypeId, dt.RepeatTime, dt.RepeatCount, dt.MeasurementInterval, dt.TestTypeId, dt.UsualCallDuration, " +
                        $" dt.UsualCallWaitTime, dt.UsualCallNumber, dt.TestDataId, dt.TestDataTypeId, replace(replace(replace(case when(dt.TestDataDownloadFileAddress is null or dt.TestDataDownloadFileAddress = N'')then " +
                        $" dt.TestDataServer else dt.TestDataServer + N'/' + dt.TestDataDownloadFileAddress end ,N'//',N'/'),N'https:/',N''),N'http:/',N'') as TestDataServer, dt.TestDataUserName, dt.TestDataPassword , dt.TestDataUploadFileSize as FileSize, " +
                        $" dt.IPTypeId, dt.OTTServiceId, dt.OTTServiceTestId, dt.NetworkId, dt.BandId , dt.SaveLogFile, dt.LogFilePartitionTypeId, dt.LogFilePartitionTime, " +
                        $" dt.LogFilePartitionSize, dt.LogFileHoldTime, dt.NumberOfPings, dt.PacketSize, dt.InternalTime, dt.ResponseWaitTime, dt.TTL,replace(CONVERT(varchar(26), DTMG.BeginDate, 121), " +
                        $" N':', N'-') BeginDate, replace(CONVERT(varchar(26), DTMG.EndDate, 121), N':', N'-') EndDate, DTMG.SIM,  " +
                        $"                     case when TesttypeId not in(4, 2) then testtypeid " +
                        $"             when TestTypeId = 2 then '2' + cast(TestDataTypeId as nvarchar(10)) " +
                        $"             when TestTypeId = 4 then '4' + " +
                        $" 				case when testdataid in(3, 4) then cast(TestDataId as nvarchar(10)) " +
                        $"                      else cast(TestDataId as nvarchar(10)) + cast(TestDataTypeId as nvarchar(10)) end end TestType " +
                        $" from MachineGroup MG " +
                        $" join DefinedTestMachineGroup DTMG on MG.Id = DTMG.MachineGroupId " +
                        $" join DefinedTest DT on DTMG.DefinedTestId = DT.id " +
                        $" where DTMG.IsActive = 1 and DTMG.BeginDate > getdate() and " +
                        $" DTMG.IsActive = 1 and DTMG.Status = 0 /*status = 0, not test*/ " +
                        $" and MG.Id = (select MachineGroupId from machine where IMEI1 = @IMEI1  )  for json path ";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IMEI1", stateObject.IMEI1);
                        connection.Open();
                        definedTest = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {

                    _ = LogErrorAsync(ex, "996 -- Method --  SendWaitingGroupTest", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                }
                finally
                {
                    connection.Close();
                }
            }
            if (definedTest != null)
            {
                foreach (var item in JArray.Parse(definedTest))
                {
                    var content = ("TST#" + item.ToString().Replace("}", "").Replace(",", "#").Replace("{", "")).Replace(" ", "").Replace("\r", "").Replace("\n", "").ToString().Encrypt("sample_shared_secret", VIKey);
                    if (AsynchronousSocketListener.SendedTest.Find(t => t.Contains(content)) == null)
                    {

                        AsynchronousSocketListener.Send(stateObject.workSocket, VIKey + " ," + content);
                    }
                }
            }
        }
        internal static async Task SendWaitingTest(StateObject stateObject)
        {
            string definedTest = "";
            string VIKey = "BgrUEy5IbpJSnhmqI2IhKw==";
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                try
                {
                    string sql = $"SELECT DTM.Id Id, dt.Title, dt.Layer3Messages, case when dt.Layer3Messages =1 then '185.192.112.74/Uploads/L3Files' end TestDataServerL3, " +
                        $" dt.RepeatTypeId, dt.RepeatTime, dt.RepeatCount, dt.MeasurementInterval, dt.TestTypeId, dt.UsualCallDuration, " +
                        $"dt.UsualCallWaitTime, dt.UsualCallNumber, dt.TestDataId, dt.TestDataTypeId, replace(replace(replace(case when (dt.TestDataDownloadFileAddress is null or dt.TestDataDownloadFileAddress = N'' )then  " +
                        $"dt.TestDataServer else dt.TestDataServer + N'/' + dt.TestDataDownloadFileAddress end ,N'//',N'/'),N'https:/',N''),N'http:/',N'') as TestDataServer, dt.TestDataUserName, dt.TestDataPassword , dt.TestDataUploadFileSize as FileSize, " +
                        $"dt.IPTypeId, dt.OTTServiceId, dt.OTTServiceTestId, dt.NetworkId, dt.BandId , dt.SaveLogFile, dt.LogFilePartitionTypeId, dt.LogFilePartitionTime, " +
                        $"dt.LogFilePartitionSize, dt.LogFileHoldTime, dt.NumberOfPings, dt.PacketSize, dt.InternalTime, dt.ResponseWaitTime, " +
                        $" dt.TTL,replace(CONVERT(varchar(26),DTM.BeginDate, 121) , " +
                        $"N':',N'-') BeginDate, replace(CONVERT(varchar(26),DTM.EndDate, 121),N':',N'-') EndDate, DTM.SIM,  " +
                        $"                    case when TesttypeId not in(4, 2) then testtypeid " +
                        $"            when TestTypeId = 2 then '2' + cast(TestDataTypeId as nvarchar(10)) " +
                        $"            when TestTypeId = 4 then '4' + " +
                        $"				case when testdataid in(3, 4) then cast(TestDataId as nvarchar(10)) " +
                        $"                     else cast(TestDataId as nvarchar(10)) + cast(TestDataTypeId as nvarchar(10)) end end TestType " +
                        $"from Machine M " +
                        $"join DefinedTestMachine DTM on M.Id = DTM.MachineId " +
                        $"join DefinedTest DT on DTM.DefinedTestId = DT.id " +
                        $"where DTM.IsActive = 1 and DTM.BeginDate > getdate() and " +
                        $"DTM.IsActive = 1 and DTM.Status = 0 " +/*status = 0, not test*/
                        $"and m.IMEI1 = @IMEI1 for json path";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IMEI1", stateObject.IMEI1);
                        connection.Open();
                        definedTest = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1052 --Method-- SendWaitingTest", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                }
                finally
                {
                    connection.Close();
                }
            }
            if (definedTest != null)
            {
                foreach (var item in JArray.Parse(definedTest))
                {
                    var content = ("TST#" + item.ToString().Replace("}", "").Replace(",", "#").Replace("{", "")).Replace(" ", "").Replace("\r", "").Replace("\n", "").ToString().Encrypt("sample_shared_secret", VIKey);
                    //change content by testid --98-12-01
                    if (AsynchronousSocketListener.SendedTest.Find(t => t.Contains(content)) == null)
                    {
                        AsynchronousSocketListener.Send(stateObject.workSocket, VIKey + " ," + content);
                        LogErrorAsync(new Exception("Send Test"),content,string.Join("-",item));
                    }
                }
            }
        }
        internal static async Task UpdateVersion(StateObject stateObject)
        {
            string newUpdate = "";
            string VIKey = "BgrUEy5IbpJSnhmqI2IhKw==";
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                try
                {
                    string sql = $"SELECT r.* " +
                        $" from MachineVersion r " +
                        $" where r.IMEI1 = @IMEI1 and r.IsDone = 0" +
                        $" order by CreateDate Desc " +
                        $" for json path";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IMEI1", stateObject.IMEI1);
                        connection.Open();
                        newUpdate = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1117 -- Method -- UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                }
                finally
                {
                    connection.Close();
                }
            }
            if (newUpdate != null)
            {
                var curReq = JArray.Parse(newUpdate);
                // curReq["SendToDevice"] =>  0 : upd donot send To device
                if (!Convert.ToBoolean(curReq[0]["SendToDevice"].ToString()))
                {
                    string sql = string.Empty;
                    using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
                    {
                        try
                        {
                            //دستور آپدیت دستگاه با آدرس فایل مورد نظردر سرور
                            var content = ("UPD#\"VID\":" + curReq[0]["Id"] + "#\"TestDataServer\":" + "\"" + curReq[0]["FileDownloadAddress"].ToString().Split(':')[1].Substring(2) + "\"")
                                                                   .Encrypt("sample_shared_secret", VIKey);
                            if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateObject.IMEI1))
                            {
                                connection.Open();
                                sql = await Trans_AddVersionDetail(stateObject, VIKey, curReq, connection, sql, content);
                                if (AsynchronousSocketListener.Imei1Log == "All" || stateObject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"*************************************");
                                    Console.WriteLine($"Send UPD Server To IMEI1={ stateObject.IMEI1} IP={stateObject.IP}");
                                    Console.WriteLine($"*************************************");
                                    Console.ForegroundColor = ConsoleColor.Green;
                                }
                                _ = Util.LogErrorAsync(new Exception($"UPD Send To IMEI1=  {stateObject.IMEI1}"), content, $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, "1147 -- Method -- UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                        }
                        finally
                        {
                            connection.Close();
                        }
                    }
                }
                //curReq["SendToDevice"] => 1:upd Send To Device and Device Get upd message ,
                else
                {
                    string ExsitUPD;
                    using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
                    {
                        try
                        {
                            string sql = $"select * from MachineVersionDetail where VersionId = @VersionId and state = N'UPD' " +
                                 $" for json path";
                            using (SqlCommand command = new SqlCommand(sql, connection))
                            {
                                await connection.OpenAsync();
                                command.CommandTimeout = 100000;
                                command.CommandType = CommandType.Text;
                                command.Parameters.AddWithValue("@VersionId", curReq[0]["Id"].ToString());
                                ExsitUPD = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                                if (ExsitUPD == null) //if upd donot Exist, add Upd Recored and Send Updaet Message
                                {
                                    var content = ("UPD#\"VID\":" + curReq[0]["Id"] + "#\"TestDataServer\":" + "\"" + curReq[0]["FileDownloadAddress"].ToString().Split(':')[1].Substring(2) + "\"")
                                                              .Encrypt("sample_shared_secret", VIKey);
                                    try
                                    {
                                        if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateObject.IMEI1))
                                        {
                                            sql = await Trans_AddVersionDetail(stateObject, VIKey, curReq, connection, sql, content);
                                            if (AsynchronousSocketListener.Imei1Log=="All" || stateObject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                            {

                                                Console.ForegroundColor = ConsoleColor.Yellow;
                                                Console.WriteLine($"*************************************");
                                                Console.WriteLine($"Send UPD Server To IMEI1={stateObject.IMEI1} IP={stateObject.IP}");
                                                Console.WriteLine($"*************************************");
                                                Console.ForegroundColor = ConsoleColor.Green;
                                            }
                                            _ = Util.LogErrorAsync(new Exception($"UPD Send To IMEI1=  {stateObject.IMEI1}"), content, $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "1193 -- Method -- UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                    }

                                }
                                else //Server Send UPD Message To Device but Device donot Reply UPG
                                {
                                    object ExsitUPG;
                                    sql = $"select * from MachineVersionDetail where VersionId = @VersionId and state = N'UPG' ";
                                    command.CommandTimeout = 100000;
                                    command.CommandType = CommandType.Text;
                                    command.CommandText = sql;
                                    try
                                    {
                                        ExsitUPG = await command.ExecuteScalarAsync().ConfigureAwait(false);
                                        if (ExsitUPG == null) // را ارسال نکرده باشدUPG کلاینت هنوز پیام 
                                        {
                                            var curUPDRec = JArray.Parse(ExsitUPD);
                                            var UPDCreateDate = Convert.ToDateTime(curUPDRec[0]["CreateDate"]);
                                            // سرور به کلاینت گذشته باشد، اطلاعات دوباره ارسال میشود UPDو دو دقیقه  هم از زمان ارسال پیام 
                                            TimeSpan diffGT2 = DateTime.Now - UPDCreateDate;
                                            if (diffGT2.Seconds > 60)
                                            {
                                                try
                                                {
                                                    int detail_UpdId; int.TryParse(curUPDRec[0]["Id"].ToString(), out detail_UpdId);
                                                    sql = await Trans_DelVersionDetail(curReq, connection, sql, detail_UpdId, stateObject.IMEI1);
                                                    //ارسال دوباره فرمان آپدیت                                        
                                                    //دستور آپدیت دستگاه با آدرس فایل مورد نظردر سرور
                                                    var content = ("UPD#\"VID\":" + curReq[0]["Id"] + "#\"TestDataServer\":" + "\"" + curReq[0]["FileDownloadAddress"].ToString().Split(':')[1].Substring(2) + "\"")
                                                                                                .Encrypt("sample_shared_secret", VIKey);
                                                    try
                                                    {
                                                        if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateObject.IMEI1))
                                                        {
                                                            sql = await Trans_AddVersionDetail(stateObject, VIKey, curReq, connection, sql, content);
                                                            if (AsynchronousSocketListener.Imei1Log == "All" || stateObject.IMEI1 == AsynchronousSocketListener.Imei1Log)
                                                            {
                                                                Console.ForegroundColor = ConsoleColor.Yellow;
                                                                Console.WriteLine($"*************************************");
                                                                Console.WriteLine($"Send UPD Server To IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                                                Console.WriteLine($"*************************************");
                                                                Console.ForegroundColor = ConsoleColor.Green;
                                                            }
                                                            _ = Util.LogErrorAsync(new Exception($"UPD Send To IMEI1={stateObject.IMEI1} IP={stateObject.IP}"), content, $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _ = LogErrorAsync(ex, "1236 -- Method -- UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    _ = LogErrorAsync(ex, "1241 -- Method -- UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "1248 -- Method -- UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, "1255 -- Method -- UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                        }
                        finally
                        {
                            connection.Close();
                        }
                    }
                }
            }
        }
        public static async Task<string> Trans_AddVersionDetail(StateObject stateObject, string VIKey, JToken item, SqlConnection connection, string sql, string content)
        {
            var tx = connection.BeginTransaction();
            try
            {
                sql = $" INSERT MachineVersionDetail (VersionId,State,CreateDate,Sender,Reciever) VALUES" +
                   $" ({item[0]["Id"].ToString()},@State,@CreateDate,@Sender,@Reciever); select SCOPE_IDENTITY()";
                var com2 = new SqlCommand(sql, connection);
                com2.CommandTimeout = 100000;
                com2.Transaction = tx;
                com2.Parameters.AddWithValue("@State", "UPD");
                com2.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                com2.Parameters.AddWithValue("@Sender", "Server");
                com2.Parameters.AddWithValue("@Reciever", stateObject.IMEI1);
                object id = await com2.ExecuteScalarAsync().ConfigureAwait(false);
                //update SendToDevice in  machineVersion Table ==>  mean...Device Get this Update                                                     
                try
                {
                    sql = $" Update MachineVersion Set SendToDevice = 1  where Id = @VersionId";
                    var com3 = new SqlCommand(sql, connection);
                    com3.CommandTimeout = 100000;
                    com3.CommandType = CommandType.Text;
                    com3.Transaction = tx;
                    com3.Parameters.AddWithValue("@VersionId", item[0]["Id"].ToString());
                    id = await com3.ExecuteScalarAsync().ConfigureAwait(false);
                    //if (AsynchronousSocketListener.SocketConnected(stateObject))
                    //{
                    tx.Commit();
                    AsynchronousSocketListener.Send(stateObject.workSocket, VIKey + " ," + content);
                    //}
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1299 -- Method -- Trans_AddVersionDetail", stateObject.IMEI1);
                    tx.Rollback();
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "1309 -- Method -- Trans_AddVersionDetail", stateObject.IMEI1);
                tx.Rollback();
            }
            return sql;
        }
        public static async Task<string> Trans_DelVersionDetail(JToken item, SqlConnection connection, string sql, int VersionDetailId, string IMEI1)
        {
            var tx = connection.BeginTransaction();
            try
            {
                sql = $"Delete from MachineVersionDetail where Id =@Id";
                var com2 = new SqlCommand(sql, connection);
                com2.CommandTimeout = 100000;
                com2.Transaction = tx;
                com2.Parameters.AddWithValue("@Id", VersionDetailId);
                object id = await com2.ExecuteScalarAsync().ConfigureAwait(false);
                try
                {
                    sql = $"Update MachineVersion Set SendToDevice=0 where Id = @VersionId";
                    var com3 = new SqlCommand(sql, connection);
                    com3.CommandTimeout = 100000;
                    com3.CommandType = CommandType.Text;
                    com3.Transaction = tx;
                    com3.Parameters.AddWithValue("@VersionId", item[0]["Id"].ToString());
                    id = await com3.ExecuteScalarAsync().ConfigureAwait(false);
                    tx.Commit();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Send UPD To Device after 2 minute,again.beacuse device Don't Respond");
                    Console.ForegroundColor = ConsoleColor.Green;
                    _ = Util.LogErrorAsync(new Exception("Send UPD To Device after 2 minute again"), "Send UPD To Device after 2 minute again", IMEI1).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1339 -- Method -- Trans_DelVersionDetail", IMEI1);
                    tx.Rollback();
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "1345 -- Method -- Trans_DelVersionDetail", IMEI1);
                tx.Rollback();
            }
            return sql;
        }
        public static string Decrypt(this string encryptedText, string passwordHash, string VIKey)
        {
            byte[] cipherTextBytes = Convert.FromBase64String(encryptedText);
            byte[] keyBytes = new Rfc2898DeriveBytes(passwordHash, Encoding.ASCII.GetBytes(SaltKey), 1024).GetBytes(16);
            var symmetricKey = new RijndaelManaged() { Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 };
            var decryptor = symmetricKey.CreateDecryptor(keyBytes, Convert.FromBase64String(VIKey));
            var memoryStream = new MemoryStream(cipherTextBytes);
            var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            byte[] plainTextBytes = new byte[cipherTextBytes.Length];
            int decryptedByteCount = cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length);
            memoryStream.Close();
            cryptoStream.Close();
            return Encoding.UTF8.GetString(plainTextBytes, 0, decryptedByteCount).TrimEnd("\0".ToCharArray());
        }
        internal static async Task UpdateMachineState(string IMEI1, string IMEI2, bool IsConnected)
        {
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                try
                {
                    string sql = $"if not exists(select 1 from machine where IMEI1 = @IMEI1) " +
                        $"begin " +
                        $" insert into machine(IMEI1, IMEI2, MachineTypeId) select @IMEI1, @IMEI2, 1 " +
                        $"end " +
                        $"update machine set IsConnected = @IsConnected where IMEI1 = @IMEI1 " +
                        $"insert into MachineConnectionHistory( MachineId, IsConnected) values " +
                        $" ((select id from  machine where IMEI1 = @IMEI1 ), @IsConnected)";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IsConnected", IsConnected);
                        command.Parameters.AddWithValue("@IMEI1", IMEI1);
                        command.Parameters.AddWithValue("@IMEI2", IMEI2);
                        connection.Open();
                        await command.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1393 -- Method -- UpdateMachineState", IMEI1);
                }
                finally
                {
                    connection.Close();
                }
            }
        }
        internal static async Task UpdateMachineLocation(string IMEI1, string IMEI2, string lat, string lon, double speed = 0.0,double speed2=0.0,double altitude=0.0, DateTime? DateFromDevice = null, float? CpuTemprature = null)
        {
            using (SqlConnection connection = new SqlConnection(Util.ConnectionStrings))
            {
                try
                {
                    string sql =
                        $"update machine set Latitude = case when @Lat !=N'0'  then @Lat else Latitude end, " +
                        $" Longitude = case when @Lon != N'0' then @Lon else Longitude end where IMEI1 = @IMEI1 and IMEI2=@IMEI2 " +
                        $" insert into MachineLocations(Id, MachineId,Latitude,Longitude,Speed,Speed2,Altitude,DateFromDevice,CpuTemperature) values " +
                        $" (@Id,(select id from  machine where IMEI1 = @IMEI1 and IMEI2=@IMEI2 ),@Lat,@Lon,@Speed,@Speed2,@Altitude,@fromDevice,@degree)";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@Id", Guid.NewGuid());
                        command.Parameters.AddWithValue("@IMEI1", IMEI1);
                        command.Parameters.AddWithValue("@IMEI2", IMEI2);
                        command.Parameters.AddWithValue("@Lat", lat);
                        command.Parameters.AddWithValue("@Lon", lon);
                        command.Parameters.AddWithValue("@Speed", speed);
                        command.Parameters.AddWithValue("@Speed2", speed2);
                        command.Parameters.AddWithValue("@Altitude", altitude);
                        if (DateFromDevice != null)
                        {
                            command.Parameters.AddWithValue("@fromDevice", DateFromDevice);
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@fromDevice", DBNull.Value);
                        }
                        if (CpuTemprature != null)
                        {
                            command.Parameters.AddWithValue("@degree", CpuTemprature);
                        }
                        else
                        {
                            command.Parameters.AddWithValue("@degree", DBNull.Value);
                        }
                        connection.Open();
                        await command.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1393 -- Method -- UpdateMachineLocation", IMEI1);
                }
                finally
                {
                    connection.Close();
                }
            }
        }
        public async static Task LogErrorAsync(Exception exception, string business, string ip = null)
        {

            string methdeName = "";
            string moduleName = "";
            try
            {

                var st = new StackTrace(exception, true);
                var frame = st.GetFrame(0);
                if (frame != null)
                {
                    methdeName = string.Format("{0}.{1}", frame.GetMethod().DeclaringType.FullName, exception.TargetSite.ToString());
                    moduleName = exception.TargetSite.DeclaringType.Module.Name;
                    var assemblyName = exception.TargetSite.DeclaringType.Assembly.FullName;
                }

            }
            catch
            {
                // Console.WriteLine("Error In LogErrorAsync Error:{0} \n MethodNamd or MoudleName don't have value", e.Message);                
            }
            if (exception.Data.Count > 0)
            {
                exception.Data.ToJsonString();
            }
            using (SqlConnection con = new SqlConnection(Util.ConnectionStrings))
            {
                var com = con.CreateCommand();
                com.CommandText = @"INSERT INTO [system].[Errors]
                                   ([Date]
                                   ,[Business]
                                   ,[Module]
                                   ,[Methode]
                                   ,[Message]
                                   ,[RawError]
                                   ,[ExtraData])
                             VALUES
                                   (GETDATE()
                                   ,@Business
                                   ,@Module
                                   ,@Methode
                                   ,@Message
                                   ,@RawError
                                   ,@ip);
                            SELECT @@IDENTITY";
                com.Parameters.AddWithValue("@Business", business);
                com.Parameters.AddWithValue("@Module", moduleName);
                com.Parameters.AddWithValue("@Methode", methdeName);
                com.Parameters.AddWithValue("@Message", exception.Message);
                com.Parameters.AddWithValue("@RawError", exception.ToString());
                com.Parameters.AddWithValue("@ip", ip ?? "");
                try
                {
                    await con.OpenAsync();
                    await com.ExecuteScalarAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error In Save Error:{0}", e);
                    Console.WriteLine("Stack Trace \n ", Environment.StackTrace);
                    //throw e; //98-12-1
                }
                finally
                {
                    con.Close();
                }
            }
        }
        public static string ToJsonString(this object obj)
        {
            string retVal = null;
            if (obj != null)
            {
                retVal = JsonConvert.SerializeObject(obj, Formatting.Indented);
            }
            return retVal;
        }

    }
}
