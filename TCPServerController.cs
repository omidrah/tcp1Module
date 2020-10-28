using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace TCPServer
{
    [Route("TCPServer")]
    [Produces("application/json")]
    //add by omid , for report on tcpserver by api
    public class DeviceController
    { 
        [HttpGet]
        [Route("[action]")]
        public int OnlineCnt()
        {         
            return AsynchronousSocketListener.DeviceList.Count;                            
        }
        [HttpGet]
        [Route("[action]")]
        public List<string> GetOnlineImeI1()
        {
            var res = new List<string>();
            foreach (var item in AsynchronousSocketListener.DeviceList)
            {
                res.Add(item.IMEI1);
            }
            return res;
        }
    }
}
