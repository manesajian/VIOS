using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VIOSDotNetClient
{
    public class Message
    {
        public string InstanceId { get; set; }
        public string Type { get; set; }
        public string MessageId { get; set; }
        public string Args { get; set; }

        public Message(string _InstanceId, string _Type, string _MessageId, string _Args)
        {
            InstanceId = _InstanceId;
            Type = _Type;
            MessageId = _MessageId;
            Args = _Args;
        }
    }
}
