using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
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
                        _ = ProcessMIDRecievedContent(state, paramArray).ConfigureAwait(false);
                        break;
                    case "SRQ":  //Device Say that want sync
                        _ = ProcessDeviceWantSync(state, paramArray).ConfigureAwait(false);
                        break;
                    case "TSC": //for communication and run task on device between device and server --omid added
                        _ = ProcessTSCRecievedContent(state, paramArray).ConfigureAwait(false);
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
                        _ = ProcessUSDSMS(state, paramArray).ConfigureAwait(false);
                        break;
                    case "YesI'mThere": //addby omid
                        if (paramArray.Length > 1)
                        {
                            DateTime.TryParse(paramArray[1].Substring(5, paramArray[1].Length - 5), out DateTime fromDevice);
                            _ = CheckHandShake(state, fromDevice).ConfigureAwait(false);
                        }
                        else
                        {
                            _ = CheckHandShake(state, null).ConfigureAwait(false);
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
            ShowMessage($"Get SRQ From IMEI1 ={state.IMEI1}", ConsoleColor.DarkGray, ConsoleColor.Green,state.IMEI1);
            _ = Util.SyncDevice(state);
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
                _ = LogErrorAsync(new Exception("update ussd"), string.Join("@", paramArray), $"Update Ussd -- 108 ").ConfigureAwait(false);
                int.TryParse(paramArray[1].Split(":")[1], out int UsId);
                DateTime.TryParse(paramArray[3].Substring(5, paramArray[3].Length - 5), out DateTime Time);
                using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                            command.Parameters.AddWithValue("@fromDevice", Time);
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
                    _ = LogErrorAsync(new Exception("ProcessSMS"), String.Join("", paramArray), "ProcessSMS").ConfigureAwait(false);
                    _ = ProcessSMS(state, paramArray);
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

            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
            {
                try
                {
                    string sql = $"SELECT * from TempTerminteTest ttt " +
                        $" where ttt.machineId = (select id from machine where IMEI1 = @IMEI1) for json path";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IMEI1", state.IMEI1); //state.IMEI1);
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
                ShowMessage($"Send TRM From IMEI1 ={state.IMEI1}", ConsoleColor.DarkGray, ConsoleColor.Green, state.IMEI1);                
                foreach (var item in JArray.Parse(TerminateTest))
                {
                    var content = ("TRM#" + item.ToString().Replace("}", "").Replace(",", "#").
                        Replace("{", "")).Replace(" ", "").Replace("\r", "").Replace("\n", "").ToString().
                        Encrypt("sample_shared_secret");
                    AsynchronousSocketListener.Send(state.workSocket, TcpSettings.VIKey + " ," + content);
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
            ShowMessage($"Get SYE From IMEI1 ={state.IMEI1}", ConsoleColor.DarkGray, ConsoleColor.Green, state.IMEI1);            
            _ = LogErrorAsync(new Exception($"SYE of Sync Process on IMEI1{state.IMEI1}"), $"IMEI ={state.IMEI1} IP= {state.IP}", String.Join(",", paramArray)).ConfigureAwait(false);
            int syncMasterId; int.TryParse(paramArray[1].Split(':')[1], out syncMasterId);
            await UpdateMasterDetailSync(state, syncMasterId, "SYE", true);
        }
        private static async Task CheckHandShake(StateObject state, DateTime? fromDevice)
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
            ShowMessage($"Sync Time IMEI1 ={state.IMEI1}/IP={state.IP}", ConsoleColor.Yellow, ConsoleColor.Green, state.IMEI1);            
            try
            {
                await AddSync(state).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "100  ProcessFLDDevice-StopSendTest").ConfigureAwait(false);
            }
        }
        private async static Task<bool> UpdateMasterDetailSync(StateObject state, int syncMasterId, string step, bool? iscompeleted = null)
        {
            bool result = false;
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                            if (iscompeleted != null)
                            {
                                ShowMessage($"Successfully Upload-SYNC  from IMEI1={state.IMEI1}/IP={state.IP}", ConsoleColor.Yellow, ConsoleColor.Green, state.IMEI1);                                
                                _ = LogErrorAsync(new Exception($"Successfully Upload {step} of Sync Process on IMEI1{state.IMEI1}"), $"IMEI ={state.IMEI1} IP= {state.IP}").ConfigureAwait(false);
                            }
                            else
                            {
                                ShowMessage($"Success: {step} of SYNC Process on IMEI1={state.IMEI1}/IP={state.IP}", ConsoleColor.Yellow, ConsoleColor.Green, state.IMEI1);                                
                                _ = LogErrorAsync(new Exception($"{step} of Sync Process on IMEI1{state.IMEI1}"), $"IMEI ={state.IMEI1} IP= {state.IP}").ConfigureAwait(false);
                            }                                
                            
                            result = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowMessage($"Failed:{step} of SYNC  IMEI1={state.IMEI1}/IP={state.IP}", ConsoleColor.Red, ConsoleColor.Green, state.IMEI1);                    
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
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
            {
                connection.Open();
                var tx = connection.BeginTransaction();
                try
                {
                    string sql = $" select id,TimeZone from machine where IMEI1 =@IMEI1";
                    using (SqlCommand command0 = new SqlCommand(sql, connection))
                    {
                        command0.CommandTimeout = 100000;
                        command0.CommandType = CommandType.Text;
                        command0.Transaction = tx;
                        command0.Parameters.AddWithValue("@IMEI1", state.IMEI1);
                        var selectedMachine = await command0.ExecuteReaderAsync().ConfigureAwait(false);
                        int machineId = 0; string timeZone = string.Empty;
                        if (selectedMachine.Read())
                        {
                            int.TryParse(selectedMachine["id"].ToString(), out machineId);
                            timeZone = selectedMachine["TimeZone"].ToString();
                        }
                        selectedMachine.Close();
                        if (machineId > 0)
                        {
                            sql = "insert into SyncMaster" +
                                    "(MachineId,IMEI1,Status,CreateDate,DisconnectedDate,CntFileGet,IsCompeleted) " +
                                    " values (@MachineId,@IMEI1,@Status,@CreateDate,@DisconnectedDate,@CntFileGet,@IsCompeleted);" +
                                    " select SCOPE_IDENTITY()";
                            using (SqlCommand command = new SqlCommand(sql, connection))
                            {
                                command.CommandTimeout = 100000;
                                command.CommandType = CommandType.Text;
                                command.Transaction = tx;
                                command.Parameters.AddWithValue("@MachineId", machineId);
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
                                        command3.Transaction = tx;
                                        command3.Parameters.AddWithValue("@PsyncId", syncMasterId);
                                        command3.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                        command3.Parameters.AddWithValue("@Command", "SYN");
                                        command3.Parameters.AddWithValue("@status", 1);
                                        await command3.ExecuteScalarAsync().ConfigureAwait(false);
                                        tx.Commit();
                                        var msg = ($"SYN#\"SId\":{syncMasterId}#\"Rest\":\"{TcpSettings.Rest}\"#\"TimeZone\":\"{timeZone}\"#");
                                        var content = msg.Encrypt("sample_shared_secret");
                                        AsynchronousSocketListener.Send(state.workSocket, TcpSettings.VIKey + " ," + content);
                                        ShowMessage($"Send SYNC IMEI1={state.IMEI1}/IP={state.IP},TimZone={timeZone}", ConsoleColor.Yellow, ConsoleColor.Green, state.IMEI1);                                        
                                        _ = LogErrorAsync(new Exception($"Send Sync IMEI1{state.IMEI1}"), msg, $"IMEI ={state.IMEI1} IP= {state.IP}").ConfigureAwait(false);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowMessage($"Don't Send SYNC IMEI1={state.IMEI1}/IP={state.IP}", ConsoleColor.Red, ConsoleColor.Green, state.IMEI1);
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
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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

            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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

                        var content = msg.Encrypt("sample_shared_secret");
                        AsynchronousSocketListener.Send(state.workSocket, TcpSettings.VIKey + " ," + content);
                        ShowMessage($"Send USSD To IMEI1={state.IMEI1}/IMEI2 ={state.IMEI2}/IP={state.IP}", ConsoleColor.Yellow, ConsoleColor.Green, state.IMEI1);
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
                            _ = LogErrorAsync(ex, "405  SendUSS-Update MachineUssd").ConfigureAwait(false);
                        }
                        finally
                        {
                            connection.Close();
                        }
                    }

                }
                catch (Exception ex)
                {
                    ShowMessage($"Don't Send USSD IMEI1={state.IMEI1}/IMEI2 ={state.IMEI2}/IP={state.IP}", ConsoleColor.Red, ConsoleColor.Green, state.IMEI1);                    
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
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                    ShowMessage($"Time Error In Test.DefinedTestMachineId= {definedTestMachineId} has Error in Date/Time>>> \n>>> IMEI1={state.IMEI1}/IP={state.IP}", ConsoleColor.Red, ConsoleColor.Green, state.IMEI1);                    
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
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                    _ = LogErrorAsync(ex, "100  ProcessFLDDevice-StopSendTest").ConfigureAwait(false);
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
                    ShowMessage($"Get LOC From IMEI1={state.IMEI1}/IP={state.IP}", ConsoleColor.DarkGray, ConsoleColor.Green, state.IMEI1);                    
                    if (paramArray[7].Split(':')[1] != ",,,,,,,,")
                    {
                        var gpsTmp = paramArray[7].Split(':')[1];
                        var gpsData = gpsTmp.Split(',');
                        //var timeFromGPGGA = gpsData[1];
                        var latFromGPGGA = gpsData[2];
                        var lonFromGPGGA = gpsData[4];
                        if (!string.IsNullOrEmpty(latFromGPGGA) && !string.IsNullOrEmpty(lonFromGPGGA))
                        {
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
                                await UpdateMachineLocation(state.IMEI1, imei2, lat.ToString(), lon.ToString(), Speed, Speed2, Altitude, fromDevice, cpuTemp).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
        }
        private static void ProcessLowBatteryDevice(StateObject state, string[] paramArray)
        {
            ShowMessage($"Device by IMEI1={state.IMEI1}/IP={state.IP} Low Battey", ConsoleColor.DarkGray, ConsoleColor.Green, state.IMEI1);            
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
                            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                                        ShowMessage($"Get RPL from IMEI1={stateobject.IMEI1}/IP={stateobject.IP}", ConsoleColor.Yellow, ConsoleColor.Green, stateobject.IMEI1);
                                    }
                                }
                                catch (Exception ex)
                                {

                                    _ = LogErrorAsync(ex, "100  ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                }
                                finally
                                {
                                    connection.Close();
                                }
                                try
                                {
                                    //update IsDone in  machineVersion Table ==>  mean...Update Process Finished but param2 show result
                                    //Device send rpl Means (1) update successfully (2) get fse from server and 3 times do upr .
                                    string sql = $"Update MachineVersion Set IsDone = 1 , CompleteDate = @CompleteDate , UpdateResult=@UpdateResult " +
                                                $" where Id = @VersionId";
                                    var UpdateResult = paramArray[2].Split(':')[1] ?? string.Empty;
                                    using (SqlCommand command = new SqlCommand(sql, connection))
                                    {
                                        command.CommandTimeout = 100000;
                                        command.CommandType = CommandType.Text;
                                        command.Parameters.AddWithValue("@VersionId", VID);
                                        command.Parameters.AddWithValue("@UpdateResult", UpdateResult);
                                        command.Parameters.AddWithValue("@CompleteDate", DateTime.Now);
                                        connection.Open();
                                        await command.ExecuteScalarAsync().ConfigureAwait(false);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _ = LogErrorAsync(ex, "165  ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
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

                        _ = LogErrorAsync(ex, "176  ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "182  ProcessRPLRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
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
                            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                                        command.Parameters.AddWithValue("@State", "UPR");
                                        command.Parameters.AddWithValue("@CreateDate", DateTime.Now);
                                        command.Parameters.AddWithValue("@Sender", stateobject.IMEI1);
                                        command.Parameters.AddWithValue("@Reciever", "Server");
                                        connection.Open();
                                        await command.ExecuteScalarAsync().ConfigureAwait(false);
                                        ShowMessage($"Get UPR from IMEI1={stateobject.IMEI1}/IP={stateobject.IP}", ConsoleColor.Yellow, ConsoleColor.Green, stateobject.IMEI1);                                        
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _ = LogErrorAsync(ex, "225  ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                }
                                finally
                                {
                                    connection.Close();
                                }
                                if (CheckFileSizeAndFileName(VID, paramArray[2].ToString()).Result)
                                {
                                    //Send msg To Device for OK fileDownload

                                    var content = ("RPL#\"VID\":" + VID + "#").Encrypt("sample_shared_secret");
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
                                            AsynchronousSocketListener.Send(stateobject.workSocket, TcpSettings.VIKey + " ," + content);
                                            ShowMessage($"Send RPL To IMEI1={stateobject.IMEI1}/IP={stateobject.IP}", ConsoleColor.Yellow, ConsoleColor.Green, stateobject.IMEI1);                                           
                                            //}
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "262  ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        connection.Close();
                                    }
                                }
                                else //Download file Nok
                                {
                                    var content = ("FSE#\"VID\":" + VID + "#").Encrypt("sample_shared_secret");
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
                                            AsynchronousSocketListener.Send(stateobject.workSocket, TcpSettings.VIKey + " ," + content);
                                            ShowMessage($"Send FSE To IMEI1={stateobject.IMEI1}/IP={stateobject.IP}**File Size has Error**", ConsoleColor.Yellow, ConsoleColor.Green, stateobject.IMEI1);
                                            //}
                                        }

                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "303  ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
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
                        _ = LogErrorAsync(ex, "316  ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "322  ProcessUPRRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
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
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
            {
                int? SelectedVersion = 0;
                try
                {
                    string sql = $"SELECT FileSize from MachineVersion where Id = @Id ";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@Id", VersionId);
                        connection.Open();
                        SelectedVersion = (int?)await command.ExecuteScalarAsync().ConfigureAwait(false);
                        if (SelectedVersion == sFileDownload)
                        {
                            result = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "362  CheckFileSize", $"FileSizeFromDevice>>{ FileSize} ,SizeFromDb>>{ SelectedVersion}");
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
        /// 990827
        /// tcpServer client.GetAsync($"http://{url}") has err:No connection could be made because the target machine actively refused it. 
        /// therefore in tcp do not download file and check filesize. define new field FileSize in MachineVersion and when user upload file 
        /// calculate size of uploaded file and save to FileSize field. tcp check this field with size sended Device
        /// </summary>
        /// <param name="url">Must be Full Url ,exp. Http://185.192.112.74/share/config.zip </param>
        /// <returns></returns>
        public static async Task<byte[]> DownloadFile(string url)
        {
            using (var client = new HttpClient(TcpSettings._handler) { Timeout = TimeSpan.MaxValue })
            {
                //url recieve from db do not has Http/Https Protocol 
                //url = string.Format("http{0}://{1}",, url); //supporting http and https 

                using var result = await client.GetAsync($"http://{url}");
                if (result.IsSuccessStatusCode)
                {
                    return await result.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    using var result2 = await client.GetAsync($"https://{url}");
                    if (result2.IsSuccessStatusCode)
                    {
                        return await result2.Content.ReadAsByteArrayAsync();
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
                            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                                        ShowMessage($"Get UPG from  IMEI1={stateobject.IMEI1}/IP={stateobject.IP}", ConsoleColor.Yellow, ConsoleColor.Green, stateobject.IMEI1);                                        
                                    }
                                }

                                catch (Exception ex)
                                {
                                    _ = LogErrorAsync(ex, "430  ProcessUPGRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
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
                        _ = LogErrorAsync(ex, "412  ProcessUPGRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "407  ProcessUPGRecievedContent", $"IMEI1={stateobject.IMEI1} Ip={stateobject.IP}").ConfigureAwait(false);
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
                    if (!string.IsNullOrEmpty(Array.Find(paramArray, element => element.Contains("TerminateTest:TRUE")))) //finish Time for abort test
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
                        double lon = 0;
                        double lat = 0;
                        bool isLoopingParam = false;
                        string witchTest = string.Empty;
                        string[] tParam = new string[2];
                        string[] ActiveParam = new string[2]; 
                        string[] SyncParam = new string[2]; 
                        string[] AsyncParam = new string[2];
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
                                                if (t[1].Split(',') != null)
                                                {

                                                    var gpsData = t[1].Split(',');
                                                    //var timeFromGPGGA = gpsData[1];
                                                    var latFromGPGGA = gpsData[2];
                                                    var lonFromGPGGA = gpsData[4];
                                                    //if lat & lon Exist ,then calculate location and update machine status 990821
                                                    if (!string.IsNullOrEmpty(latFromGPGGA) && !string.IsNullOrEmpty(lonFromGPGGA))
                                                    {
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
                                                        if (DateTime.TryParse(paramArray[4].Substring(5, paramArray[4].Length - 5), out DateTime fromDevice))
                                                        {
                                                            _ = Util.UpdateMachineLocation(state.IMEI1, state.IMEI2, lat.ToString(), lon.ToString(),
                                                            Speed, Speed2, Altitude, fromDevice, null).ConfigureAwait(false);
                                                        }
                                                        else
                                                        {
                                                            _ = Util.UpdateMachineLocation(state.IMEI1, state.IMEI2, lat.ToString(), lon.ToString(),
                                                              Speed, Speed2, Altitude, null, null).ConfigureAwait(false);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else if (t[0].Equals("Ping"))
                                        {
                                            var pingResult = t[1].Split(',');
                                            if (pingResult.Length == 1)
                                            {
                                                inseretStatment += "Ping,";
                                                valueStatmenet += $"'{t[1]}',";
                                            }
                                            else
                                            {
                                                inseretStatment += " NumOfPacketSent , NumOfPacketReceived, NumOfPacketLost, Ping, Rtt, MinRtt,AvgRtt,MaxRtt,mdev,";
                                                valueStatmenet += pingResult[0].Split(' ')[0] + " , " + pingResult[1].Split(' ')[1] + " , " + pingResult[2].Split('%')[0].Split(' ')[1] + " , 'Ping' ," +
                                                    pingResult[3].Split('=')[0].Split(' ')[2].Split('m')[0] + " , " + pingResult[3].Split('=')[1].Split('/')[0] + " , " +
                                                    pingResult[3].Split('=')[1].Split('/')[1] + " , " + pingResult[3].Split('=')[1].Split('/')[2] + " , " + pingResult[3].Split('=')[1].Split('/')[3].Split(' ')[0] + " ,";
                                            }
                                        }
                                        else if (t[0].Equals("TraceRoute"))
                                        {
                                            isLoopingParam = true;
                                            witchTest = "TraceRoute";
                                            tParam = t;
                                        }
                                        else if (t[0].Contains("ActiveSET") && t[1] != "NULL")
                                        {
                                            isLoopingParam = true;
                                            witchTest = "ActiveSet";
                                            ActiveParam = t;
                                        }
                                        else if (t[0].Contains("SyncNeighborSET"))
                                        {
                                            if (t[1] != "NULL")
                                            {
                                                isLoopingParam = true;
                                                witchTest = "ActiveSet";
                                                SyncParam = t;
                                            }
                                        }
                                        else if (t[0].Contains("AsyncNeighborSET") && t[1] != "NULL")
                                        {
                                            isLoopingParam = true;
                                            witchTest = "ActiveSet";
                                            AsyncParam = t;
                                        }
                                        else if (t[0].Contains("EONS"))
                                        {
                                            if (t[1] != "NaN")
                                            {
                                                var eons = t[1].Split(',');
                                                var mcc = Convert.ToInt32(eons[1].Substring(0, 3));
                                                var mnc = Convert.ToInt32(eons[1].Substring(3, 2));
                                                inseretStatment += "MCC ,MNC,";
                                                valueStatmenet = valueStatmenet + mcc + " , " + mnc + " , ";
                                            }
                                        }
                                        else if (t[0].Contains("CGREG"))
                                        {
                                            if (t[1] != "NaN")
                                            {
                                                var cgParm = t[1].Split(',');
                                                string rsVal = string.Empty;
                                                int.TryParse(cgParm[1], out int regstat);
                                                switch (regstat)
                                                {
                                                    case 0:
                                                        rsVal = "Not registered, not currently searching ";
                                                        break;
                                                    case 1:
                                                        rsVal = "Registered, home network";
                                                        break;
                                                    case 2:
                                                        rsVal = "Not registered, currently searching.";
                                                        break;
                                                    case 3:
                                                        rsVal = "Registration denied";
                                                        break;
                                                    case 4:
                                                        rsVal = "Unknown";
                                                        break;
                                                    case 5:
                                                        rsVal = "Registered, roaming";
                                                        break;
                                                }
                                                int LacVal = 0;
                                                // هگزاست تبدیل شود به دسیمال                                            
                                                LacVal = Convert.ToInt32(cgParm[2], 16);
                                                // هگزاست تبدیل شود به دسیمال    
                                                int cidValue = -1;
                                                cidValue = Convert.ToInt32(cgParm[3], 16);

                                                inseretStatment += "Reg_stat,LAC,CID,";
                                                valueStatmenet = valueStatmenet + "'" + rsVal + "'," + LacVal + "," + cidValue + ",";
                                            }
                                        }
                                        else if (t[0].Contains("IFX") )
                                        {
                                            if (t[1] != "NaN")
                                            {
                                                var ifx = t[1].Split(",");
                                                string srvSt = string.Empty, srvdom = string.Empty, roamst = string.Empty, simstat = string.Empty;
                                                int.TryParse(ifx[0], out int svSt);
                                                switch (svSt)
                                                {
                                                    case 0:
                                                        srvSt = "No services";
                                                        break;
                                                    case 1:
                                                        srvSt = "Restricted ";
                                                        break;
                                                    case 2:
                                                        srvSt = "Valid ";
                                                        break;
                                                    case 3:
                                                        srvSt = "Restricted regional ";
                                                        break;
                                                    case 4:
                                                        srvSt = "Power saving ";
                                                        break;
                                                }
                                                int.TryParse(ifx[1], out int svdom);
                                                switch (svdom)
                                                {
                                                    case 0:
                                                        srvdom = "No services";
                                                        break;
                                                    case 1:
                                                        srvdom = "CS only";
                                                        break;
                                                    case 2:
                                                        srvdom = "PS only";
                                                        break;
                                                    case 3:
                                                        srvdom = "PS+CS";
                                                        break;
                                                    case 4:
                                                        srvdom = "Not registered- searching ";
                                                        break;
                                                }
                                                int.TryParse(ifx[2], out int roam);
                                                switch (roam)
                                                {
                                                    case 0:
                                                        roamst = "Not roaming";
                                                        break;
                                                    case 1:
                                                        roamst = "Roaming";
                                                        break;
                                                }
                                                int.TryParse(ifx[3], out int ssim);
                                                switch (ssim)
                                                {
                                                    case 0:
                                                        simstat = "Invalid SIM ";
                                                        break;
                                                    case 1:
                                                        simstat = "Valid SIM ";
                                                        break;
                                                    case 2:
                                                        simstat = "Invalid SIM in CS";
                                                        break;
                                                    case 3:
                                                        simstat = "Invalid SIM in PS";
                                                        break;
                                                    case 4:
                                                        simstat = "Invalid SIM in PS and CS";
                                                        break;
                                                    case 240:
                                                        simstat = "ROMSIM version ";
                                                        break;
                                                    case 250:
                                                        simstat = "No SIM  ";
                                                        break;
                                                }
                                                int.TryParse(ifx[5], out int ssmode);
                                                switch (ssmode)
                                                {
                                                    case 0: ssmode = 0; break;
                                                    case 1: ssmode = 1; break;
                                                    case 3: ssmode = 4; break;
                                                    case 6: ssmode = 8; break;
                                                }

                                                inseretStatment += "Srv_status,Srv_domain,Roam_status,Sim_state,SystemMode,";
                                                valueStatmenet = valueStatmenet + "'" + srvSt + "','" + srvdom + "','" + roamst + "','" + simstat + "'," + ssmode + ",";
                                            }
                                        }
                                        else if (t[0].Contains("HCSQ"))
                                        {
                                            if ( t[1] != "NaN") {
                                                var hc = t[1].Split(',');
                                                double.TryParse(hc[1], out double rssi);
                                                rssi = (rssi - 121) + (new Random().NextDouble());
                                                double.TryParse(hc[2], out double rsrp);
                                                rsrp = (rsrp - 141) + (new Random().NextDouble());
                                                double.TryParse(hc[3], out double sinr);
                                                sinr = (sinr * 0.2) - 20.5;
                                                double.TryParse(hc[4], out double rsrq);
                                                rsrq = (rsrq * 0.5) - 20;
                                                inseretStatment += "RSSI,RSRP,SINR,RSRQ,";
                                                valueStatmenet += rssi + "," + rsrp + "," + sinr + "," + rsrq + ",";
                                            }
                                        }
                                        else if (t[0].Contains("LCC"))
                                        {
                                            if (t[1] != "NaN")
                                            {
                                                var t1Split = t[1].Split(",");
                                                string cdir = string.Empty, cstat = string.Empty, cmode = string.Empty, cnumber = string.Empty;
                                                int.TryParse(t1Split[2], out int dir); if (dir == 1) cdir = "MT"; else cdir = "MO";
                                                int.TryParse(t1Split[3], out int stat); switch (stat)
                                                {
                                                    case 0: cstat = "Active"; break;
                                                    case 1: cstat = "Held"; break;
                                                    case 2: cstat = "Dialing"; break;
                                                    case 3: cstat = "Alerting"; break;
                                                    case 4: cstat = "Incoming"; break;
                                                    case 5: cstat = "Waiting"; break;
                                                    case 6: cstat = "Disconnect"; break;
                                                }
                                                int.TryParse(t1Split[4], out int mode); switch (mode)
                                                {
                                                    case 0: cmode = "Voice"; break;
                                                    case 1: cmode = "Data"; break;
                                                    case 2: cmode = "Fax"; break;
                                                    case 9: cmode = "Unknown"; break;
                                                }
                                                inseretStatment += "CLCC_dir,CLCC_stat,CLCC_mode,CLCC_number ,";
                                                valueStatmenet = valueStatmenet + "'" + cdir + "','" + cstat + "','" + cmode + "','" + t1Split[6] + "',";
                                            }
                                        }
                                        else
                                        {
                                            if (t[0] == "CreateDate")
                                            {
                                                createDate = t[1].ToString();
                                            }
                                            inseretStatment = inseretStatment + t[0] + " ,";
                                            valueStatmenet = valueStatmenet +
                                                (int.TryParse(t[1].Replace("dBm", ""), out i) || t[1].Contains("0x") ? i.ToString() : t[1] == "NuNu" ? "null" : "'" + t[1].Replace("dBm", "") + "'") + " ,";
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
                        if (isLoopingParam)
                        {                            
                            switch (witchTest)
                            {                                
                                case "ActiveSet":
                                    if(ActiveParam[0]!=null)
                                        _= ActiveSet(inseretStatment, valueStatmenet, ActiveParam, TestId, createDate, state);
                                    if (SyncParam[0] != null)
                                        _ = NeighborSet(SyncType.Sync, SyncParam, TestId, createDate, lat, lon, state);
                                    if (AsyncParam[0] != null)
                                        _ = NeighborSet(SyncType.Async, AsyncParam, TestId, createDate, lat, lon, state);
                                    break;
                                case "TraceRoute":
                                      _= TraceRoute(inseretStatment, valueStatmenet, tParam, TestId, createDate, state);
                                    break;
                            }
                        }
                        else
                        {
                            await InsertTestResult($"{inseretStatment} RegisterDate) {valueStatmenet} '{DateTime.Now}' )", TestId, createDate, state);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "1436  ProcessTSCRecievedContent", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
            }
        }
        private static async Task NeighborSet(SyncType SType, string[] t, int TestId, string CreateDate,double lat, double lon, StateObject state)
        {
            
            string insertStatment = $"insert into TestResultNeighbor (TestId,lat,long,CreateDate,SyncType,SetNumber" +
                                    $",Psc,UARFCN,SSC,Sttd,TotECIO,ECIO,Rscp,WinSize,RegisterDate) values";
            string valueStatmenet = $" ({TestId},{lat},{lon},'{CreateDate}',";
           string fn = string.Empty;            
            var ActSets = t[1].Split(',');
            if (int.TryParse(ActSets[0], out int LoopOfActSet))
            {                
                if (LoopOfActSet > 0)
                {
                    if (LoopOfActSet == 1)
                    {
                        try
                        {
                            fn = valueStatmenet + $"{(SType == SyncType.Async ? "'Async'" : "'Sync'")},{LoopOfActSet},{Convert.ToInt32(ActSets[1])}," +
                                $"{Convert.ToInt32(ActSets[2])},{Convert.ToInt32(ActSets[3])},{Convert.ToDouble(ActSets[4])},{Convert.ToDouble(ActSets[5]) * -1}," +
                                $"{ Convert.ToDouble(ActSets[6]) * -1},{ Convert.ToDouble(ActSets[7]) * -1},{ Convert.ToDouble(ActSets[8])},'{DateTime.Now}')";
                        }
                        catch(Exception ex)
                        {
                            _ = LogErrorAsync(ex, "1352  NeighborSet. Convert Params has Error", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        int stIndex = 1;
                        for (int i = 1; i <= LoopOfActSet; i++)
                        {
                            string tmpvalue = valueStatmenet + $"{(SType == SyncType.Async ? "'Async'" : "'Sync'")},{LoopOfActSet},";
                            if (i == LoopOfActSet)
                                {
                                        try
                                        {
                                            tmpvalue += $"{ Convert.ToInt32(ActSets[stIndex])}," +
                                                $"{Convert.ToInt32(ActSets[stIndex + 1])},{Convert.ToInt32(ActSets[stIndex + 2])},{Convert.ToDouble(ActSets[stIndex + 3])},{Convert.ToDouble(ActSets[stIndex + 4]) * -1}," +
                                                $"{ Convert.ToDouble(ActSets[stIndex + 5]) * -1},{ Convert.ToDouble(ActSets[stIndex + 6]) * -1},{ Convert.ToDouble(ActSets[stIndex + 7])},'{DateTime.Now}')";
                                        }
                                        catch (Exception ex)
                                        {
                                            _ = LogErrorAsync(ex, "1371 NeighborSet. Convert Params has Error", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                                        }
                                }
                                else
                                {
                                        try
                                        {
                                            tmpvalue += $"{ Convert.ToInt32(ActSets[stIndex])}," +
                                               $"{Convert.ToInt32(ActSets[stIndex + 1])},{Convert.ToInt32(ActSets[stIndex + 2])},{Convert.ToDouble(ActSets[stIndex + 3])},{Convert.ToDouble(ActSets[stIndex + 4]) * -1}," +
                                               $"{Convert.ToDouble(ActSets[stIndex + 5]) * -1},{ Convert.ToDouble(ActSets[stIndex + 6]) * -1},{ Convert.ToDouble(ActSets[stIndex + 7])},'{DateTime.Now}'),";
                                        }
                                        catch (Exception ex)
                                        {
                                            _ = LogErrorAsync(ex, "1384  NeighborSet. Convert Params has Error", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                                        }
                                }                          
                                fn += tmpvalue;
                                stIndex += 8;
                        }
                    }
                    try
                    {
                        await InsertTestResult($"{insertStatment} {fn} ", TestId, CreateDate, state);
                    }
                    catch (Exception ex)
                    {
                        _ = LogErrorAsync(ex, "1491  NeighborSet", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                    }
                }

            }
        }

        /// <summary>
        /// ActiveSet Param in TSC
        /// first param is length of loop
        /// </summary>
        /// <param name="inseretStatment"></param>
        /// <param name="valueStatmenet"></param>
        /// <param name="t"></param>
        /// <param name="testId"></param>
        /// <param name="createDate"></param>
        /// <param name="state"></param>
        private static async Task  ActiveSet(string insertStatment, string valueStatmenet,
            string[] t,
            int TestId,
            string CreateDate,
            StateObject state)
        {
            valueStatmenet = valueStatmenet.Replace("Values", "");
            string tmpvalue=string.Empty,fn = string.Empty;
            insertStatment += "ActiveSetNumber,PSC,UARFCN,SSC,STTD,TOTECIO,ECIO,RSCP,TPC,OVSF,WinSize,RegisterDate) values ";
            var ActSets = t[1].Split(',');
            if(int.TryParse(ActSets[0],out int LoopOfActSet))
            {
                if(LoopOfActSet > 0)
                {
                    if (LoopOfActSet == 1)
                    {
                        fn = valueStatmenet + $"{LoopOfActSet},";
                        try
                        {
                            fn += $"{Convert.ToDouble(ActSets[1])},{Convert.ToDouble(ActSets[2])},{Convert.ToDouble(ActSets[3])},{Convert.ToDouble(ActSets[4])}," +
                                $"{Convert.ToDouble(ActSets[5]) * -1},{Convert.ToDouble(ActSets[6]) * -1},{Convert.ToDouble(ActSets[7]) * -1},{Convert.ToDouble(ActSets[8])}," +
                                $"{Convert.ToDouble(ActSets[9])},{Convert.ToDouble(ActSets[10])},'{DateTime.Now}')";
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, "1439 ActiveSet. Convert Params has Error", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        int cntvalue = 1;               
                        for (int i = 1; i <= LoopOfActSet; i++)
                        {
                            int stIndex = cntvalue; int enIndex =i*10;
                            for (int j = stIndex; j <= enIndex; j++)
                            {
                                tmpvalue = valueStatmenet + $"{LoopOfActSet},";
                                if (j == 10)
                                {
                                    try
                                    {
                                        tmpvalue += $"{Convert.ToDouble(ActSets[stIndex])},{Convert.ToDouble(ActSets[stIndex + 1])},{Convert.ToDouble(ActSets[stIndex + 2])},{Convert.ToDouble(ActSets[stIndex + 3])}," +
                                                    $"{Convert.ToDouble(ActSets[stIndex + 4]) * -1},{Convert.ToDouble(ActSets[stIndex + 5]) * -1},{Convert.ToDouble(ActSets[stIndex + 6]) * -1}," +
                                                    $"{Convert.ToDouble(ActSets[stIndex + 7])},{Convert.ToDouble(ActSets[stIndex + 8])},{Convert.ToDouble(ActSets[stIndex + 9])}," +
                                                    $"'{DateTime.Now}')";
                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "1462 ActiveSet. Convert Params has Error", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        tmpvalue += $"{Convert.ToDouble(ActSets[stIndex])},{Convert.ToDouble(ActSets[stIndex + 1])},{Convert.ToDouble(ActSets[stIndex + 2])},{Convert.ToDouble(ActSets[stIndex + 3])}," +
                                                    $"{Convert.ToDouble(ActSets[stIndex + 4]) * -1},{Convert.ToDouble(ActSets[stIndex + 5]) * -1},{Convert.ToDouble(ActSets[stIndex + 6]) * -1}," +
                                                    $"{Convert.ToDouble(ActSets[stIndex + 7])},{Convert.ToDouble(ActSets[stIndex + 8])},{Convert.ToDouble(ActSets[stIndex + 9])}," +
                                                    $"'{DateTime.Now}'),";
                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "1476 ActiveSet. Convert Params has Error", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                                    }
                                }
                            }
                            fn += tmpvalue;
                            cntvalue += enIndex;
                        }
                    }
                    try
                    {
                        await InsertTestResult($"{insertStatment} {fn} ", TestId, CreateDate, state);
                    }
                    catch (Exception ex)
                    {
                        _ = LogErrorAsync(ex, "1563 ActiveSet", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                    }
                }
               
            }

        }

        /// <summary>
        /// /// In TraceRoute , because mutil traceRoute response Received from device in one Test 
        /// use insert multi row in one sql query 
        /// </summary>
        /// <param name="insertStatment">Insert value</param>
        /// <param name="valueStatmenet">value of field</param>
        /// <param name="t">traceroute params</param>
        /// <param name="TestId">Id of Test</param>
        /// <param name="CreateDate">Date From Device</param>
        /// <param name="state"></param>
        /// <returns></returns>
        private static async Task TraceRoute(string insertStatment, string valueStatmenet, string[] t,int TestId,string CreateDate,StateObject state)
        {
            var tmpVstat = valueStatmenet =  valueStatmenet.Replace("Values", "");
            var tmpStat = insertStatment;
            insertStatment+= "TraceRoute,";
            var dd = System.Text.RegularExpressions.Regex.Split(t[1], "traceroute");
            string tmpValue, finalvalue=string.Empty;
            for (int ii = 1; ii < dd.Length; ii++)
            {
                var curTr = dd[ii].Split('\n');                
                if (curTr.Length - 1 > 1)
                {
                    tmpValue = valueStatmenet + $"'{curTr[0]}',";
                    for (int j = 1; j < curTr.Length - 1; j++)
                    {
                        insertStatment += $"hop{j},hop{j}_rtt,";
                        var vals = curTr[j].Split(' ');
                        if (curTr[j].Contains("*"))
                        {
                            if (j == curTr.Length - 2)
                            {
                                insertStatment += $"RegisterDate) values";
                                tmpValue += $"'{vals[2]}',{ System.Data.SqlTypes.SqlDouble.Null},'{DateTime.Now}')";
                                if (ii == dd.Length - 1)
                                {
                                    finalvalue += tmpValue;
                                }
                                else
                                {
                                    finalvalue += tmpValue + ",";
                                }
                            }
                            else
                            {
                                tmpValue += $"'{vals[3]}',{System.Data.SqlTypes.SqlDouble.Null},";
                            }
                        }
                        else
                        {
                            if (j == curTr.Length - 2)
                            {
                                insertStatment += $"RegisterDate) values";
                                tmpValue += $"'{vals[2]}',{ Convert.ToDouble(vals[5])},'{DateTime.Now.ToString()}')";
                                if (ii == dd.Length - 1)
                                {
                                    finalvalue += tmpValue;
                                }
                                else
                                {
                                    finalvalue += tmpValue + ",";
                                }
                            }
                            else
                            {
                                tmpValue += $"'{vals[3]}',{ Convert.ToDouble(vals[6])},";
                            }
                        }

                    }
                }
                else
                {
                    tmpStat += "TraceRoute,RegisterDate) values ";
                    tmpVstat +=  $"'{curTr[0]}','{DateTime.Now.ToString()}')";
                    _= InsertTestResult($"{tmpStat} {tmpVstat}", TestId, CreateDate, state);
                }

            }
            try
            {
                await InsertTestResult($"{insertStatment} {finalvalue} ", TestId, CreateDate, state);
            }
            catch(Exception ex)
            {
                _ = LogErrorAsync(ex, "1630  TraceRoute", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
            }
        }
        
        private static async Task InsertTestResult(string testResult, int TestId, string createDate, StateObject state)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
                {
                    connection.Open();
                    var tx = connection.BeginTransaction();
                    string sql = testResult;
                    SqlCommand command = new SqlCommand(sql, connection);
                    command.CommandTimeout = 100000;
                    command.CommandType = CommandType.Text;
                    command.Transaction = tx;
                    try
                    {
                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                        sql = $" update machine set LastTestResult = @createDate where id = (select MachineId from DefinedTestMachine where id =@TestId)";
                        var com2 = new SqlCommand(sql, connection);
                        com2.CommandTimeout = 100000;
                        com2.CommandType = CommandType.Text;
                        com2.Transaction = tx;
                        com2.Parameters.AddWithValue("@createDate", DateTime.TryParse(createDate, out DateTime fromDevice) ? fromDevice : DateTime.Now);
                        com2.Parameters.AddWithValue("@TestId", TestId);
                        try
                        {
                            await com2.ExecuteScalarAsync().ConfigureAwait(false);
                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, "1664  Trans in Update lastTestResult in machine");
                            tx.Rollback();
                        }
                    }
                    catch (Exception ex)
                    {
                        _ = LogErrorAsync(ex, "1670  Insert TestResult ");
                        tx.Rollback();
                    }
                    finally
                    {
                        connection.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "1681  insertTestResult", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
            }
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
                        int ber;
                        if (int.TryParse(param.Split(":")[1], out ber))
                        {
                            if (ber >= 99) //مقدار غیر صحیح
                                return new string[] { "BER", "NuNu" };
                            else
                                return new string[] { "BER", param.Split(":")[1] };
                        }
                        else //if BER=NAN, return null for int?
                            return new string[] { "BER", "NuNu" };
                    case "PCI":  //PID change to PCI Data:991124.
                        return new string[] { "PCI", param.Split(":")[1] };
                    case "EONS":
                        return new string[] { "EONS ", param.Split(":")[1]};
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
                    case "CGREG":
                        return new string[] { "CGREG", param.Split(":")[1] };
                    case "SYSINFOEX":
                        return new string[] { "IFX", param.Split(":")[1] };
                    case "HCSQ":
                        return new string[] { "HCSQ", param.Split(":")[1] };
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
                        int cidValue;
                        if (int.TryParse(param.Split(":")[1], out cidValue))
                        {
                            if (cidValue == -1 || cidValue == 0) //مقدار غیر صحیح
                                return new string[] { "CID", "NuNu" };
                            else
                                return new string[] { "CID", cidValue.ToString() };
                        }
                        return new string[] { "CID", "NuNu" };
                    case "UARFCN":
                        return new string[] { "UARFCN", param.Split(":")[1] };
                    case "ARFCN":
                        return new string[] { "ARFCN", param.Split(":")[1] };
                    case "DLBW":
                        int dlbw = 0;
                        if (int.TryParse(param.Split(":")[1], out dlbw))
                        {
                            switch (dlbw)
                            {
                                case 0:
                                    dlbw = 14;
                                    break;
                                case 1:
                                    dlbw = 3;
                                    break;
                                case 2:
                                    dlbw = 5;
                                    break;
                                case 3:
                                    dlbw = 10;
                                    break;
                                case 4:
                                    dlbw = 15;
                                    break;
                                case 5:
                                    dlbw = 20;
                                    break;
                                default: //has null value
                                    return new string[] { "DLBW", "NuNu" };
                            }
                        };
                        return new string[] { "DLBW", dlbw.ToString() };                        
                    case "LAC":
                        int resL = 0;
                        if ((param.Split(":")[1]).Contains("0x")) //اگر هگزاست تبدیل شود به دسیمال
                        {
                            resL = Convert.ToInt32(param.Split(":")[1], 16);
                            return new string[] { "LAC", resL.ToString() };
                        }
                        else
                        {
                            if (int.TryParse(param.Split(":")[1], out resL))
                            {
                                return new string[] { "LAC", resL.ToString() };
                            }
                        }
                        return new string[] { "LAC", "NuNu" };
                    case "ULBW":
                        int ulbw = 0;
                        if (int.TryParse(param.Split(":")[1], out  ulbw)) 
                        {
                            switch (ulbw)
                            {
                                case 0:
                                    ulbw = 14;
                                    break;
                                case 1:
                                    ulbw = 3;
                                    break;
                                case 2:
                                    ulbw = 5;
                                    break;
                                case 3:
                                    ulbw = 10;
                                    break;
                                case 4:
                                    ulbw = 15;
                                    break;
                                case 5:
                                    ulbw = 20;
                                    break;
                                default: //has null value
                                    return new string[] { "ULBW", "NuNu" };
                            }
                        };
                        return new string[] { "ULBW", ulbw.ToString() };
                    case "EER":
                        return new string[] { "CEER", param.Split(":")[1] };
                    case "LCC":
                        return new string[] { "LCC", param.Split(":")[1] };
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
                            return new string[] { "TAC", res.ToString() };
                        }
                        else
                        {
                            if (int.TryParse(param.Split(":")[1], out res))
                            {
                                return new string[] { "TAC", res.ToString() };
                            }
                        }
                        return new string[] { "TAC", "NuNu" };
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
                        if (tmpVal > 0)
                        {
                            return new string[] { "RSRP", "NuNu" };   //add in 990905.drvahidpour said 
                        }
                        return new string[] { "RSRP", tmpVal.ToString() };
                    case "TXSPEED":
                        if (float.TryParse(param.Split(":")[1], out tmpVal))
                        {
                            return new string[] { "TXSPEED", tmpVal.ToString() };
                        }
                        return new string[] { "TXSPEED", "NuNu" };
                    case "RXSPEED":
                        if (float.TryParse(param.Split(":")[1], out tmpVal))
                        {
                            return new string[] { "RXSPEED", tmpVal.ToString() };
                        }
                        return new string[] { "RXSPEED", "NuNu" };
                    case "RSSI":
                        float.TryParse(param.Split(":")[1], out tmpVal); tmpVal /= 10;//addeddby-omid-981229
                        return new string[] { "RSSI", tmpVal.ToString() };
                    case "OVSF":
                        return new string[] { "OVSF", param.Split(":")[1] }; //omid Edit and update
                    case "RXEQUAL":
                        return new string[] { "RXQual", param.Split(":")[1] };
                    case "SYSMODE":  //1:2G , 4:3G, 8:4G
                        int sysmode;
                        if (int.TryParse(param.Split(":")[1], out sysmode))
                        {                           
                            return new string[] { "SystemMode", sysmode.ToString() };
                        }
                        else
                            return new string[] { "SystemMode", "NuNu" };
                    case "PingResault":
                        return new string[] { "Ping", param.Split(":")[1] };
                    case "OPERATOR":
                        return new string[] { "Operator", param.Split(":")[1] };//addeddby-omid-981229
                    case "Traceroute":
                        return new string[] { "TraceRoute", param.Split(":")[1] };
                    case "TIME":
                        return new string[] { "CreateDate", param.Split(":")[1] + ":" + param.Split(":")[2] + ":" + param.Split(":")[3] };
                    case "UTC":
                        return new string[] { "UTC", param.Split(":")[1] + ":" + param.Split(":")[2] + ":" + param.Split(":")[3] };
                    case "GPS":
                        return new string[] { "GPS", param.Split(":")[1] };
                    case "Layer3":
                        return new string[] { "Layer3Messages", param.Split(":")[1] };
                    case "SPEED": //HTTP-FTP-Downlink/Uplink  --during action--addedby omid 990107
                        double spd = 0;
                        if (double.TryParse(param.Split(":")[1], out spd))
                        {
                            return new string[] { "Speed", spd.ToString() };
                        }
                        return new string[] { "Speed", "NuNu" };
                    case "ElapsedTime": //HTTP-FTP-Downlink/Uplink --compelete Action --addedby omid 990107
                        double ept = 0;
                        if (double.TryParse(param.Split(":")[1], out ept))
                        {
                            return new string[] { "ElapsedTime", ept.ToString() };
                        }
                        return new string[] { "ElapsedTime", "NuNu" };
                    case "AvrgSpeed": //HTTP-FTP-Downlink/Uplink  --compelete Action--addedby omid 990107
                        double asp = 0;
                        if (double.TryParse(param.Split(":")[1], out asp))
                        {
                            return new string[] { "AvrgSpeed", asp.ToString() };
                        }
                        return new string[] { "AvrgSpeed", "NuNu" };
                    case "FileName": //MosCall Params , Name Of wav file                     
                        String FileName = param.Split(":")[1];
                        var ar = FileName.Split('/');
                        if (!string.IsNullOrEmpty(TcpSettings.serverPath))
                        {
                            return new string[] { "FileName", TcpSettings.serverPath + "/" + ar[ar.Length - 1] };
                        }
                        return new string[] { "FileName", ar[ar.Length - 1] };
                    case "FileNameL3": //Layer3" ,Name Of txt file                        
                        var arr = param.Split(":")[1].Split('/');
                        if (!string.IsNullOrEmpty(TcpSettings.serverPath))
                        {
                            return new string[] { "FileName", TcpSettings.serverPath + "/" + arr[arr.Length - 1] };
                        }
                        return new string[] { "FileName", arr[arr.Length - 1] };
                    case "FileSize": //MosCall Params , Size Of wav file 
                    case "FileSizeL3"://layer3 , Size of txt file
                        int FileSize = 0;
                        if (int.TryParse(param.Split(":")[1], out FileSize))
                        {
                            return new string[] { "FileSize", FileSize.ToString() };
                        }
                        return new string[] { "FileSize", "NuNu" };
                    case "mosFile": //MosCall Params , Path oF File 
                    case "ServerL3"://layer3 params, Path of File
                        TcpSettings.serverPath = param.Split(":")[1];
                        break;
                    case "Up_time_redirect": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double  sd))
                        {
                            return new string[] { "Up_time_redirect", sd.ToString() };
                        }
                        return new string[] { "Up_time_redirect", "NuNu" };
                    case "Up_time_namelookup": //Upload,Download  
                        {
                            if (double.TryParse(param.Split(":")[1], out double  utm))
                            {
                                return new string[] { "Up_time_namelookup", utm.ToString() };
                            }
                            return new string[] { "Up_time_namelookup", "NuNu" };
                        }
                    case "Up_time_connect": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double  utr))
                        {
                            return new string[] { "Up_time_connect", utr.ToString() };
                        }
                        return new string[] { "Up_time_connect", "NuNu" };
                    case "Up_time_appconnect": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double uta))
                        {
                            return new string[] { "Up_time_appconnect", uta.ToString() };
                        }
                        return new string[] { "Up_time_appconnect", "NuNu" };
                    case "Up_time_pretransfer": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double utp))
                        {
                            return new string[] { "Up_time_pretransfer", utp.ToString() };
                        }
                        return new string[] { "Up_time_pretransfer", "NuNu" };

                    case "Up_time_starttransfer": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double uts))
                        {
                            return new string[] { "Up_time_starttransfer", uts.ToString() };
                        }
                        return new string[] { "Up_time_starttransfer", "NuNu" };
                    case "Up_time_total": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double utt))
                        {
                            return new string[] { "Up_time_total", utt.ToString() };
                        }
                        return new string[] { "Up_time_total", "NuNu" };
                    case "Up_size_request": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double usr))
                        {
                            return new string[] { "Up_size_request", usr.ToString() };
                        }
                        return new string[] { "Up_size_request", "NuNu" };
                    case "Up_size_upload": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double usu))
                        {
                            return new string[] { "Up_size_upload", usu.ToString() };
                        }
                        return new string[] { "Up_size_upload", "NuNu" };
                    case "Up_size_download": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double usd))
                        {
                            return new string[] { "Up_size_download", usd.ToString() };
                        }
                        return new string[] { "Up_size_download", "NuNu" };
                    case "Up_size_header": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double ush))
                        {
                            return new string[] { "Up_size_header", ush.ToString() };
                        }
                        return new string[] { "Up_size_header", "NuNu" };
                    case "Up_http_version": //Upload,Download                                            
                        return new string[] { "Up_http_version", param.Split(":")[1] };
                    case "Up_redirect_url": //Upload,Download                                           
                        return new string[] { "Up_redirect_url", param.Split(":")[1] };
                    case "Up_remote_ip": //Upload,Download                                           
                        return new string[] { "Up_remote_ip", param.Split(":")[1] };
                    case "Up_remote_port": //Upload,Download                    
                        if (int.TryParse(param.Split(":")[1], out int pr))
                        {
                            return new string[] { "Up_remote_port", pr.ToString() };
                        }
                        return new string[] { "Up_remote_port", "NuNu" };
                    case "Up_scheme": //Upload,Download                                           
                        return new string[] { "Up_scheme", param.Split(":")[1] };
                    case "Up_speed_download": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double uud))
                        {
                            return new string[] { "Up_speed_download", uud.ToString() };
                        }
                        return new string[] { "Up_speed_download", "NuNu" };
                    case "Up_speed_upload": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double uss))
                        {
                            return new string[] { "Up_speed_upload", uss.ToString() };
                        }
                        return new string[] { "Up_speed_upload", "NuNu" };
                    case "Dl_time_redirect": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double tuu))
                        {
                            return new string[] { "Dl_time_redirect", tuu.ToString() };
                        }
                        return new string[] { "Dl_time_redirect", "NuNu" };
                    case "Dl_time_namelookup": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dtt))
                        {
                            return new string[] { "Dl_time_namelookup", dtt.ToString() };
                        }
                        return new string[] { "Dl_time_namelookup", "NuNu" };
                    case "Dl_time_connect": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dtc))
                        {
                            return new string[] { "Dl_time_connect", dtc.ToString() };
                        }
                        return new string[] { "Dl_time_connect", "NuNu" };
                    case "Dl_time_appconnect": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dta))
                        {
                            return new string[] { "Dl_time_appconnect", dta.ToString() };
                        }
                        return new string[] { "Dl_time_appconnect", "NuNu" };
                    case "Dl_time_pretransfer": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dtp))
                        {
                            return new string[] { "Dl_time_pretransfer", dtp.ToString() };
                        }
                        return new string[] { "Dl_time_pretransfer", "NuNu" };
                    case "Dl_time_starttransfer": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dts))
                        {
                            return new string[] { "Dl_time_starttransfer", dts.ToString() };
                        }
                        return new string[] { "Dl_time_starttransfer", "NuNu" };
                    case "Dl_time_total": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double ltt))
                        {
                            return new string[] { "Dl_time_total", ltt.ToString() };
                        }
                        return new string[] { "Dl_time_total", "NuNu" };
                    case "Dl_size_request": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double drs))
                        {
                            return new string[] { "Dl_size_request", drs.ToString() };
                        }
                        return new string[] { "Dl_size_request", "NuNu" };
                    case "Dl_size_upload": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dis))
                        {
                            return new string[] { "Dl_size_upload", dis.ToString() };
                        }
                        return new string[] { "Dl_size_upload", "NuNu" };
                    case "Dl_size_download": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double sdi))
                        {
                            return new string[] { "Dl_size_download", sdi.ToString() };
                        }
                        return new string[] { "Dl_size_download", "NuNu" };
                    case "Dl_size_header": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dsh))
                        {
                            return new string[] { "Dl_size_header", dsh.ToString() };
                        }
                        return new string[] { "Dl_size_header", "NuNu" };
                    case "Dl_http_version": //Upload,Download                                           
                        return new string[] { "Dl_http_version", param.Split(":")[1] };
                    case "Dl_redirect_url": //Upload,Download                                            
                        return new string[] { "Dl_redirect_url", param.Split(":")[1] };
                    case "Dl_remote_ip": //Upload,Download                                            
                        return new string[] { "Dl_remote_ip", param.Split(":")[1] };
                    case "Dl_remote_port": //Upload,Download                    
                        if (int.TryParse(param.Split(":")[1], out int drp))
                        {
                            return new string[] { "Dl_remote_port", drp.ToString() };
                        }
                        return new string[] { "Dl_remote_port", "NuNu" };
                    case "Dl_scheme": //Upload,Download                                           
                        return new string[] { "Dl_scheme", param.Split(":")[1] };
                    case "Dl_speed_download": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dsd))
                        {
                            return new string[] { "Dl_speed_download", dsd.ToString() };
                        }
                        return new string[] { "Dl_speed_download", "NuNu" };
                    case "Dl_speed_upload": //Upload,Download                    
                        if (double.TryParse(param.Split(":")[1], out double dsp))
                        {
                            return new string[] { "Dl_speed_upload", dsp.ToString() };
                        }
                        return new string[] { "Dl_speed_upload", "NuNu" };
                    case "ActiveSET":                        
                    case "SyncNeighborSET":                        
                    case "AsyncNeighborSET":
                        return param.Split(":");
                    default:
                        break;
                }
            }
            return null;
        }
        private static async Task UpdateMachineTestStatusToFinish(StateObject state, string testId)
        {
            AsynchronousSocketListener.SendedTest.Add(testId);
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                            _ = LogErrorAsync(ex, "2115  UpdateMachineTestStatusToFinish", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
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
                            _ = LogErrorAsync(ex, " 2134  UpdateMachineTestStatusToFinish", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
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
                using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                                _ = UpdatePreviousTestFinishTime(state, testId).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _ = LogErrorAsync(ex, "861  UpdateMachineTestStatusToRunning", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
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
                                _ = LogErrorAsync(ex, "881  UpdateMachineTestStatusToRunning", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

                await LogErrorAsync(ex, "889  UpdateMachineTestStatusToRunning", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
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
            //ToDo:omid: پس از تعریف تست جدید برای دستگاه درصورت هم پوشانی زمانی با تست از قبل تعریف شده و درحال اجرا نیاز است که تست قبلی بروزرسانی شود:990626
            string sql = $"declare @bdate datetime,@enddate datetime,@machineId int; " +
               $" begin select @bdate = BeginDate , @enddate = EndDate , @machineId=MachineId from DefinedTestMachine  where id =@Id; " +
                  $" update DefinedTestMachine " +
                  $" set Status = 1, FinishTime = @bdate " +
                  $" where status = 1 and MachineId = @machineId and FinishTime is null " +
                  $" and (EndDate >= @bdate and BeginDate <= @enddate) and id <> @Id   end";
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                        _ = LogErrorAsync(ex, "861  UpdatePreviousTestFinishTime", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
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

                    //     _=LogErrorAsync(ex, "912  ProcessMIDRecievedContent", state.IMEI1).ConfigureAwait(false);
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
                    if (!string.IsNullOrEmpty(Array.Find(paramArray, element => element.Contains("FWVer"))))
                    {
                        var FWVer = Array.Find(paramArray, element => element.Contains("FWVer"));
                        var TDD = Array.Find(paramArray, element => element.Contains("TDD"));
                        if (!string.IsNullOrEmpty(FWVer)) //version Exist
                        {
                            _ = Util.UpdateMachineStateByVersion(state.IMEI1, state.IMEI2, true, 
                                !string.IsNullOrEmpty(FWVer)?FWVer.Split(":")[1]:string.Empty
                                ,!string.IsNullOrEmpty(TDD)?TDD.Split(":")[1]:string.Empty );
                        }                        
                    }
                    else //Version Don't Exist
                    {
                        _ = Util.UpdateMachineState(state.IMEI1, state.IMEI2, true);
                    }
                    ShowMessage($"Get MID FRom  IMEI1={state.IMEI1}/IP={state.IP}", ConsoleColor.Cyan, ConsoleColor.Green, state.IMEI1);                   
                    // _ = Util.SyncDevice(state);
                }

            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "932  ProcessMIDRecievedContent", $"IMEI1={state.IMEI1} Ip={state.IP}").ConfigureAwait(false);
            }
        }
        private static string[] _CaptchaList = new string[100];
        static string SaltKey = "sample_salt";
        public static string Encrypt(this string plainText, string passwordHash)
        {
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);

            byte[] keyBytes = new Rfc2898DeriveBytes(passwordHash, Encoding.ASCII.GetBytes(SaltKey), 1024).GetBytes(16);
            var symmetricKey = new RijndaelManaged() { Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 };
            var encryptor = symmetricKey.CreateEncryptor(keyBytes, Convert.FromBase64String(TcpSettings.VIKey));

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
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
            {
                try
                {
                    string sql = $"SELECT -1 * DTMG.Id Id, dt.Title, dt.Layer3Messages, case when dt.Layer3Messages =1 then l3Host end ServerUrlL3, dt.RepeatTypeId, dt.RepeatTime, dt.RepeatCount, dt.MeasurementInterval, dt.TestTypeId, dt.UsualCallDuration, " +
                        $" dt.UsualCallWaitTime, dt.UsualCallNumber, dt.TestDataId, dt.TestDataTypeId, replace(replace(replace(case when (dt.DlFileAddress is null or dt.DlFileAddress = N'' ) then  " +
                        $" dt.DlServer else dt.DlServer + N'/' + dt.DlFileAddress end ,N'//',N'/'),N'https:/',N''),N'http:/',N'') as DlServer, dt.DlUserName, dt.DlPassword,dt.DlTime, " +
                        $" replace(replace(replace(case when (dt.UpFileAddress is null or dt.UpFileAddress = N'' ) then  " +
                        $" dt.UpServer else dt.UpServer + N'/' + dt.UpFileAddress end ,N'//',N'/'),N'https:/',N''),N'http:/',N'') as UpServer ,  dt.UpTime, dt.UpFileSize,dt.UpUserName,dt.UpPassword, " +
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
                        command.Parameters.AddWithValue("@l3Host", TcpSettings.l3Host);
                        command.Parameters.AddWithValue("@IMEI1", stateObject.IMEI1);
                        connection.Open();
                        definedTest = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {

                    _ = LogErrorAsync(ex, "996   SendWaitingGroupTest", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
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
                    var content = ("TST#" + item.ToString().Replace("}", "").Replace(",", "#").Replace("{", "")).Replace(" ", "").Replace("\r", "").Replace("\n", "").ToString().Encrypt("sample_shared_secret");
                    if (AsynchronousSocketListener.SendedTest.Find(t => t.Contains(content)) == null)
                    {
                        AsynchronousSocketListener.Send(stateObject.workSocket, TcpSettings.VIKey + " ," + content);
                        ShowMessage($"Send Test To IMEI1={stateObject.IMEI1}/IP={stateObject.IP}", ConsoleColor.White, ConsoleColor.Green, stateObject.IMEI1);
                    }
                }
            }
        }
        internal static async Task SendWaitingTest(StateObject stateObject)
        {
            string definedTest = "";
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
            {
                try
                {
                    string sql = $"SELECT DTM.Id Id, dt.Title, dt.Layer3Messages, case when dt.Layer3Messages =1 then @l3MessHost end TestDataServerL3, " +
                        $" dt.RepeatTypeId, dt.RepeatTime, dt.RepeatCount, dt.MeasurementInterval, dt.TestTypeId, dt.UsualCallDuration, " +
                        $" dt.UsualCallWaitTime, dt.UsualCallNumber, dt.TestDataId, dt.TestDataTypeId, replace(replace(replace(case when (dt.DlFileAddress is null or dt.DlFileAddress = N'' ) then  " +
                        $" dt.DlServer else dt.DlServer + N'/' + dt.DlFileAddress end ,N'//',N'/'),N'https:/',N''),N'http:/',N'') as DlServer, dt.DlUserName, dt.DlPassword,dt.DlTime, " +
                        $" replace(replace(replace(case when (dt.UpFileAddress is null or dt.UpFileAddress = N'' ) then  " +
                        $" dt.UpServer else dt.UpServer + N'/' + dt.UpFileAddress end ,N'//',N'/'),N'https:/',N''),N'http:/',N'') as UpServer ,  dt.UpTime, dt.UpFileSize,dt.UpUserName,dt.UpPassword, " +
                        $" dt.IPTypeId, dt.OTTServiceId, dt.OTTServiceTestId, dt.NetworkId, dt.BandId , dt.SaveLogFile, dt.LogFilePartitionTypeId, dt.LogFilePartitionTime, " +
                        $" dt.LogFilePartitionSize, dt.LogFileHoldTime, dt.NumberOfPings, dt.PacketSize, dt.InternalTime, dt.ResponseWaitTime, dt.TTL,  DTM.SIM, " +
                        $"            case when TesttypeId not in(4, 2) then testtypeid " +
                        $"             when TestTypeId = 2 then '2' + cast(TestDataTypeId as nvarchar(10)) " +
                        $"             when TestTypeId = 4 then '4' + " +
                        $"				case when testdataid in(3, 4) then cast(TestDataId as nvarchar(10)) " +
                        $"                     else cast(TestDataId as nvarchar(10)) + cast(TestDataTypeId as nvarchar(10)) end end TestType, " +
                        $" replace(CONVERT(varchar(26),DTM.BeginDate, 121),N':',N'-') BeginDate, " +
                        $" replace(CONVERT(varchar(26),DTM.EndDate, 121),N':',N'-') EndDate " +
                        $"from Machine M " +
                        $"join DefinedTestMachine DTM on M.Id = DTM.MachineId " +
                        $"join DefinedTest DT on DTM.DefinedTestId = DT.id " +
                        $"where" +
                        $" DTM.IsActive = 1 and " +
                        $" DTM.BeginDate > getdate() and " +
                        $" DTM.Status = 0 " +/*status = 0, not test*/
                        $" and m.IMEI1 = @IMEI1 for json path";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@l3MessHost", TcpSettings.l3Host);
                        command.Parameters.AddWithValue("@IMEI1", stateObject.IMEI1);
                        connection.Open();
                        definedTest = (string)await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1052  SendWaitingTest", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
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
                    var content = ("TST#" + item.ToString().Replace("}", "").Replace(",", "#").Replace("{", "")).Replace(" ", "").Replace("\r", "").Replace("\n", "").ToString().
                               Encrypt("sample_shared_secret");
                    //change content by testid --98-12-01
                    if (AsynchronousSocketListener.SendedTest.Find(t => t.Contains(content)) == null)
                    {
                        AsynchronousSocketListener.Send(stateObject.workSocket, TcpSettings.VIKey + " ," + content);
                        ShowMessage($"Send Test To IMEI1={stateObject.IMEI1}/IP={stateObject.IP}", ConsoleColor.White, ConsoleColor.Green, stateObject.IMEI1);
                        _ = LogErrorAsync(new Exception("Send Test"), content, string.Join("-", item));
                    }
                }
            }
        }
        internal static async Task UpdateVersion(StateObject stateObject)
        {
            string newUpdate = "";
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
            {
                try
                {
                    string sql = $"SELECT top 1 r.* " +
                        $" from MachineVersion r " +
                        $" where r.IMEI1 = @IMEI1 and r.IsDone = 0 " +
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
                    _ = LogErrorAsync(ex, "1117  UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
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
                    using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
                    {
                        try
                        {
                            //دستور آپدیت دستگاه با آدرس فایل مورد نظردر سرور
                            var content = ("UPD#\"VID\":" + curReq[0]["Id"] + "#\"TestDataServer\":" + "\"" + curReq[0]["FileDownloadAddress"] + "\"")
                                                                   .Encrypt("sample_shared_secret");
                            if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateObject.IMEI1))
                            {
                                connection.Open();
                                sql = await Trans_AddVersionDetail(stateObject, curReq, connection, sql, content);
                                ShowMessage($"Send UPD To IMEI1={stateObject.IMEI1}/IP={stateObject.IP}", ConsoleColor.Yellow, ConsoleColor.Green, stateObject.IMEI1);                                
                                _ = Util.LogErrorAsync(new Exception($"UPD Send To IMEI1=  {stateObject.IMEI1}"), content, $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, "1147  UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
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
                    using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                                    var content = ("UPD#\"VID\":" + curReq[0]["Id"] + "#\"TestDataServer\":" + "\"" + curReq[0]["FileDownloadAddress"] + "\"")
                                                              .Encrypt("sample_shared_secret");
                                    try
                                    {
                                        if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateObject.IMEI1))
                                        {
                                            sql = await Trans_AddVersionDetail(stateObject, curReq, connection, sql, content);
                                            ShowMessage($"Send UPD To IMEI1={stateObject.IMEI1}/IP={stateObject.IP}", ConsoleColor.Yellow, ConsoleColor.Green, stateObject.IMEI1);                                            
                                            _ = Util.LogErrorAsync(new Exception($"UPD Send To IMEI1=  {stateObject.IMEI1}"), content, $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "1193  UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
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
                                            // TimeSpan diffGT2 = DateTime.Now - UPDCreateDate;
                                            // if (diffGT2.Seconds > 60)
                                            // {
                                            try
                                            {
                                                int detail_UpdId; int.TryParse(curUPDRec[0]["Id"].ToString(), out detail_UpdId);
                                                sql = await Trans_DelVersionDetail(curReq, connection, sql, detail_UpdId, stateObject.IMEI1);
                                                //ارسال دوباره فرمان آپدیت                                        
                                                //دستور آپدیت دستگاه با آدرس فایل مورد نظردر سرور
                                                var content = ("UPD#\"VID\":" + curReq[0]["Id"] + "#\"TestDataServer\":" + "\"" + curReq[0]["FileDownloadAddress"] + "\"")
                                                                                            .Encrypt("sample_shared_secret");
                                                try
                                                {
                                                    if (AsynchronousSocketListener.DeviceList.Exists(x => x.IMEI1 == stateObject.IMEI1))
                                                    {
                                                        sql = await Trans_AddVersionDetail(stateObject, curReq, connection, sql, content);
                                                        ShowMessage($"Send UPD To IMEI1={stateObject.IMEI1}/IP={stateObject.IP}", ConsoleColor.Yellow, ConsoleColor.Green, stateObject.IMEI1);                                                       
                                                        _ = Util.LogErrorAsync(new Exception($"UPD Send To IMEI1={stateObject.IMEI1} IP={stateObject.IP}"), content, $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    _ = LogErrorAsync(ex, "1236  UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                _ = LogErrorAsync(ex, "1241  UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                            }
                                            // }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _ = LogErrorAsync(ex, "1248  UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _ = LogErrorAsync(ex, "1255  UpdateVersion", $"IMEI1={stateObject.IMEI1} Ip={stateObject.IP}");
                        }
                        finally
                        {
                            connection.Close();
                        }
                    }
                }
            }
        }
        public static async Task<string> Trans_AddVersionDetail(StateObject stateObject, JToken item, SqlConnection connection, string sql, string content)
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
                    AsynchronousSocketListener.Send(stateObject.workSocket, TcpSettings.VIKey + " ," + content);
                    //}
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1299  Trans_AddVersionDetail", stateObject.IMEI1);
                    tx.Rollback();
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "1309  Trans_AddVersionDetail", stateObject.IMEI1);
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
                    ShowMessage($"Send UPD To IMEI1={IMEI1} again.Beacuse device Don't Respond ", ConsoleColor.Yellow, ConsoleColor.Green, IMEI1);                   
                    _ = Util.LogErrorAsync(new Exception("Send UPD To Device after 2 minute again"), "Send UPD To Device after 2 minute again", IMEI1).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1339  Trans_DelVersionDetail", IMEI1);
                    tx.Rollback();
                }
            }
            catch (Exception ex)
            {
                _ = LogErrorAsync(ex, "1345  Trans_DelVersionDetail", IMEI1);
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
        internal static async Task UpdateMachineState(string IMEI1, string IMEI2, bool IsConnected, string machineVersion = "")
        {
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
            {
                try
                {
                    var machineVersionCheck = !string.IsNullOrEmpty(machineVersion) ? ", Version = @Version" : string.Empty;
                    string sql = $"if not exists(select 1 from machine where IMEI1 = @IMEI1) " +
                        $"begin " +
                        $" insert into machine(IMEI1, IMEI2, MachineTypeId) select @IMEI1, @IMEI2, 1 " +
                        $"end " +
                        $"update machine set IsConnected = @IsConnected {machineVersion} where IMEI1 = @IMEI1 " +
                        $"insert into MachineConnectionHistory( MachineId, IsConnected) values " +
                        $" ((select id from  machine where IMEI1 = @IMEI1 ), @IsConnected)";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IsConnected", IsConnected);
                        command.Parameters.AddWithValue("@IMEI1", IMEI1);
                        command.Parameters.AddWithValue("@IMEI2", IMEI2);
                        if (!string.IsNullOrEmpty(machineVersion))
                        {
                            command.Parameters.AddWithValue("@Version", machineVersion);
                        }
                        connection.Open();
                        await command.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1393  UpdateMachineState", IMEI1);
                }
                finally
                {
                    connection.Close();
                }
            }
        }
        internal static async Task UpdateMachineStateByVersion(string IMEI1, string IMEI2, bool IsConnected, string machineVersion = "",string TDD="")
        {
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
            {
                try
                {
                    string sql = $"if not exists(select 1 from machine where IMEI1 = @IMEI1) " +
                        $"begin " +
                        $" insert into machine(IMEI1, IMEI2, MachineTypeId) select @IMEI1, @IMEI2, 1 " +
                        $"end " +
                        $"update machine set IsConnected = @IsConnected , Version = @Version , Tdd = @TDD where IMEI1 = @IMEI1 " +
                        $"insert into MachineConnectionHistory( MachineId, IsConnected) values " +
                        $" ((select id from  machine where IMEI1 = @IMEI1 ), @IsConnected)";
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.CommandTimeout = 100000;
                        command.CommandType = CommandType.Text;
                        command.Parameters.AddWithValue("@IsConnected", IsConnected);
                        command.Parameters.AddWithValue("@IMEI1", IMEI1);
                        command.Parameters.AddWithValue("@IMEI2", IMEI2);
                        command.Parameters.AddWithValue("@Version", machineVersion);
                        command.Parameters.AddWithValue("@TDD", TDD);

                        connection.Open();
                        await command.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    _ = LogErrorAsync(ex, "1393  UpdateMachineState", IMEI1);
                }
                finally
                {
                    connection.Close();
                }
            }
        }
        internal static async Task UpdateMachineLocation(string IMEI1, string IMEI2, string lat, string lon, double speed = 0.0, double speed2 = 0.0, double altitude = 0.0, DateTime? DateFromDevice = null, float? CpuTemprature = null)
        {
            using (SqlConnection connection = new SqlConnection(TcpSettings.ConnectionString))
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
                            //null for DateTime has err:SqlDateTime overflow. Must be between 1/1/1753 12:00:00 AM and 12/31/9999 11:59:59 PM.
                            // command.Parameters.AddWithValue("@fromDevice", DBNull.Value);
                            command.Parameters.AddWithValue("@fromDevice", DateTime.Now);
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
                    _ = LogErrorAsync(ex, "1393  UpdateMachineLocation", IMEI1);
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
            using (SqlConnection con = new SqlConnection(TcpSettings.ConnectionString))
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

        public static void ShowMessage(string msg,ConsoleColor colorBefore=ConsoleColor.White,ConsoleColor colorAfter=ConsoleColor.Green,string imei="All")
        {
            if (TcpSettings.Imei1Log == "All" || imei == TcpSettings.Imei1Log )
            {
                Console.ForegroundColor = colorBefore;
                ConsolePrint.PrintLine('*');
                Console.WriteLine($"{msg} @{DateTime.Now.ToString("yyyy/M/d HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)} ,ServerUTC @{DateTime.UtcNow.ToString("yyyy/M/d HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}");
                //ConsolePrint.PrintLine('*');
                Console.ForegroundColor = colorAfter;
            }
        }

    }
}
