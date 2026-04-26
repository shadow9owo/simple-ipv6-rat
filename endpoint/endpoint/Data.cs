using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace endpoint
{
    public class Data
    {
        public enum PacketType
        {
            exec,
            download,
            recv,
            endofdwnld,
            disconnect,
            echo
        }

        public static bool debug = false;

        public class Packet
        {
            public PacketType pt { get; set; }
            public string input { get; set; }
            public byte[] buffer { get; set; }
        }

        public class Config
        {
            public string ip { get; set; }
            public string port { get; set; }
        }
    }
}
