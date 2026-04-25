using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace test1
{
    public class Data
    {
        public static Uri endpoint = new Uri("ws://[ipv6]:8080"); //any ipv6 address will do
    }

    public enum PacketType
    {
        exec,
        download,
        recv,
        endofdwnld,
        disconnect,
        echo
    }

    public class Packet
    {
        public PacketType pt { get; set; }
        public string input { get; set; }
        public byte[] buffer { get; set; }
    }
}
